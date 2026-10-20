using MiniERP2.DataLoaders;
using MiniERP2.Models;
using static MiniERP2.DataLoaders.ExportQuarterSourceLoader;
using static MiniERP2.DataLoaders.ExportSummaryLoader;

namespace MiniERP2.Services;

/// <summary>
/// 분기 부가세용 수출요약보고서(26년2분기 보고서 양식)를 분기 원본 폴더 하나로 만든다.
/// 폴더 안 파일을 종류별로 자동 판별해 마켓×연월×지표(매출액/신고액/정산액)×통화 표와 원본 시트, 수출신고 대사를 만든다.
///
///   매출액: 쇼피 = 구매확정일 기준 소비자 실결제액(Order.completed), 아마존 = 거래내역 CSV의 Income 합계, 자사몰 = PayPal Gross
///   신고액: 수출신고실적(통화→마켓; USD는 아마존 미국 FBA 입고분)
///   정산액: 페이오니어(Description 규칙), 자사몰 = PayPal Net
/// 기간은 수출신고 파일이 걸친 달로 정한다(분기 3개월 파일 → 그 분기).
/// </summary>
public static class ExportQuarterReportBuilder
{
    public const string Sales = "매출액";
    public const string Declared = "신고액";
    public const string Settled = "정산액";

    public record TidyRow(string MarketCode, string MarketName, string YearMonth, string Indicator, string Currency, decimal Amount, string Note);

    public record Report(
        DateTime PeriodStart, DateTime PeriodEndExclusive,
        List<TidyRow> Tidy,
        List<RawTable> DeclarationTables, List<(RawTable Table, List<PayoneerRow> Rows)> PayoneerTables,
        List<(RawTable Table, List<PaypalRow> Rows)> PaypalTables, List<(RawTable Table, List<WixOrder> Orders)> WixTables,
        List<AmazonMonthlyIncome> AmazonIncome,
        ExportDeclarationReconcileEngine.Result Reconcile,
        List<string> UsedFiles, List<string> Warnings);

    public static Report Build(string folder, ExportSummaryConfig config)
    {
        var warnings = new List<string>();
        var usedFiles = new List<string>();
        var declarations = new List<DeclarationRow>();
        var declarationTables = new List<RawTable>();
        var orders = new Dictionary<(string Market, string OrderId), SalesOrderRecord>();
        var payoneer = new List<(RawTable, List<PayoneerRow>)>();
        var paypal = new List<(RawTable, List<PaypalRow>)>();
        var amazon = new List<AmazonMonthlyIncome>();
        var wix = new List<(RawTable, List<WixOrder>)>();
        var duplicateOrders = 0;
        var seenDeclarationNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateDeclarations = 0;

        string CurrencyOf(string market) => config.Markets.FirstOrDefault(m => m.MarketCode == market)?.Currency ?? "";

        foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            var rel = Path.GetRelativePath(folder, file);
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (name.StartsWith("~$", StringComparison.Ordinal) || ext is not (".xlsx" or ".xls" or ".csv")) continue;

            try
            {
                // 1) 쇼피 주문(파일명 패턴)
                var baseName = Path.GetFileNameWithoutExtension(file);
                var mapping = config.SalesMarkets.FirstOrDefault(m =>
                    m.MatchesFileName(baseName));
                if (mapping is not null && ext != ".csv")
                {
                    if (mapping.Recon is null || string.IsNullOrWhiteSpace(mapping.OrderIdColumn))
                    {
                        warnings.Add($"{rel}: {mapping.MarketCode} 마켓은 주문 단위 설정이 없어 제외");
                        continue;
                    }
                    foreach (var o in LoadSalesOrders(file, mapping, CurrencyOf(mapping.MarketCode)))
                    {
                        // 이전 달 파일을 함께 넣어도 같은 주문은 한 번만
                        if (!orders.TryAdd((o.MarketCode, o.OrderId), o)) duplicateOrders++;
                    }
                    usedFiles.Add($"쇼피 주문: {rel}");
                    continue;
                }

                // 2) 수출신고(헤더 신고일자·결제금액·통화코드로 판별)
                List<DeclarationRow>? rows = null;
                try { rows = LoadDeclarationRows(file, config.DeclarationTrack); }
                catch (InvalidOperationException) { /* 수출신고 아님 */ }
                if (rows is { Count: > 0 })
                {
                    // 같은 신고번호가 여러 파일에 있으면(작업용 사본 등) 한 번만
                    var fresh = rows.Where(r => r.DeclarationNo.Length == 0 || seenDeclarationNos.Add(r.DeclarationNo)).ToList();
                    duplicateDeclarations += rows.Count - fresh.Count;
                    declarations.AddRange(fresh);
                    declarationTables.Add(ReadTable(file, config.DeclarationTrack.HeaderRow));
                    usedFiles.Add($"수출신고: {rel}");
                    continue;
                }

                // 3) 페이오니어·PayPal·Wix·아마존 거래내역(CSV·엑셀 모두 헤더로 판별)
                var kind = DetectSourceKind(file, config);
                switch (kind)
                {
                    case SourceKind.AmazonTransaction:
                        amazon.AddRange(LoadAmazonTransactions(file, config.AmazonTransactionTrack));
                        usedFiles.Add($"아마존 거래내역: {rel}");
                        break;
                    case SourceKind.Paypal:
                    {
                        var table = ReadSourceTable(file, kind, config);
                        paypal.Add((table, LoadPaypal(table, config.PaypalTrack)));
                        usedFiles.Add($"PayPal: {rel}");
                        break;
                    }
                    case SourceKind.WixOrders:
                    {
                        var table = ReadSourceTable(file, kind, config);
                        wix.Add((table, LoadWixOrders(table, config.WixOrderTrack)));
                        usedFiles.Add($"Wix 주문(PayPal 대조용): {rel}");
                        break;
                    }
                    case SourceKind.Payoneer:
                    {
                        var table = ReadSourceTable(file, kind, config);
                        payoneer.Add((table, LoadPayoneer(table, config.RemittanceTrack)));
                        usedFiles.Add($"페이오니어: {rel}");
                        break;
                    }
                    default:
                        // 엑셀은 보고서·작업용 사본 등이 섞여 있어 조용히 넘기고, CSV만 알린다.
                        if (ext == ".csv") warnings.Add($"{rel}: 종류를 알 수 없는 CSV라 건너뜀");
                        break;
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"{rel}: 읽기 오류 — {ex.Message}");
            }
        }

        if (declarations.Count == 0)
            throw new InvalidOperationException("폴더에서 수출신고실적 파일을 찾지 못했습니다. 보고서 기간을 신고 파일 기준으로 정하므로 신고 파일이 필요합니다.");

        var (start, end) = ExportDeclarationReconcileEngine.InferPeriod(declarations);
        bool InPeriod(DateTime? d) => d is { } v && v >= start && v < end;
        string Ym(DateTime d) => d.ToString("yyyy-MM");
        string NameOf(string market) => config.Markets.FirstOrDefault(m => m.MarketCode == market)?.MarketName ?? market;

        var tidy = new Dictionary<(string Market, string Ym, string Ind, string Cur, string Note), decimal>();
        void Add(string market, DateTime date, string ind, string cur, decimal amount, string note = "")
        {
            var key = (market, Ym(date), ind, cur, note);
            tidy[key] = tidy.GetValueOrDefault(key) + amount;
        }

        // 매출액: 쇼피(구매확정일)
        foreach (var o in orders.Values.Where(o => InPeriod(o.CompletedAt)))
            Add(o.MarketCode, o.CompletedAt!.Value, Sales, o.Currency, o.PaidAmount);

        // 신고액
        foreach (var d in declarations.Where(d => InPeriod(d.Date)))
        {
            if (!config.DeclarationTrack.CurrencyToMarket.TryGetValue(d.Currency, out var market))
            {
                market = $"미분류({d.Currency})";
                warnings.Add($"신고 {d.DeclarationNo} {d.Currency} {d.Amount:N2}: 통화에 대응하는 마켓이 설정에 없음");
            }
            Add(market, d.Date, Declared, d.Currency, d.Amount);
        }

        // 정산액: 페이오니어
        foreach (var (table, rows) in payoneer)
        {
            foreach (var r in rows.Where(r => !r.Excluded && InPeriod(r.Date)))
            {
                if (r.MarketCode is null)
                {
                    warnings.Add($"페이오니어 미분류: {r.Date:yyyy-MM-dd} \"{r.Description}\" {r.Amount:N2} {r.Currency} (설정의 송금 규칙 추가 필요)");
                    continue;
                }
                Add(r.MarketCode, r.Date!.Value, Settled, r.Currency, r.Amount, "페이오니어");
            }
        }

        // 자사몰: PayPal
        foreach (var (_, rows) in paypal)
        {
            foreach (var r in rows.Where(r => r.Included && InPeriod(r.Date)))
            {
                Add(config.PaypalTrack.MarketCode, r.Date!.Value, Sales, r.Currency, r.Gross);
                Add(config.PaypalTrack.MarketCode, r.Date!.Value, Settled, r.Currency, r.Net, "페이팔");
            }
        }

        // 매출액: 아마존(거래내역 CSV)
        var amazonInPeriod = amazon.Where(a => string.CompareOrdinal(a.YearMonth, Ym(start)) >= 0 && string.CompareOrdinal(a.YearMonth, Ym(end)) < 0).ToList();
        foreach (var g in amazonInPeriod.GroupBy(a => (a.MarketCode, a.YearMonth, a.Currency)))
        {
            if (g.Count() > 1) warnings.Add($"아마존 {NameOf(g.Key.MarketCode)} {g.Key.YearMonth}: 거래내역 파일 {g.Count()}개가 같은 달을 포함 — 중복 합산 여부 확인");
            var key = (g.Key.MarketCode, g.Key.YearMonth, Sales, g.Key.Currency, "");
            tidy[key] = tidy.GetValueOrDefault(key) + g.Sum(a => a.Income);
        }

        // 빠진 자료 경고
        var months = Enumerable.Range(0, 12).Select(i => start.AddMonths(i)).TakeWhile(m => m < end).Select(Ym).ToList();
        var monthsWithoutDeclaration = months.Where(m => !declarations.Any(d => Ym(d.Date) == m)).ToList();
        if (monthsWithoutDeclaration.Count > 0)
            warnings.Add($"수출신고 내역이 없는 달: {string.Join(", ", monthsWithoutDeclaration)} — 해당 월 수출신고실적목록조회 파일 누락 여부 확인");
        if (duplicateDeclarations > 0) warnings.Add($"여러 파일에 중복된 수출신고 {duplicateDeclarations}건(같은 신고번호)은 한 번만 집계함");
        foreach (var market in config.AmazonTransactionTrack.CurrencyToMarket.Values.Distinct())
        {
            var missing = months.Where(m => !amazonInPeriod.Any(a => a.MarketCode == market && a.YearMonth == m)).ToList();
            if (missing.Count > 0)
                warnings.Add($"{NameOf(market)} 매출액 없음({string.Join(", ", missing)}): 아마존 거래내역 CSV(Date Range Report → Transaction)를 폴더에 넣으세요");
        }
        // Wix 주문 ↔ PayPal 대조: 결제된 Wix 주문이 PayPal 내역에 없으면 PayPal을 다시 받아야 매출이 빠지지 않는다.
        var paypalUnused = paypal.SelectMany(p => p.Item2).Where(r => r.Included && r.Gross > 0 && r.Date.HasValue).ToList();
        // Wix 시각은 미국 기준이라 한국시간(PayPal 기준)으로 분기를 넘는 주문은 PayPal에서도 다음 분기 거래다.
        foreach (var o in wix.SelectMany(w => w.Item2).Where(o => o.Included && !InPeriod(o.Date) && InPeriod(o.LocalDate)))
            warnings.Add($"Wix 주문 #{o.OrderNumber}(미국 {o.LocalDate:MM-dd HH:mm} = 한국 {o.Date:MM-dd HH:mm}, {o.Total:N2} {o.Currency})은 PayPal(한국시간) 기준 기간 밖 — 다음 분기 자사몰 매출(누락 아님)");
        foreach (var o in wix.SelectMany(w => w.Item2).Where(o => o.Included && InPeriod(o.Date)).OrderBy(o => o.Date))
        {
            var hit = paypalUnused.FirstOrDefault(r => r.Gross == o.Total && Math.Abs((r.Date!.Value.Date - o.Date!.Value.Date).TotalDays) <= 3);
            if (hit is not null) paypalUnused.Remove(hit);
            else warnings.Add($"Wix 주문 #{o.OrderNumber}({o.Date:yyyy-MM-dd}, {o.Total:N2} {o.Currency})이 PayPal 내역에 없음 — PayPal 거래내역을 기간 끝까지 다시 받으세요(자사몰 매출 누락)");
        }

        if (paypal.Count == 0) warnings.Add("자사몰 PayPal 거래내역(Download.CSV)이 없어 자사몰 매출액·정산액이 비어 있음");
        if (payoneer.Count == 0) warnings.Add("페이오니어 거래내역 CSV가 없어 정산액이 비어 있음");
        foreach (var g in orders.Values.GroupBy(o => o.MarketCode))
        {
            // 주문파일은 주문일 기준: 기간 첫 달 전에 생성돼 기간 안에 구매확정된 주문이 빠지지 않게 이전 달 파일 필요
            var earliestCompleted = g.Where(o => o.CompletedAt.HasValue).Select(o => o.CompletedAt!.Value).DefaultIfEmpty().Min();
            var earliestShipped = g.Where(o => o.ShippedAt.HasValue).Select(o => o.ShippedAt!.Value).DefaultIfEmpty().Min();
            if (earliestShipped != default && earliestShipped >= start && earliestCompleted >= start)
                warnings.Add($"{NameOf(g.Key)}: 기간 이전 주문 파일이 없음 — {start.AddMonths(-1):yyyy-MM}월 주문 중 {start:yyyy-MM}월에 구매확정된 매출이 빠졌을 수 있음(이전 달 Order.completed 파일 추가 권장)");
        }
        if (duplicateOrders > 0) warnings.Add($"여러 파일에 중복된 쇼피 주문 {duplicateOrders}건은 한 번만 집계함");

        var tidyRows = tidy
            .Where(kv => kv.Value != 0)
            .Select(kv => new TidyRow(kv.Key.Market, NameOf(kv.Key.Market), kv.Key.Ym, kv.Key.Ind, kv.Key.Cur, kv.Value, kv.Key.Note))
            .OrderBy(r => r.MarketName, StringComparer.Ordinal)
            .ThenBy(r => IndicatorOrder(r.Indicator)).ThenBy(r => r.Currency, StringComparer.Ordinal).ThenBy(r => r.YearMonth, StringComparer.Ordinal)
            .ToList();

        // 수출신고 대사(쇼피)
        var configuredRates = config.SalesMarkets
            .Where(m => m.Recon is { DeclaredRate: > 0 })
            .Select(m => (Currency: CurrencyOf(m.MarketCode), m.Recon!.DeclaredRate))
            .Where(x => x.Currency.Length > 0)
            .GroupBy(x => x.Currency)
            .ToDictionary(g => g.Key, g => g.First().DeclaredRate);
        var reconcile = ExportDeclarationReconcileEngine.Run(declarations, orders.Values.ToList(), start, end, configuredRates);

        return new Report(start, end, tidyRows, declarationTables, payoneer, paypal, wix, amazon, reconcile, usedFiles, warnings);
    }

    public static int IndicatorOrder(string indicator) => indicator switch
    {
        Sales => 0,
        Declared => 1,
        Settled => 2,
        _ => 9,
    };
}

using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 분기 수출요약보고서용 원자료 읽기. 각 파일을 "헤더 + 문자열 행" 표로 읽어(보고서에 원본 시트로도 그대로 싣기 위해)
/// 페이오니어·페이팔·아마존 거래내역을 지표 단위로 해석한다. 쇼피 주문·수출신고는 ExportSummaryLoader를 쓴다.
/// </summary>
public static class ExportQuarterSourceLoader
{
    public enum SourceKind { Unknown, Payoneer, Paypal, AmazonTransaction, WixOrders }

    /// <summary>파일 하나의 원본 표. Preamble은 헤더 위 머리말 줄(아마존 CSV의 "All amounts in USD" 등).</summary>
    public record RawTable(string FileName, List<string> Preamble, List<string> Headers, List<List<string>> Rows)
    {
        public int Col(string header) => Headers.FindIndex(h => string.Equals(h.Trim(), header.Trim(), StringComparison.OrdinalIgnoreCase));
        public string Get(List<string> row, int col) => col >= 0 && col < row.Count ? row[col] : "";
    }

    public record PayoneerRow(DateTime? Date, string Description, decimal Amount, string Currency, string? MarketCode, bool Excluded, List<string> Raw);
    public record PaypalRow(DateTime? Date, string Currency, decimal Gross, decimal Net, bool Included, List<string> Raw);
    /// <summary>Wix 주문. Date는 PayPal과 같은 한국시간, LocalDate는 Wix 화면의 현지(미국) 시각.</summary>
    public record WixOrder(string OrderNumber, DateTime? Date, string Currency, decimal Total, bool Included, List<string> Raw, DateTime? LocalDate = null);

    /// <summary>아마존 거래내역 월별 매출액(Income 합계)과 구성 항목.</summary>
    public record AmazonMonthlyIncome(string FileName, string MarketCode, string Currency, string YearMonth,
        Dictionary<string, decimal> Components, decimal Reimbursement)
    {
        public decimal Income => Components.Values.Sum() + Reimbursement;
    }

    /// <summary>엑셀/CSV 첫 시트를 표로 읽는다. headerRow가 0이면 headerFinder로 헤더 줄을 찾는다.</summary>
    public static RawTable ReadTable(string filePath, int headerRow = 1, Func<List<string>, bool>? headerFinder = null)
    {
        using var package = ExcelFileOpener.Open(filePath);
        var sheet = package.Workbook.Worksheets.FirstOrDefault()
                    ?? throw new InvalidOperationException("엑셀 파일에서 시트를 찾을 수 없습니다.");
        var lastRow = sheet.Dimension?.End.Row ?? 0;
        var lastCol = sheet.Dimension?.End.Column ?? 0;

        List<string> ReadRow(int r) => Enumerable.Range(1, lastCol).Select(c => sheet.Cells[r, c].Text ?? "").ToList();

        if (headerFinder is not null)
        {
            headerRow = 0;
            for (var r = 1; r <= Math.Min(lastRow, 40); r++)
            {
                if (headerFinder(ReadRow(r))) { headerRow = r; break; }
            }
            if (headerRow == 0) throw new InvalidOperationException("헤더 행을 찾지 못했습니다.");
        }

        var preamble = Enumerable.Range(1, Math.Max(0, headerRow - 1)).Select(r => string.Join(" ", ReadRow(r)).Trim()).ToList();
        var headers = headerRow <= lastRow ? ReadRow(headerRow).Select(h => h.Trim()).ToList() : new List<string>();
        while (headers.Count > 0 && headers[^1].Length == 0) headers.RemoveAt(headers.Count - 1);

        var rows = new List<List<string>>();
        for (var r = headerRow + 1; r <= lastRow; r++)
        {
            var row = ReadRow(r).Take(Math.Max(headers.Count, 1)).ToList();
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(row);
        }
        return new RawTable(Path.GetFileName(filePath), preamble, headers, rows);
    }

    /// <summary>원본 파일 종류를 헤더로 판별한다(CSV·엑셀 공통). 아마존은 헤더 위에 설명 줄이 있고, 페이오니어 엑셀도
    /// 머리말이 있을 수 있어 앞쪽 40줄 안에서 헤더 행을 찾는다.</summary>
    public static SourceKind DetectSourceKind(string filePath, ExportSummaryConfig config)
    {
        if (config.AmazonTransactionTrack.AllLayouts().Any(layout => TryReadTable(filePath, row => IsAmazonHeader(row, layout)) is not null))
            return SourceKind.AmazonTransaction;

        var paypal = config.PaypalTrack;
        if (TryReadTable(filePath, row => Has(row, paypal.GrossColumn) && Has(row, paypal.NetColumn) && Has(row, paypal.DateColumn)) is not null)
            return SourceKind.Paypal;

        var wix = config.WixOrderTrack;
        if (TryReadTable(filePath, row => Has(row, wix.OrderNumberColumn) && Has(row, wix.TotalColumn) && Has(row, wix.DateColumn)) is not null)
            return SourceKind.WixOrders;

        var remit = config.RemittanceTrack;
        if (TryReadTable(filePath, row => Has(row, remit.DescriptionColumn) && Has(row, remit.AmountColumn) && Has(row, remit.DateColumn)) is not null)
            return SourceKind.Payoneer;

        return SourceKind.Unknown;
    }

    /// <summary>종류별 헤더 조건으로 표를 읽는다(머리말 줄 건너뜀).</summary>
    public static RawTable ReadSourceTable(string filePath, SourceKind kind, ExportSummaryConfig config) => kind switch
    {
        SourceKind.Paypal => ReadTable(filePath, headerFinder: row => Has(row, config.PaypalTrack.GrossColumn) && Has(row, config.PaypalTrack.NetColumn)),
        SourceKind.WixOrders => ReadTable(filePath, headerFinder: row => Has(row, config.WixOrderTrack.OrderNumberColumn) && Has(row, config.WixOrderTrack.TotalColumn)),
        SourceKind.Payoneer => ReadTable(filePath, headerFinder: row => Has(row, config.RemittanceTrack.DescriptionColumn) && Has(row, config.RemittanceTrack.AmountColumn)),
        _ => ReadTable(filePath),
    };

    private static RawTable? TryReadTable(string filePath, Func<List<string>, bool> headerFinder)
    {
        try { return ReadTable(filePath, headerFinder: headerFinder); }
        catch (InvalidOperationException) { return null; }
    }

    private static bool Has(List<string> row, string header) => header.Length > 0 && row.Any(c => Same(c, header));

    private static bool IsAmazonHeader(List<string> row, AmazonTransactionLayout layout)
        => Has(row, layout.DateTimeColumn) && Has(row, layout.TypeColumn) && Has(row, layout.IncomeColumns.FirstOrDefault() ?? "");

    public static List<PayoneerRow> LoadPayoneer(RawTable table, RemittanceTrackConfig config)
    {
        int dateCol = table.Col(config.DateColumn), amountCol = table.Col(config.AmountColumn),
            descCol = table.Col(config.DescriptionColumn), curCol = table.Col(config.CurrencyColumn);
        var result = new List<PayoneerRow>();
        foreach (var row in table.Rows)
        {
            var description = table.Get(row, descCol).Trim();
            var excluded = config.ExcludeContains.Any(x => description.Contains(x, StringComparison.OrdinalIgnoreCase));
            var market = excluded ? null : config.Rules.FirstOrDefault(rule =>
                rule.Contains.All(c => description.Contains(c, StringComparison.OrdinalIgnoreCase)))?.MarketCode;
            var currency = table.Get(row, curCol).Trim();
            result.Add(new PayoneerRow(
                ExportSummaryLoader.ParseDateText(table.Get(row, dateCol)), description,
                ExportSummaryLoader.ParseAmountText(table.Get(row, amountCol)),
                currency.Length > 0 ? currency : "USD", market, excluded, row));
        }
        return result;
    }

    public static List<PaypalRow> LoadPaypal(RawTable table, PaypalTrackConfig config)
    {
        int dateCol = table.Col(config.DateColumn), curCol = table.Col(config.CurrencyColumn),
            grossCol = table.Col(config.GrossColumn), netCol = table.Col(config.NetColumn), statusCol = table.Col(config.StatusColumn);
        return table.Rows.Select(row =>
        {
            var included = string.IsNullOrWhiteSpace(config.IncludeStatus) || statusCol < 0
                           || string.Equals(table.Get(row, statusCol).Trim(), config.IncludeStatus, StringComparison.OrdinalIgnoreCase);
            return new PaypalRow(ExportSummaryLoader.ParseDateText(table.Get(row, dateCol)), table.Get(row, curCol).Trim(),
                ExportSummaryLoader.ParseAmountText(table.Get(row, grossCol)), ExportSummaryLoader.ParseAmountText(table.Get(row, netCol)),
                included, row);
        }).ToList();
    }

    /// <summary>Wix 주문내역. 상품 줄마다 주문 행이 반복될 수 있어 주문번호당 한 번만.</summary>
    public static List<WixOrder> LoadWixOrders(RawTable table, WixOrderTrackConfig config)
    {
        int noCol = table.Col(config.OrderNumberColumn), dateCol = table.Col(config.DateColumn), totalCol = table.Col(config.TotalColumn),
            curCol = table.Col(config.CurrencyColumn), statusCol = table.Col(config.PaymentStatusColumn), timeCol = table.Col(config.TimeColumn);
        TimeZoneInfo? source = null, korea = null;
        if (!string.IsNullOrWhiteSpace(config.TimeZoneId))
        {
            try
            {
                source = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId);
                korea = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
            }
            catch (TimeZoneNotFoundException) { source = korea = null; }
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<WixOrder>();
        foreach (var row in table.Rows)
        {
            var no = table.Get(row, noCol).Trim();
            if (no.Length == 0 || !seen.Add(no)) continue;
            var included = string.IsNullOrWhiteSpace(config.IncludePaymentStatus) || statusCol < 0
                           || string.Equals(table.Get(row, statusCol).Trim(), config.IncludePaymentStatus, StringComparison.OrdinalIgnoreCase);
            var currency = table.Get(row, curCol).Trim();
            var local = ExportSummaryLoader.ParseDateText(table.Get(row, dateCol));
            if (local is { } day && timeCol >= 0 && DateTime.TryParse(table.Get(row, timeCol), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var time))
                local = day.Date + time.TimeOfDay;
            var kst = local is { } l && source is not null && korea is not null
                ? TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(l, DateTimeKind.Unspecified), source, korea)
                : local;
            result.Add(new WixOrder(no, kst, currency.Length > 0 ? currency : "USD",
                ExportSummaryLoader.ParseAmountText(table.Get(row, totalCol)), included, row, local));
        }
        return result;
    }

    /// <summary>아마존 거래내역 CSV → 월별 Income 합계. 월은 거래 게시일(date/time, 현지 시간) 기준이라 요약 PDF의 기간과 같다.</summary>
    public static List<AmazonMonthlyIncome> LoadAmazonTransactions(string filePath, AmazonTransactionTrackConfig config)
    {
        // 형식(영어·일본어 등)을 차례로 시도해 헤더가 맞는 것으로 읽는다
        RawTable? table = null;
        AmazonTransactionLayout layout = config;
        foreach (var candidate in config.AllLayouts())
        {
            table = TryReadTable(filePath, row => IsAmazonHeader(row, candidate));
            if (table is not null) { layout = candidate; break; }
        }
        table ??= ReadTable(filePath, headerFinder: row => IsAmazonHeader(row, config));

        // 머리말 "All amounts in USD, unless specified"에서 통화, 없으면 형식의 기본 통화(일본: 円)
        var currency = table.Preamble
            .Select(p => System.Text.RegularExpressions.Regex.Match(p, @"All amounts in ([A-Z]{3})"))
            .FirstOrDefault(m => m.Success)?.Groups[1].Value ?? layout.DefaultCurrency;
        if (!config.CurrencyToMarket.TryGetValue(currency, out var marketCode))
            throw new InvalidOperationException($"아마존 거래내역 통화 {currency}에 대응하는 마켓이 설정에 없습니다.");

        int dateCol = table.Col(layout.DateTimeColumn), typeCol = table.Col(layout.TypeColumn), descCol = table.Col(layout.DescriptionColumn),
            reimbCol = table.Col(layout.ReimbursementAmountColumn);
        var incomeCols = layout.IncomeColumns.Select(c => (Name: c, Col: table.Col(c))).ToList();
        var missing = incomeCols.Where(c => c.Col < 0).Select(c => c.Name).ToList();
        if (dateCol < 0 || typeCol < 0 || missing.Count > 0)
            throw new InvalidOperationException($"아마존 거래내역 헤더를 찾지 못했습니다: {string.Join(", ", missing.Prepend(dateCol < 0 ? layout.DateTimeColumn : "").Where(x => x.Length > 0))}");

        var byMonth = new SortedDictionary<string, (Dictionary<string, decimal> Comp, decimal Reimb)>(StringComparer.Ordinal);
        foreach (var row in table.Rows)
        {
            var date = ExportSummaryLoader.ParseDateText(table.Get(row, dateCol));
            if (date is null) continue;
            var ym = date.Value.ToString("yyyy-MM");
            if (!byMonth.TryGetValue(ym, out var acc))
                acc = (incomeCols.ToDictionary(c => c.Name, _ => 0m), 0m);

            var type = table.Get(row, typeCol).Trim();
            if (layout.IncomeTypes.Any(t => Same(t, type)))
            {
                foreach (var (name, col) in incomeCols)
                    acc.Comp[name] += ExportSummaryLoader.ParseAmountText(table.Get(row, col));
            }
            else if (Same(type, layout.ReimbursementType)
                     && table.Get(row, descCol).Contains(layout.ReimbursementContains, StringComparison.OrdinalIgnoreCase))
            {
                // 요약 PDF는 양수 재고 보상만 Income에, 음수 조정은 Expenses에 넣는다.
                var amount = ExportSummaryLoader.ParseAmountText(table.Get(row, reimbCol));
                if (amount > 0) acc.Reimb += amount;
            }
            byMonth[ym] = acc;
        }

        return byMonth.Select(kv => new AmazonMonthlyIncome(table.FileName, marketCode, currency, kv.Key, kv.Value.Comp, kv.Value.Reimb)).ToList();
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

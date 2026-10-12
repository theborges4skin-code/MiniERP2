using MiniERP2.DataLoaders;
using static MiniERP2.DataLoaders.ExportSummaryLoader;

namespace MiniERP2.Services;

/// <summary>
/// 분기 부가세 신고용 "수출신고 대사". 관세청 수출신고 내역(통화·신고일자·결제금액만 있고 주문번호 없음)과
/// 쇼피 구매확정 주문을 짝지어, 판매 매출(소비자 실결제액, 구매확정일 기준)과 신고액의 차이를 항목별로 나눈다.
///
/// 2026-10-12 3분기 실데이터로 확인한 신고액 구조:
///   신고일자 = 쇼피 출고처리일(한국시간), 신고액 = (할인가×수량 − 판매자 부담 쿠폰) × 마켓별 고정비율 k.
///   쇼피 부담 쿠폰·코인은 신고액에서 빠지지 않는다. k는 SG 0.759, TW 0.835처럼 마켓마다 다르고 월별로 거의 일정하다.
///
/// 차이 브리지(통화별, 기간 P):
///   판매액(완료일∈P) − [완료∈P인데 P 안 신고와 짝 없음] + [P 안 신고와 짝인데 완료∉P]
///   + ② 쇼피부담 할인(기준가−실결제) + ③ 신고환산율 차이((k−1)×기준가) + ④ 건별 차이(신고−k×기준가)
///   + ⑤ 짝 없는 신고 = 신고액(신고일∈P)
/// </summary>
// 신고 행은 신고번호가 비면 같은 날 같은 금액이 값으로 같아지므로(record 값 비교) 집합은 모두 참조 비교로 다룬다.
public static class ExportDeclarationReconcileEngine
{
    /// <summary>예상신고액 대비 허용 오차. 넘으면 금액으로는 짝짓지 않고 "같은 날 남은 1:1" 단계로 넘긴다.</summary>
    public const decimal AmountTolerance = 0.15m;

    /// <summary>k를 데이터로 추정하려면 최소 이만큼의 확실한 1:1 표본(그날 신고 1건·주문 1건)이 필요하다.</summary>
    public const int MinRateSamples = 3;

    public record RateInfo(string Currency, decimal Rate, int SampleCount, string Source);

    public record MatchRow(DeclarationRow Declaration, SalesOrderRecord Order, decimal Rate, string MatchType)
    {
        public decimal ExpectedDeclared => Order.SellerBaseAmount * Rate;
        /// <summary>신고액 − 소비자 실결제액.</summary>
        public decimal TotalDiff => Declaration.Amount - Order.PaidAmount;
        /// <summary>② 쇼피 부담 할인(쿠폰·코인 등): 신고 기준가에는 남아 있고 실결제액에서는 빠진 금액.</summary>
        public decimal ShopeeFundedDiscount => Order.SellerBaseAmount - Order.PaidAmount;
        /// <summary>③ 신고환산율 차이: (k − 1) × 기준가.</summary>
        public decimal RateEffect => (Rate - 1m) * Order.SellerBaseAmount;
        /// <summary>④ 건별 차이: 실제 신고액 − 예상 신고액(환율·가격변경 등).</summary>
        public decimal Residual => Declaration.Amount - ExpectedDeclared;
    }

    public record UnmatchedDeclaration(DeclarationRow Declaration, string Reason);
    public record UnmatchedOrder(SalesOrderRecord Order, string Reason);

    public record CurrencySummary(
        string Currency, string Markets,
        decimal SalesCompleted, decimal CompletedButNoDeclaration, decimal DeclaredButCompletedOutside,
        decimal ShopeeFundedDiscount, decimal RateEffect, decimal Residual, decimal UnmatchedDeclarations,
        decimal Declared, int MatchedCount, int UnmatchedDeclarationCount, int UnmatchedOrderCount,
        DateTime? FirstShippedOrder)
    {
        /// <summary>브리지 검산: 판매액에서 출발해 각 항목을 더한 값 − 신고액. 0이어야 한다.</summary>
        public decimal BridgeCheck =>
            SalesCompleted - CompletedButNoDeclaration + DeclaredButCompletedOutside
            + ShopeeFundedDiscount + RateEffect + Residual + UnmatchedDeclarations - Declared;
    }

    public record Result(
        DateTime PeriodStart, DateTime PeriodEndExclusive,
        List<RateInfo> Rates, List<MatchRow> Matches,
        List<UnmatchedDeclaration> UnmatchedDeclarations, List<UnmatchedOrder> UnmatchedOrders,
        List<CurrencySummary> Summaries);

    /// <summary>신고 내역이 걸친 기간을 부가세 신고 단위인 분기 경계로 맞춘다(첫 신고가 속한 분기 첫날 ~ 마지막 신고가 속한 분기 다음 날).
    /// 한 달치 신고가 비어 있어도 분기 전체가 기간이 되므로 빠진 달은 호출 측에서 경고한다.</summary>
    public static (DateTime Start, DateTime EndExclusive) InferPeriod(IReadOnlyList<DeclarationRow> declarations)
    {
        if (declarations.Count == 0) throw new InvalidOperationException("수출신고 내역이 없습니다.");
        var min = declarations.Min(d => d.Date);
        var max = declarations.Max(d => d.Date);
        static DateTime QuarterStart(DateTime d) => new(d.Year, (d.Month - 1) / 3 * 3 + 1, 1);
        return (QuarterStart(min), QuarterStart(max).AddMonths(3));
    }

    public static Result Run(
        IReadOnlyList<DeclarationRow> declarations, IReadOnlyList<SalesOrderRecord> orders,
        DateTime periodStart, DateTime periodEndExclusive,
        IReadOnlyDictionary<string, decimal> configuredRates)
    {
        bool InPeriod(DateTime? d) => d is { } v && v >= periodStart && v < periodEndExclusive;

        var rates = new List<RateInfo>();
        var matches = new List<MatchRow>();
        var unmatchedDecls = new List<UnmatchedDeclaration>();
        var unmatchedOrders = new List<UnmatchedOrder>();
        var summaries = new List<CurrencySummary>();

        var currencies = declarations.Select(d => d.Currency).Concat(orders.Select(o => o.Currency))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.Ordinal).ToList();

        foreach (var currency in currencies)
        {
            var decls = declarations.Where(d => Same(d.Currency, currency)).ToList();
            var ords = orders.Where(o => Same(o.Currency, currency)).ToList();

            if (ords.Count == 0)
            {
                // 쇼피 주문이 없는 통화(USD·MXN 등): 대사 대상 아님. 신고액만 요약에 남긴다.
                foreach (var d in decls)
                    unmatchedDecls.Add(new UnmatchedDeclaration(d, "판매 주문 데이터가 없는 통화(아마존·자사몰 등) — 별도 확인"));
                var declaredOnly = decls.Where(d => InPeriod(d.Date)).Sum(d => d.Amount);
                summaries.Add(new CurrencySummary(currency, "", 0, 0, 0, 0, 0, 0, declaredOnly, declaredOnly,
                    0, decls.Count(d => InPeriod(d.Date)), 0, null));
                continue;
            }

            var rate = EstimateRate(currency, decls, ords, configuredRates);
            rates.Add(rate);

            var pairs = Match(decls, ords, rate.Rate);
            matches.AddRange(pairs);

            var matchedDecls = pairs.Select(p => p.Declaration).ToHashSet(ReferenceEqualityComparer.Instance);
            var matchedOrders = pairs.Select(p => p.Order).ToHashSet(ReferenceEqualityComparer.Instance);
            var firstShipped = ords.Where(o => o.ShippedAt.HasValue).Select(o => o.ShippedAt!.Value.Date).DefaultIfEmpty().Min();
            DateTime? firstShippedOrNull = firstShipped == default ? null : firstShipped;

            foreach (var d in decls.Where(d => !matchedDecls.Contains(d)))
            {
                // 주문파일은 주문일 기준으로 받으므로, 파일 시작 직후 신고는 이전 달 주문(파일 없음)일 가능성이 크다.
                var reason = firstShippedOrNull is { } fs && d.Date <= fs
                    ? "주문파일 기간 이전 주문으로 추정 — 이전 달 주문파일을 넣으면 대사 가능"
                    : firstShippedOrNull is { } fs2 && d.Date <= fs2.AddDays(7)
                        ? "짝 주문 없음 — 주문파일 시작 직후라 이전 달 주문일 가능성 큼(이전 달 주문파일 확인)"
                        : "짝이 되는 주문을 찾지 못함 — 신고일 전후 출고 주문 확인 필요";
                unmatchedDecls.Add(new UnmatchedDeclaration(d, reason));
            }

            foreach (var o in ords.Where(o => !matchedOrders.Contains(o)))
            {
                string reason;
                if (o.ShippedAt is null) reason = "출고일 없음";
                else if (o.ShippedAt.Value.Date >= periodEndExclusive) reason = "기간 이후 출고 — 다음 분기 신고분";
                else if (o.ShippedAt.Value.Date < periodStart) reason = "기간 이전 출고 — 이전 분기 신고분";
                else reason = "짝이 되는 신고를 찾지 못함 — 신고 누락 또는 금액 차이 큼(확인 필요)";
                unmatchedOrders.Add(new UnmatchedOrder(o, reason));
            }

            // 브리지
            var matchedInPeriod = pairs.Where(p => InPeriod(p.Declaration.Date)).ToList();
            var ordersWithDeclInPeriod = matchedInPeriod.Select(p => p.Order).ToHashSet(ReferenceEqualityComparer.Instance);
            var completed = ords.Where(o => InPeriod(o.CompletedAt)).ToList();

            summaries.Add(new CurrencySummary(
                currency,
                string.Join(", ", ords.Select(o => o.MarketCode).Distinct()),
                SalesCompleted: completed.Sum(o => o.PaidAmount),
                CompletedButNoDeclaration: completed.Where(o => !ordersWithDeclInPeriod.Contains(o)).Sum(o => o.PaidAmount),
                DeclaredButCompletedOutside: matchedInPeriod.Where(p => !InPeriod(p.Order.CompletedAt)).Sum(p => p.Order.PaidAmount),
                ShopeeFundedDiscount: matchedInPeriod.Sum(p => p.ShopeeFundedDiscount),
                RateEffect: matchedInPeriod.Sum(p => p.RateEffect),
                Residual: matchedInPeriod.Sum(p => p.Residual),
                UnmatchedDeclarations: decls.Where(d => InPeriod(d.Date) && !matchedDecls.Contains(d)).Sum(d => d.Amount),
                Declared: decls.Where(d => InPeriod(d.Date)).Sum(d => d.Amount),
                MatchedCount: matchedInPeriod.Count,
                UnmatchedDeclarationCount: decls.Count(d => InPeriod(d.Date) && !matchedDecls.Contains(d)),
                UnmatchedOrderCount: ords.Count(o => !matchedOrders.Contains(o)),
                FirstShippedOrder: firstShippedOrNull));
        }

        return new Result(periodStart, periodEndExclusive, rates, matches, unmatchedDecls, unmatchedOrders, summaries);
    }

    /// <summary>그날(통화 기준) 신고 1건·주문 1건뿐인 날은 짝이 확실하므로 그 비율(신고액÷기준가)의 중앙값을 k로 쓴다.
    /// 표본이 부족하면 설정값, 그것도 없으면 1.</summary>
    public static RateInfo EstimateRate(string currency, IReadOnlyList<DeclarationRow> decls, IReadOnlyList<SalesOrderRecord> orders,
        IReadOnlyDictionary<string, decimal> configuredRates)
    {
        var declByDay = decls.GroupBy(d => d.Date.Date).ToDictionary(g => g.Key, g => g.ToList());
        var ratios = orders.Where(o => o.ShippedAt.HasValue)
            .GroupBy(o => o.ShippedAt!.Value.Date)
            .Where(g => g.Count() == 1 && declByDay.TryGetValue(g.Key, out var ds) && ds.Count == 1)
            .Select(g => (Order: g.Single(), Decl: declByDay[g.Key][0]))
            .Where(x => x.Order.SellerBaseAmount > 0 && x.Decl.Amount > 0)
            .Select(x => x.Decl.Amount / x.Order.SellerBaseAmount)
            .OrderBy(r => r)
            .ToList();

        if (ratios.Count >= MinRateSamples)
        {
            var mid = ratios.Count / 2;
            var median = ratios.Count % 2 == 1 ? ratios[mid] : (ratios[mid - 1] + ratios[mid]) / 2m;
            return new RateInfo(currency, Math.Round(median, 4), ratios.Count, "데이터 추정");
        }

        if (configuredRates.TryGetValue(currency, out var configured) && configured > 0)
            return new RateInfo(currency, configured, ratios.Count, "설정값(표본 부족)");

        return new RateInfo(currency, 1m, ratios.Count, "추정 불가(1로 계산)");
    }

    /// <summary>
    /// 신고일 기준 출고일 허용 범위. 신고가 출고처리보다 먼저 잡히는 경우가 있어(예: 금요일 신고, 다음 주 월요일 출고처리)
    /// 출고일이 신고일 1일 전 ~ 4일 후까지 짝 후보로 본다.
    /// </summary>
    public const int ShipDaysBeforeDeclaration = 1;
    public const int ShipDaysAfterDeclaration = 4;

    /// <summary>3단계(느슨한 매칭)의 금액 허용 오차. 이 단계 결과는 "금액차 큼(확인)"으로 표시한다.</summary>
    public const decimal LooseAmountTolerance = 0.35m;

    /// <summary>
    /// 1단계: 출고일이 허용 범위 안이고 신고액이 예상신고액(k×기준가)의 ±15% 이내인 후보를
    ///        (오차 + 날짜차이 가중) 작은 순으로 1:1 확정.
    /// 2단계: 같은 날 남은 신고·주문 수가 같으면 금액 순으로 1:1(가격 변경·환율 변동으로 오차가 큰 건 구제).
    /// 3단계: 남은 것끼리 같은 날짜 범위·오차 35% 이내로 1:1. 확인 권장 표시.
    /// </summary>
    private static List<MatchRow> Match(List<DeclarationRow> decls, List<SalesOrderRecord> orders, decimal rate)
    {
        var usedD = new HashSet<DeclarationRow>(ReferenceEqualityComparer.Instance);
        var usedO = new HashSet<SalesOrderRecord>(ReferenceEqualityComparer.Instance);
        var result = new List<MatchRow>();

        void GreedyPass(decimal tolerance, string matchType)
        {
            var candidates = new List<(DeclarationRow D, SalesOrderRecord O, decimal Score)>();
            foreach (var d in decls.Where(d => !usedD.Contains(d)))
            {
                foreach (var o in orders.Where(o => !usedO.Contains(o)))
                {
                    if (o.ShippedAt is null || o.SellerBaseAmount <= 0) continue;
                    var shipMinusDecl = (o.ShippedAt.Value.Date - d.Date).Days;
                    if (shipMinusDecl < -ShipDaysBeforeDeclaration || shipMinusDecl > ShipDaysAfterDeclaration) continue;
                    var expected = o.SellerBaseAmount * rate;
                    var relErr = Math.Abs(d.Amount - expected) / Math.Max(expected, 0.01m);
                    if (relErr > tolerance) continue;
                    candidates.Add((d, o, relErr + 0.05m * Math.Abs(shipMinusDecl)));
                }
            }

            foreach (var c in candidates.OrderBy(c => c.Score))
            {
                if (usedD.Contains(c.D) || usedO.Contains(c.O)) continue;
                usedD.Add(c.D); usedO.Add(c.O);
                result.Add(new MatchRow(c.D, c.O, rate, matchType));
            }
        }

        GreedyPass(AmountTolerance, "금액 일치");

        foreach (var g in decls.Where(d => !usedD.Contains(d)).GroupBy(d => d.Date.Date).ToList())
        {
            var ds = g.OrderBy(d => d.Amount).ToList();
            var os = orders.Where(o => !usedO.Contains(o) && o.ShippedAt?.Date == g.Key && o.SellerBaseAmount > 0)
                .OrderBy(o => o.SellerBaseAmount).ToList();
            if (ds.Count != os.Count) continue;
            for (var i = 0; i < ds.Count; i++)
            {
                usedD.Add(ds[i]); usedO.Add(os[i]);
                result.Add(new MatchRow(ds[i], os[i], rate, "같은 날 남은 1:1"));
            }
        }

        GreedyPass(LooseAmountTolerance, "금액차 큼(확인)");
        return result;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

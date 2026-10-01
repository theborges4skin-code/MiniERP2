using System.Globalization;
using System.Text.RegularExpressions;
using MiniERP2.Models;

namespace MiniERP2.Services;

/// <summary>
/// 온라인 매출 종합보고서의 숫자를 계산한다(DB/파일 접근 없음 — 입력을 받아 결과만 만든다).
/// 엑셀 출력(OnlineReportExcelWriter)과 화면 점검 메시지가 이 결과 하나를 공유한다.
///
/// 계산 규칙(2026-10-02 사용자 확정):
/// - 채널 블록의 각 품목 열 = 이익분석 DB(ProfitFact)·광고비 DB(AdFact)를 상품그룹 번호로 열에 모은 값.
/// - 택배비 열(CountShipping 블록만): 건수 = 정산 배송비 ÷ 기준단가(3,000), 매출 = 건수 × 기준단가, 이익·광고비 0.
/// - 실택배비 조정(합계 블록 택배비 열 순이익): Σ 실출고건 (기준단가 − 실운임 − 부자재비) + 풀필건수 × (기준단가 − 풀필단가).
///   부자재비는 운임 ≤ 3,200 → 400, 초과 → 1,100. 출고 전체(거래처분 포함)를 한꺼번에 계산하고 채널별로 나누지 않는다.
/// </summary>
public static class OnlineReportBuilder
{
    public const string ShippingColumnHeader = "택배비";

    public class Cell
    {
        public decimal Qty { get; set; }
        public decimal Revenue { get; set; }
        public decimal Profit { get; set; }
        /// <summary>광고비(양수 = 비용). 출력 시 음수로 표시한다.</summary>
        public decimal AdCost { get; set; }
        public decimal Net => Profit - AdCost;
    }

    public class BlockResult
    {
        public required OnlineReportBlock Block { get; init; }
        /// <summary>품목 열 순서와 같은 인덱스.</summary>
        public required Cell[] Cells { get; init; }
        public decimal ShippingCount { get; set; }
        public decimal ShippingRevenue { get; set; }
        /// <summary>택배비 열 순이익(합계 블록에서만 실택배비 조정액, 채널 블록은 0).</summary>
        public decimal ShippingNet { get; set; }
        public List<string> ExtraAdNotes { get; } = [];

        public decimal Qty => Cells.Sum(c => c.Qty);
        public decimal Revenue => Cells.Sum(c => c.Revenue) + ShippingRevenue;
        public decimal Profit => Cells.Sum(c => c.Profit);
        public decimal AdCost => Cells.Sum(c => c.AdCost);
        public decimal Net => Cells.Sum(c => c.Net) + ShippingNet;
        /// <summary>광고비율 = 광고비 ÷ 매출(택배비 매출 포함) — 기존 엑셀 A열 % 와 같은 정의.</summary>
        public decimal? AdRatio => Revenue == 0 ? null : AdCost / Revenue;
    }

    public record FreightLine(decimal Rate, int Count, decimal BaseDiff, decimal Packing, decimal Subtotal);

    public record PartnerResult(string DisplayName, decimal Revenue, decimal Profit, bool IsManual, IReadOnlyList<string> MatchedParties);

    public record ExportResult(string Market, decimal RevenueUsd, decimal ProfitUsd, decimal RevenueKrw, decimal ProfitKrw);

    public record ChangeRow(string Key, string Group, decimal Previous, decimal Current)
    {
        public decimal Delta => Current - Previous;
        public decimal? Rate => Previous == 0 ? null : Delta / Previous;
    }

    public class Result
    {
        public required string Period { get; init; }
        public required IReadOnlyList<OnlineReportGroupColumn> Columns { get; init; }
        public required IReadOnlyList<BlockResult> Blocks { get; init; }
        public required BlockResult Total { get; init; }

        public required IReadOnlyList<FreightLine> FreightLines { get; init; }
        public int FulfillmentCount { get; init; }
        public decimal FulfillmentSubtotal { get; init; }
        public decimal FreightCalculated { get; init; }
        public decimal FreightAdjustment { get; init; }
        public bool FreightOverridden { get; init; }

        public required IReadOnlyList<PartnerResult> Partners { get; init; }
        public required IReadOnlyList<ExportResult> Exports { get; init; }
        public decimal ExchangeRate { get; init; }
        public string ExchangeRateNote { get; init; } = string.Empty;
        public string Memo { get; init; } = string.Empty;
        public required IReadOnlyList<(string Item, decimal Amount)> Costs { get; init; }

        public required IReadOnlyList<ChangeRow> GroupTop { get; init; }
        public required IReadOnlyList<ChangeRow> GroupBottom { get; init; }
        public required IReadOnlyList<ChangeRow> CskuTop { get; init; }
        public required IReadOnlyList<ChangeRow> CskuBottom { get; init; }
        public bool HasPreviousProfit { get; init; }
        public bool HasPreviousCsku { get; init; }
        public bool HasCurrentCsku { get; init; }

        /// <summary>화면에 보여줄 점검 메시지. 데이터 소스가 DB 쪽 점검 결과를 덧붙일 수 있게 목록으로 둔다.</summary>
        public required List<string> Warnings { get; init; }

        // ── 요약(최종 결과) ──
        public decimal OnlineRevenue => Total.Revenue - Blocks.Where(b => b.Block.IsRocketGrowth).Sum(b => b.Revenue);
        public decimal OnlineNet => Total.Net - Blocks.Where(b => b.Block.IsRocketGrowth).Sum(b => b.Net);
        public decimal RocketRevenue => Blocks.Where(b => b.Block.IsRocketGrowth).Sum(b => b.Revenue);
        public decimal RocketNet => Blocks.Where(b => b.Block.IsRocketGrowth).Sum(b => b.Net);
        public decimal PartnerRevenue => Partners.Sum(p => p.Revenue);
        public decimal PartnerProfit => Partners.Sum(p => p.Profit);
        public decimal ExportRevenue => Exports.Sum(e => e.RevenueKrw);
        public decimal ExportProfit => Exports.Sum(e => e.ProfitKrw);
        public decimal SubtotalRevenue => OnlineRevenue + RocketRevenue + PartnerRevenue + ExportRevenue;
        public decimal SubtotalProfit => OnlineNet + RocketNet + PartnerProfit + ExportProfit;
        public decimal CostTotal => Costs.Sum(c => c.Amount);
        public decimal FinalProfit => SubtotalProfit - CostTotal;
    }

    public static Result Build(
        string period,
        OnlineReportConfig config,
        OnlineReportMonthInput month,
        IReadOnlyList<ProfitFactRow> profitFacts,
        IReadOnlyList<AdFactRow> adFacts,
        IReadOnlyList<ProfitFactRow> previousProfitFacts,
        IReadOnlyList<OnlineReportPartnerClosing> partnerClosings,
        IReadOnlyList<OnlineReportCskuRow> currentCsku,
        IReadOnlyList<OnlineReportCskuRow> previousCsku)
    {
        var warnings = new List<string>();
        var columns = config.GroupColumns;
        var fallbackIndex = columns.ToList().FindIndex(c => c.Number == config.FallbackColumnNumber);
        if (fallbackIndex < 0)
        {
            warnings.Add($"기타 열(번호 {config.FallbackColumnNumber})이 품목 열 설정에 없습니다 — 분류되지 않는 그룹은 마지막 열로 들어갑니다.");
            fallbackIndex = columns.Count - 1;
        }

        var fallbackGroups = new SortedSet<string>(StringComparer.Ordinal);
        int ColumnOf(string productGroup)
        {
            var index = ResolveColumnIndex(columns, productGroup);
            if (index >= 0) return index;
            if (!string.IsNullOrWhiteSpace(productGroup)) fallbackGroups.Add(productGroup);
            return fallbackIndex;
        }

        var baseFee = config.Freight.BaseFee <= 0 ? 3000m : config.Freight.BaseFee;
        var blockResults = new List<BlockResult>();
        var assignedChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in config.Blocks)
        {
            var codes = new HashSet<string>(block.ChannelCodes, StringComparer.OrdinalIgnoreCase);
            assignedChannels.UnionWith(codes);
            var result = new BlockResult { Block = block, Cells = NewCells(columns.Count) };

            var blockProfit = profitFacts.Where(f => codes.Contains(f.ChannelCode)).ToList();
            foreach (var fact in blockProfit)
            {
                var cell = result.Cells[ColumnOf(fact.ProductGroup)];
                cell.Qty += fact.Qty;
                cell.Revenue += fact.Revenue;
                cell.Profit += fact.GrossProfit;
            }
            foreach (var ad in adFacts.Where(f => codes.Contains(f.ChannelCode)))
                result.Cells[ColumnOf(ad.ProductGroup)].AdCost += ad.AdCost;

            if (block.CountShipping)
            {
                var shippingFee = blockProfit.Sum(f => f.ShippingFee);
                result.ShippingCount = shippingFee / baseFee;
                result.ShippingRevenue = shippingFee;
                if (blockProfit.Count > 0 && shippingFee == 0)
                    warnings.Add($"{block.Name}: 배송비 정보가 없습니다 — 마감/이익분석에서 [보고서 저장]을 다시 하거나 결과파일을 다시 불러오면 택배 건수가 채워집니다.");
            }

            foreach (var extra in month.ExtraAdCosts.Where(x => string.Equals(x.BlockName, block.Name, StringComparison.Ordinal)))
            {
                var index = extra.ColumnNumber == 0 ? fallbackIndex : columns.ToList().FindIndex(c => c.Number == extra.ColumnNumber);
                if (index < 0) index = fallbackIndex;
                result.Cells[index].AdCost += extra.Amount;
                result.ExtraAdNotes.Add(string.IsNullOrWhiteSpace(extra.Memo)
                    ? extra.Amount.ToString("N0", CultureInfo.InvariantCulture)
                    : $"{extra.Memo} {extra.Amount.ToString("N0", CultureInfo.InvariantCulture)}");
            }

            if (codes.Count > 0 && blockProfit.Count == 0)
                warnings.Add($"{block.Name}: 이 달 이익분석 데이터가 없습니다 (채널 {string.Join(", ", codes)}).");
            if (codes.Count == 0)
                warnings.Add($"{block.Name}: 연결된 채널이 없습니다 — 설정에서 채널을 지정하세요.");

            blockResults.Add(result);
        }

        // ── 실택배비 조정 ──
        var freight = config.Freight;
        var freightLines = month.FreightTiers
            .Where(t => t.Count != 0)
            .OrderBy(t => t.Rate)
            .Select(t =>
            {
                var packing = (t.Rate <= freight.PackingThreshold ? freight.PackingSmall : freight.PackingLarge) * t.Count;
                var baseDiff = (baseFee - t.Rate) * t.Count;
                return new FreightLine(t.Rate, t.Count, baseDiff, -packing, baseDiff - packing);
            })
            .ToList();
        var fulfillmentSubtotal = (baseFee - freight.FulfillmentUnitCost) * month.FulfillmentCount;
        var freightCalculated = freightLines.Sum(l => l.Subtotal) + fulfillmentSubtotal;
        var freightAdjustment = month.FreightAdjustmentOverride ?? freightCalculated;
        if (freightLines.Count == 0 && month.FreightAdjustmentOverride is null)
            warnings.Add("운임표가 비어 있습니다 — [운임 파일 불러오기]로 이 달 운송장이력 파일을 넣어야 실택배비 조정이 계산됩니다.");

        var total = new BlockResult { Block = new OnlineReportBlock { Name = "합계" }, Cells = NewCells(columns.Count) };
        foreach (var block in blockResults)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                total.Cells[i].Qty += block.Cells[i].Qty;
                total.Cells[i].Revenue += block.Cells[i].Revenue;
                total.Cells[i].Profit += block.Cells[i].Profit;
                total.Cells[i].AdCost += block.Cells[i].AdCost;
            }
            total.ShippingCount += block.ShippingCount;
            total.ShippingRevenue += block.ShippingRevenue;
        }
        total.ShippingNet = freightAdjustment;

        // ── 경고: 보고서에 안 들어간 채널 ──
        var ignoredCodes = new HashSet<string>(config.IgnoredChannelCodes, StringComparer.OrdinalIgnoreCase);
        var unassigned = profitFacts
            .Where(f => !assignedChannels.Contains(f.ChannelCode) && !ignoredCodes.Contains(f.ChannelCode)
                        && !config.IgnoredChannelNamePrefixes.Any(p => !string.IsNullOrEmpty(p) && f.ChannelName.StartsWith(p, StringComparison.Ordinal)))
            .Select(f => string.IsNullOrWhiteSpace(f.ChannelName) ? f.ChannelCode : $"{f.ChannelName}({f.ChannelCode})")
            .Distinct()
            .ToList();
        if (unassigned.Count > 0)
            warnings.Add($"어느 블록에도 없는 채널이 있어 보고서에서 빠졌습니다: {string.Join(", ", unassigned)}");
        if (fallbackGroups.Count > 0)
            warnings.Add($"{columns[fallbackIndex].Header} 열로 모은 상품그룹: {string.Join(", ", fallbackGroups)}");

        // ── 거래처 ──
        var partners = new List<PartnerResult>();
        foreach (var partner in config.Partners)
        {
            var manual = month.PartnerOverrides.FirstOrDefault(o => string.Equals(o.DisplayName, partner.DisplayName, StringComparison.Ordinal));
            var matched = partnerClosings
                .Where(c => partner.PartyNameKeywords.Any(k => !string.IsNullOrWhiteSpace(k) && c.PartyName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            partners.Add(manual is not null
                ? new PartnerResult(partner.DisplayName, manual.Revenue, manual.Profit, true, matched.Select(m => m.PartyName).ToList())
                : new PartnerResult(partner.DisplayName, matched.Sum(m => m.Revenue), matched.Sum(m => m.Profit), false, matched.Select(m => m.PartyName).ToList()));
        }

        // ── 수출 ──
        var exports = config.ExportMarkets
            .Select(market =>
            {
                var entry = month.Exports.FirstOrDefault(e => string.Equals(e.Market, market, StringComparison.Ordinal));
                var revenueUsd = entry?.RevenueUsd ?? 0m;
                var profitUsd = entry?.ProfitUsd ?? 0m;
                return new ExportResult(market, revenueUsd, profitUsd, Math.Round(revenueUsd * month.ExchangeRate), Math.Round(profitUsd * month.ExchangeRate));
            })
            .ToList();
        if (month.ExchangeRate == 0 && exports.Any(e => e.RevenueUsd != 0 || e.ProfitUsd != 0))
            warnings.Add("수출 금액이 있는데 환율이 0입니다.");

        var costs = config.CostItems
            .Select(item => (item, month.Costs.TryGetValue(item, out var amount) ? amount : 0m))
            .ToList();

        // ── 상품그룹 매출 변동(온라인 전체 블록, 택배비 제외) ──
        var currentByColumn = total.Cells.Select(c => c.Revenue).ToArray();
        var previousByColumn = new decimal[columns.Count];
        foreach (var fact in previousProfitFacts.Where(f => assignedChannels.Contains(f.ChannelCode)))
            previousByColumn[ResolveColumnIndexOrFallback(columns, fact.ProductGroup, fallbackIndex)] += fact.Revenue;
        var groupChanges = columns
            .Select((c, i) => new ChangeRow(FormatColumnLabel(c), FormatColumnLabel(c), previousByColumn[i], currentByColumn[i]))
            .Where(r => r.Previous != 0 || r.Current != 0)
            .ToList();
        var hasPreviousProfit = previousProfitFacts.Any(f => assignedChannels.Contains(f.ChannelCode));

        // ── CSKU 매출 변동 ──
        var cskuChanges = BuildCskuChanges(currentCsku, previousCsku, assignedChannels);

        return new Result
        {
            Period = period,
            Columns = columns,
            Blocks = blockResults,
            Total = total,
            FreightLines = freightLines,
            FulfillmentCount = month.FulfillmentCount,
            FulfillmentSubtotal = fulfillmentSubtotal,
            FreightCalculated = freightCalculated,
            FreightAdjustment = freightAdjustment,
            FreightOverridden = month.FreightAdjustmentOverride is not null,
            Partners = partners,
            Exports = exports,
            ExchangeRate = month.ExchangeRate,
            ExchangeRateNote = month.ExchangeRateNote,
            Memo = month.Memo ?? string.Empty,
            Costs = costs,
            GroupTop = hasPreviousProfit ? groupChanges.Where(r => r.Delta > 0).OrderByDescending(r => r.Delta).Take(3).ToList() : [],
            GroupBottom = hasPreviousProfit ? groupChanges.Where(r => r.Delta < 0).OrderBy(r => r.Delta).Take(3).ToList() : [],
            CskuTop = cskuChanges.Top,
            CskuBottom = cskuChanges.Bottom,
            HasPreviousProfit = hasPreviousProfit,
            HasPreviousCsku = cskuChanges.HasPrevious,
            HasCurrentCsku = cskuChanges.HasCurrent,
            Warnings = warnings,
        };
    }

    private static (List<ChangeRow> Top, List<ChangeRow> Bottom, bool HasPrevious, bool HasCurrent) BuildCskuChanges(
        IReadOnlyList<OnlineReportCskuRow> current, IReadOnlyList<OnlineReportCskuRow> previous, HashSet<string> channels)
    {
        static Dictionary<string, (string Group, decimal Revenue)> Sum(IEnumerable<OnlineReportCskuRow> rows) =>
            rows.Where(r => !string.IsNullOrWhiteSpace(r.CskuCode))
                .GroupBy(r => r.CskuCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (g.Select(r => r.ProductGroup).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "", g.Sum(r => r.Revenue)), StringComparer.OrdinalIgnoreCase);

        var cur = Sum(current.Where(r => channels.Contains(r.ChannelCode)));
        var prev = Sum(previous.Where(r => channels.Contains(r.ChannelCode)));

        if (cur.Count == 0) return ([], [], prev.Count > 0, false);

        if (prev.Count == 0)
        {
            // 전월 통계가 없으면 증감을 낼 수 없으므로 당월 매출 상위 5개만 보여준다(Previous=0, 표시 시 "–").
            var topOnly = cur.OrderByDescending(kv => kv.Value.Revenue).Take(5)
                .Select(kv => new ChangeRow(kv.Key, kv.Value.Group, 0m, kv.Value.Revenue)).ToList();
            return (topOnly, [], false, true);
        }

        var all = cur.Keys.Union(prev.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                cur.TryGetValue(key, out var c);
                prev.TryGetValue(key, out var p);
                var group = !string.IsNullOrWhiteSpace(c.Group) ? c.Group : p.Group ?? "";
                return new ChangeRow(key, group, p.Revenue, c.Revenue);
            })
            .ToList();
        return (all.Where(r => r.Delta > 0).OrderByDescending(r => r.Delta).Take(5).ToList(),
                all.Where(r => r.Delta < 0).OrderBy(r => r.Delta).Take(5).ToList(), true, true);
    }

    private static Cell[] NewCells(int count) => Enumerable.Range(0, count).Select(_ => new Cell()).ToArray();

    private static readonly Regex LeadingNumber = new(@"^\s*(\d+)", RegexOptions.Compiled);

    /// <summary>상품그룹 문자열의 앞 번호("14.면도" → 14, "14. 면도" → 14). 번호가 없으면 null.</summary>
    public static int? ParseGroupNumber(string? productGroup)
    {
        if (string.IsNullOrWhiteSpace(productGroup)) return null;
        var match = LeadingNumber.Match(productGroup);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : null;
    }

    /// <summary>상품그룹이 들어갈 품목 열 인덱스. 어느 열에도 없으면 -1.</summary>
    public static int ResolveColumnIndex(IReadOnlyList<OnlineReportGroupColumn> columns, string? productGroup)
    {
        var number = ParseGroupNumber(productGroup);
        if (number is null) return -1;
        for (int i = 0; i < columns.Count; i++)
            if (columns[i].EffectiveMembers.Contains(number.Value)) return i;
        return -1;
    }

    private static int ResolveColumnIndexOrFallback(IReadOnlyList<OnlineReportGroupColumn> columns, string? productGroup, int fallbackIndex)
    {
        var index = ResolveColumnIndex(columns, productGroup);
        return index >= 0 ? index : fallbackIndex;
    }

    public static string FormatColumnLabel(OnlineReportGroupColumn column) => $"{column.Number:00}.{column.Header}";

    /// <summary>"2026-09" → "2026.09.01~2026.09.30. 정산완료"(기존 엑셀 A2 문구).</summary>
    public static string FormatPeriodLabel(string period)
    {
        if (!DateTime.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
            return period;
        var last = first.AddMonths(1).AddDays(-1);
        return $"{first:yyyy.MM.dd}~{last:yyyy.MM.dd}. 정산완료";
    }

    /// <summary>"2026-09" → "2026-08".</summary>
    public static string PreviousPeriod(string period) =>
        DateTime.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first)
            ? first.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : period;

    /// <summary>"2026-09" → "2609"(CSKU별 통계의 기간 표기).</summary>
    public static string ToCskuStatPeriod(string period) =>
        period.Length == 7 && period[4] == '-' ? period.Substring(2, 2) + period.Substring(5, 2) : period;
}

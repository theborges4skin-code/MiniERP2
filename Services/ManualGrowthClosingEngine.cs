using System.Text.RegularExpressions;
using MiniERP2.Migration;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Services;

/// <summary>
/// 이공그로스수동마감(ManualGrowthClosing_Spec.md)의 순수 로직: 대상 시트 판정, 파싱 어댑터(구분행 제외·
/// 마감월 결정), CSKU 해석·원가·이익 계산, 요약/리포트 집계. DB·UI 의존 없이 조회 함수만 주입받는다.
/// </summary>
public static class ManualGrowthClosingEngine
{
    private static readonly Regex SheetPeriodRegex = new(@"^\s*(\d{2})(\d{2})(?!\d)", RegexOptions.Compiled);

    /// <summary>
    /// 시트명에 '제트' 또는 '그로스'가 들어있고 앞 4자리가 YYMM이면 대상 시트다(스펙 D4). period는 'yyyy-MM'.
    /// </summary>
    public static bool TryGetTargetPeriod(string sheetName, out string period)
    {
        period = "";
        if (!(sheetName.Contains("제트") || sheetName.Contains("그로스"))) return false;
        var m = SheetPeriodRegex.Match(sheetName);
        if (!m.Success) return false;
        int month = int.Parse(m.Groups[2].Value);
        if (month is < 1 or > 12) return false;
        period = $"20{m.Groups[1].Value}-{month:00}";
        return true;
    }

    /// <summary>대상 시트만 (시트명, 마감월)로 골라 최신 마감월 순으로 돌려준다.</summary>
    public static List<(string SheetName, string Period)> FindTargetSheets(IEnumerable<string> sheetNames) =>
        sheetNames
            .Select(n => TryGetTargetPeriod(n, out var p) ? (n, p) : (n, (string?)null))
            .Where(x => x.Item2 != null)
            .Select(x => (x.n, x.Item2!))
            .OrderByDescending(x => x.Item2, StringComparer.Ordinal)
            .ThenBy(x => x.n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 상품명 → CSKU코드. 앞뒤 공백 제거 + 연속 공백 1칸만 한다(스펙 Q4) — 그 외 글자는 한 글자라도
    /// 다르면 다른 CSKU로 본다(세트 구성도 마찬가지, 매핑 단계에서 사용자가 판단).
    /// </summary>
    public static string NormalizeItemName(string? itemName) =>
        string.IsNullOrWhiteSpace(itemName) ? "" : Regex.Replace(itemName.Trim(), @"\s+", " ");

    public static ManualGrowthSheetResult ParseSheet(ExcelWorksheet sheet, string sourceFileName, string period)
        => Adapt(TradeStatementSheetParser.Parse(sheet, sourceFileName), period);

    /// <summary>
    /// 파서 결과를 마감 라인으로 바꾼다. 구분행(<c>&lt;로켓그로스(제트)&gt;</c>처럼 품목명만 있고 수량·금액이
    /// 모두 0인 행)은 제외하고 건수만 센다. 라인 날짜의 월이 마감월과 다르면 경고 플래그만 단다.
    /// </summary>
    public static ManualGrowthSheetResult Adapt(ParsedStatementSheet parsed, string period)
    {
        var result = new ManualGrowthSheetResult
        {
            SourceFileName = parsed.SourceFileName,
            SheetName = parsed.SourceSheetName,
            Period = period,
            TotalsReconciled = parsed.TotalsReconciled,
        };
        result.Flags.AddRange(parsed.Flags);

        int periodYear = int.Parse(period[..4]);
        int periodMonth = int.Parse(period[5..7]);
        int rowNo = 0;

        foreach (var src in parsed.Lines)
        {
            if (src.Qty == 0 && src.SupplyAmount == 0 && src.Total == 0)
            {
                result.ExcludedDividerCount++;
                continue;
            }

            var line = new ManualGrowthLine
            {
                Period = period,
                SourceSheetName = parsed.SourceSheetName,
                RowNo = ++rowNo,
                ItemName = src.ItemName,
                CskuCode = NormalizeItemName(src.ItemName),
                Qty = src.Qty,
                UnitPrice = src.UnitPrice,
                UnitPriceVatIncluded = src.UnitPriceVatIncluded,
                SupplyAmount = src.SupplyAmount,
                Tax = src.Tax,
            };

            if (src.Month > 0 && src.Day > 0)
            {
                int year = src.Year <= 0 ? periodYear : src.Year < 100 ? 2000 + src.Year : src.Year;
                try { line.LineDate = new DateTime(year, src.Month, src.Day).ToString("yyyy-MM-dd"); }
                catch (ArgumentOutOfRangeException) { /* 잘못된 날짜 — 공란으로 둔다 */ }
            }
            if (src.Month > 0 && src.Month != periodMonth) line.DateMismatch = true;

            result.Lines.Add(line);
        }

        return result;
    }

    /// <summary>
    /// CSKU(=정규화 상품명)로 MSKU·원가를 해석하고 이익·단가불일치를 계산한다. 미배정 라인은 원가 0,
    /// 이익 = 매출 그대로 두고 상태로만 구분한다(확정은 폼에서 차단).
    /// 원가 우선순위는 <see cref="CostResolver"/>(매입가 없음 → CSKU 개별원가 &gt; 마스터 대표원가).
    /// </summary>
    public static void Resolve(IEnumerable<ManualGrowthLine> lines,
        Func<string, ChannelSkuModel?> cskuLookup, Func<string, ItemModel?> itemLookup)
    {
        foreach (var line in lines)
        {
            var csku = string.IsNullOrEmpty(line.CskuCode) ? null : cskuLookup(line.CskuCode);
            if (csku == null || string.IsNullOrWhiteSpace(csku.Msku))
            {
                line.MasterSku = "";
                line.ProductGroup = "";
                line.CostPrice = 0;
                line.PriceMismatch = false;
                line.Profit = line.Revenue;
                continue;
            }

            var item = itemLookup(csku.Msku);
            line.MasterSku = csku.Msku;
            line.ProductGroup = item?.ProductGroup ?? "";
            line.CostPrice = CostResolver.Resolve(null, csku.CostPriceOverride, item?.CostPrice ?? 0m);
            line.Profit = line.Revenue - line.Cost;
            line.PriceMismatch = csku.SupplyPrice != 0 && Math.Abs(ToVatIncludedUnitPrice(line) - csku.SupplyPrice) >= 1m;
        }
    }

    /// <summary>
    /// 명세표 단가를 CSKU 납품가와 같은 VAT포함 기준으로 바꾼다(ChannelSkuTable.SupplyPrice는 VAT포함 저장).
    /// 분리형 양식(단가=VAT별도)은 공급가액/세액 비율이 아니라 10%를 곱해 원 단위 반올림한다.
    /// </summary>
    public static decimal ToVatIncludedUnitPrice(ManualGrowthLine line) =>
        line.UnitPriceVatIncluded ? line.UnitPrice : VatRound(line.UnitPrice * 1.1m);

    private static decimal VatRound(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);

    public static List<ManualGrowthCskuSummary> BuildCskuSummary(IEnumerable<ManualGrowthLine> lines) =>
        lines
            .GroupBy(l => (l.Period, l.CskuCode))
            .Select(g => new ManualGrowthCskuSummary
            {
                Period = g.Key.Period,
                CskuCode = g.Key.CskuCode,
                MasterSku = g.First().MasterSku,
                ProductGroup = g.First().ProductGroup,
                Qty = g.Sum(l => l.Qty),
                SupplyAmount = g.Sum(l => l.SupplyAmount),
                Revenue = g.Sum(l => l.Revenue),
                Cost = g.Sum(l => l.Cost),
                Profit = g.Sum(l => l.Profit),
            })
            .OrderBy(s => s.Period, StringComparer.Ordinal)
            .ThenByDescending(s => s.Revenue)
            .ToList();

    /// <summary>
    /// ProfitFactTable 저장용 품목그룹 집계. 그룹 라벨은 마감/이익분석(SettlementForm.ResolveProductGroupLabel)과
    /// 같은 규칙 — MSKU 없으면 "(미매핑)", 그룹 미지정이면 "(미지정)". Revenue는 VAT포함 매출.
    /// </summary>
    public static List<ProfitFactRow> BuildProfitFacts(IEnumerable<ManualGrowthLine> lines) =>
        lines
            .GroupBy(l => l.IsUnassigned ? "(미매핑)" : string.IsNullOrWhiteSpace(l.ProductGroup) ? "(미지정)" : l.ProductGroup)
            .Select(g => new ProfitFactRow
            {
                ProductGroup = g.Key,
                Qty = (int)g.Sum(l => l.Qty),
                Revenue = g.Sum(l => l.Revenue),
                GrossProfit = g.Sum(l => l.Profit),
            })
            .OrderBy(r => r.ProductGroup, StringComparer.Ordinal)
            .ToList();

    /// <summary>마감월 단위로 라인을 묶어 저장용 헤더를 만든다(같은 달 시트가 여러 장이면 합친다).</summary>
    public static List<ManualGrowthClosing> BuildClosings(string channelCode, IEnumerable<ManualGrowthSheetResult> sheets) =>
        sheets
            .GroupBy(s => s.Period)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var closing = new ManualGrowthClosing
                {
                    Period = g.Key,
                    ChannelCode = channelCode,
                    SourceFileName = string.Join(", ", g.Select(s => s.SourceFileName).Distinct()),
                    SourceSheetName = string.Join(", ", g.Select(s => s.SheetName)),
                    StatusFlags = string.Join(",", g.SelectMany(s => s.Flags).Distinct()),
                };
                int rowNo = 0;
                foreach (var line in g.SelectMany(s => s.Lines))
                {
                    line.RowNo = ++rowNo;
                    closing.Lines.Add(line);
                }
                RecalculateTotals(closing);
                return closing;
            })
            .ToList();

    public static void RecalculateTotals(ManualGrowthClosing closing)
    {
        closing.TotalQty = closing.Lines.Sum(l => l.Qty);
        closing.TotalSupply = closing.Lines.Sum(l => l.SupplyAmount);
        closing.TotalTax = closing.Lines.Sum(l => l.Tax);
        closing.TotalCost = closing.Lines.Sum(l => l.Cost);
        closing.TotalProfit = closing.Lines.Sum(l => l.Profit);
    }
}

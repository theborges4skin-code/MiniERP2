using System.Globalization;
using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// 거래처 마감보드(거래처마감보드_개발기획서.md §9)의 발행 문서 빌더. 기존 `DocsForm`의 그리드 주입
/// 방식(`OutboundHistoryPickerDialog` 패턴)은 단일 거래처·다이얼로그 상호작용 전제라 다중 거래처
/// 일괄 발행에는 맞지 않아, 여기서는 `DocumentExporter`(§2 "출력 엔진 그대로 사용")를 직접 헤드리스로
/// 호출한다 — 단건이든 배치든 같은 경로로 처리된다.
/// </summary>
public static class PartnerClosingDocumentBuilder
{
    public static TradeStatementDoc BuildTradeStatement(PartnerClosingSummary summary, DocType docType, DocParty supplier, DocParty buyer)
    {
        var vatExcluded = docType == DocType.TradeStatementVatExcl;
        // 명세표는 항상 "같은 날짜·같은 CSKU"만 합산한다(§3 요청) — 날짜가 다르면 같은 CSKU라도 별도 줄.
        var merged = MergeByCskuAndDate(summary.Lines, summary.Period, ignoreDate: false);

        return new TradeStatementDoc
        {
            DocType = docType,
            Supplier = supplier,
            Buyer = buyer,
            IssueDate = DateTime.Today,
            StampImagePath = supplier.StampImagePath,
            Lines = merged.Select(m => new DocLineItem
            {
                Year = m.Year,
                Month = m.Month,
                Day = m.Day,
                ItemName = m.ItemName,
                Spec = m.Spec,
                Qty = m.Qty,
                UnitPrice = VatCalculator.ToDisplay(m.UnitPrice, vatExcluded),
            }).ToList(),
        };
    }

    /// <summary>
    /// 매출장(내부 손익 검토용)의 단가/원가 기준을 <paramref name="vatExcluded"/>로 고른다 — CSKU
    /// 납품단가는 VAT포함 기준이므로(VatCalculator 주석), true면 10/11로 나눠 공급가 기준으로 보여주고
    /// (부가세는 실제 수익이 아니므로 기본값), false면 CSKU 값 그대로(VAT포함) 보여준다.
    /// <paramref name="ignoreDate"/>가 true면 날짜와 무관하게 CSKU 하나로 전체 기간을 합산하고
    /// (§3 옵션), false(기본)면 명세표와 같은 "날짜·CSKU" 단위로 합산한다.
    /// </summary>
    public static SalesLedgerDoc BuildSalesLedger(PartnerClosingSummary summary, DocParty supplier, DocParty buyer, bool ignoreDate = false, bool vatExcluded = true)
    {
        var merged = MergeByCskuAndDate(summary.Lines, summary.Period, ignoreDate);

        return new SalesLedgerDoc
        {
            Supplier = supplier,
            Buyer = buyer,
            IssueDate = DateTime.Today,
            StampImagePath = supplier.StampImagePath,
            IsVatExcluded = vatExcluded,
            Lines = merged.Select(m => new SalesLedgerLineItem
            {
                Year = m.Year,
                Month = m.Month,
                Day = m.Day,
                ItemName = m.ItemName,
                Spec = m.Spec,
                Qty = m.Qty,
                UnitPrice = VatCalculator.ToDisplay(m.UnitPrice, vatExcluded),
                CostPrice = VatCalculator.ToDisplay(m.CostPrice, vatExcluded),
            }).ToList(),
        };
    }

    public static string DefaultFileName(PartnerClosingSummary summary, string docLabel)
    {
        var safeName = summary.PartyName;
        foreach (var c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');
        return $"{safeName}_{summary.Period}_{docLabel}.xlsx";
    }

    private sealed record MergedLine(int Year, int Month, int Day, string CskuCode, string ItemName, string Spec, decimal Qty, decimal UnitPrice, decimal CostPrice);

    /// <summary>
    /// 같은 (귀속일, CSKU) 라인을 하나로 합산한다(§3). ignoreDate=true면 CSKU만으로 합산하고
    /// 날짜는 귀속월(Period) 1일로 표기하되 "일" 칸은 비워(Day=0) 특정일이 아님을 나타낸다
    /// (DocumentExporter.WriteDataRow가 Day&lt;=0이면 그 칸을 그냥 안 쓴다). 단가/원가는 라인별로
    /// 다를 수 있어(가격 변경 등) 단순 평균이 아니라 "합계 ÷ 합계수량"으로 역산해 총액이 정확히
    /// 보존되게 한다.
    /// </summary>
    private static List<MergedLine> MergeByCskuAndDate(List<PartnerClosingLine> lines, string period, bool ignoreDate)
    {
        var groups = ignoreDate
            ? lines.GroupBy(l => (Date: (DateTime?)null, l.CskuCode))
            : lines.GroupBy(l => (Date: l.LineDate?.Date, l.CskuCode));

        var periodFallback = DateTime.TryParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : DateTime.Today;

        return groups.Select(g =>
            {
                var qty = g.Sum(l => l.Qty);
                var supply = g.Sum(l => l.Qty * l.UnitPrice);
                var cost = g.Sum(l => l.Qty * l.CostPrice);
                var first = g.First();
                var date = g.Key.Date;

                return new MergedLine(
                    date?.Year ?? periodFallback.Year,
                    date?.Month ?? periodFallback.Month,
                    date?.Day ?? 0, // 0이면 "일" 칸을 비워 특정일이 아님을 나타냄
                    g.Key.CskuCode,
                    first.ItemName,
                    first.Spec,
                    qty,
                    qty == 0 ? 0 : supply / qty,
                    qty == 0 ? 0 : cost / qty);
            })
            .OrderBy(m => m.Year).ThenBy(m => m.Month).ThenBy(m => m.Day)
            .ThenBy(m => m.CskuCode, StringComparer.Ordinal)
            .ToList();
    }
}

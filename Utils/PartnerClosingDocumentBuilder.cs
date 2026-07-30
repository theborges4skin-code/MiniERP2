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
    public static TradeStatementDoc BuildTradeStatement(PartnerClosingSummary summary, DocType docType, DocParty supplier, DocParty buyer) => new()
    {
        DocType = docType,
        Supplier = supplier,
        Buyer = buyer,
        IssueDate = DateTime.Today,
        StampImagePath = supplier.StampImagePath,
        Lines = summary.Lines.Select(l => new DocLineItem
        {
            Year = (l.LineDate ?? DateTime.Today).Year,
            Month = (l.LineDate ?? DateTime.Today).Month,
            Day = (l.LineDate ?? DateTime.Today).Day,
            ItemName = l.ItemName,
            Spec = l.Spec,
            Qty = l.Qty,
            UnitPrice = l.UnitPrice,
        }).ToList(),
    };

    public static SalesLedgerDoc BuildSalesLedger(PartnerClosingSummary summary, DocParty supplier, DocParty buyer) => new()
    {
        Supplier = supplier,
        Buyer = buyer,
        IssueDate = DateTime.Today,
        StampImagePath = supplier.StampImagePath,
        Lines = summary.Lines.Select(l => new SalesLedgerLineItem
        {
            Year = (l.LineDate ?? DateTime.Today).Year,
            Month = (l.LineDate ?? DateTime.Today).Month,
            Day = (l.LineDate ?? DateTime.Today).Day,
            ItemName = l.ItemName,
            Spec = l.Spec,
            Qty = l.Qty,
            UnitPrice = l.UnitPrice,
            CostPrice = l.CostPrice,
        }).ToList(),
    };

    public static string DefaultFileName(PartnerClosingSummary summary, string docLabel)
    {
        var safeName = summary.PartyName;
        foreach (var c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');
        return $"{safeName}_{summary.Period}_{docLabel}.xlsx";
    }
}

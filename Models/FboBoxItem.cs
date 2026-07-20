namespace MiniERP2.Models;

/// <summary>
/// FBO 박스 안의 품목 1줄. 이 grain이 곧 하배출고이서/재고입고이서의 한 행이다(기획서 §2.2).
/// FboItemCode/ItemName/QtyPerBox는 저장 시점의 FboCskuMaster 값을 스냅샷하고(마스터가 나중에
/// 바뀌어도 과거 발주 이력은 불변), Qty는 실제 박스에 담긴 수량(부분출고 시 QtyPerBox와 달라짐)이다.
/// </summary>
public class FboBoxItem
{
    public required string FboNo { get; set; }
    public int BoxSeq { get; set; }
    public int ItemSeq { get; set; }
    public required string Csku { get; set; }
    public string FboItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    /// <summary>저장 시점 FboCskuMaster.InvoiceDisplayName 스냅샷 — 하배출고이서 내보내기에서
    /// ItemName 대신 이 값을 우선 쓴다(비어있으면 ItemName으로 대체).</summary>
    public string? InvoiceDisplayName { get; set; }
    public int QtyPerBox { get; set; }
    public int Qty { get; set; }
    public string? ExpiryDate { get; set; }
}

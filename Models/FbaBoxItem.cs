namespace MiniERP2.Models;

/// <summary>
/// 아마존 FBA 박스 안의 품목 1줄. Asin/CommodityDescription/HsCode/ItemPrice/UnitWeightG/
/// QtyPerLayer는 저장 시점의 FbaCskuMaster 값을 스냅샷한다 — 마스터가 나중에 바뀌어도 과거 발주의
/// 재출력값(선적명세 등)이 변하면 안 되기 때문이다(기획서 §3.4). Qty는 1단 미만·비배수도 허용한다.
/// </summary>
public class FbaBoxItem
{
    public required string FbaNo { get; set; }
    public int BoxSeq { get; set; }
    public int ItemSeq { get; set; }
    public required string Csku { get; set; }
    public string ItemName { get; set; } = string.Empty;
    /// <summary>하배출고이서 "품목명" 칸 표기 스냅샷 — 비어있으면 ItemName으로 대체.</summary>
    public string? InvoiceDisplayName { get; set; }
    public string Asin { get; set; } = string.Empty;
    public string CommodityDescription { get; set; } = string.Empty;
    public string HsCode { get; set; } = string.Empty;
    public decimal ItemPrice { get; set; }
    public double UnitWeightG { get; set; }
    public int QtyPerLayer { get; set; }
    public int Qty { get; set; }
    public string? ExpiryDate { get; set; }
}

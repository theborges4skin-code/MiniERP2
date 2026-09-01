namespace MiniERP2.Models;

/// <summary>FBA 발주 이력 조회창(FbaHistoryForm)의 한 줄 — FbaOrder+FbaBox+FbaBoxItem 조인 결과.</summary>
public class FbaHistoryRow
{
    public required string FbaNo { get; set; }
    public DateTime OrderDate { get; set; }
    public string? ShipmentId { get; set; }
    public int BoxSeq { get; set; }

    /// <summary>FbaBoxItem의 PK 구성요소 — CSKU별 집계에서 박스 수를 중복 없이 셀 때 쓴다.</summary>
    public int ItemSeq { get; set; }
    public string Csku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string? ExpiryDate { get; set; }
    public string? TrackingNo { get; set; }
    public string Status { get; set; } = string.Empty;
}

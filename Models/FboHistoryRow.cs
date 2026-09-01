namespace MiniERP2.Models;

/// <summary>FBO 발주/출고 이력 조회창(FboHistoryForm)의 한 줄 — FboOrder+FboBox+FboBoxItem 조인 결과.</summary>
public class FboHistoryRow
{
    public required string FboNo { get; set; }
    public DateTime OrderDate { get; set; }
    public required string ChannelId { get; set; }
    public int BoxSeq { get; set; }

    /// <summary>FboBoxItem의 PK 구성요소 — 개별 CSKU 라인을 정확히 지목해 수정/복사/삭제할 때 쓴다.</summary>
    public int ItemSeq { get; set; }
    public string ReceiverDisplayName { get; set; } = string.Empty;
    public string Csku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string? ExpiryDate { get; set; }
    public string? TrackingNo { get; set; }
    public string Status { get; set; } = string.Empty;
}

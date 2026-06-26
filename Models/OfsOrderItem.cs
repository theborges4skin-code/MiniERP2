namespace MiniERP2.Models;

/// <summary>
/// OFS 창의 그리드에 표시될 단일 주문 항목을 나타냅니다.
/// </summary>
public class OfsOrderItem
{
    // 원본/표준화 데이터
    public string? ChannelCode { get; set; }
    public string? OrderNo { get; set; }
    public string? ProductName { get; set; }
    public string? OptionName { get; set; }
    public int Quantity { get; set; }
    public string? Recipient { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? DeliveryMessage { get; set; }
    // 매핑/변환 데이터
    public string? MappedSku { get; set; }
    public string? Status { get; set; }
    public string? TrackingNo { get; set; }
}
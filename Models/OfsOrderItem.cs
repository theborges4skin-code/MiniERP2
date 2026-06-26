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

    /// <summary>
    /// 택배사 출력양식(송장)에 쓸 간결한 품목 표시 문자열입니다(SkuMapper가 매핑 시 채워줌).
    /// 매핑된 SKU의 채널별 송장표시명이 설정되어 있을 때만 값이 채워지며, 그 외에는 null입니다
    /// (이 경우 택배사 양식 설정에서 ProductName 등 다른 속성을 직접 선택해 사용하면 됩니다).
    /// </summary>
    public string? InvoiceLabel { get; set; }
}
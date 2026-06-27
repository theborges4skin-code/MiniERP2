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

    /// <summary>
    /// 이 줄이 속한 "묶음(송장 1건 단위)"을 명시적으로 지정합니다. 비어있으면 같은 주문번호끼리
    /// 자동으로 한 묶음이 됩니다(기본값). 사용자가 OFS 그리드 컨텍스트 메뉴로 분리배송/합포장을
    /// 지정하면 여기에 실제 값이 채워져 기본값을 덮어씁니다. 실제 유효 그룹 키 계산은
    /// <see cref="Utils.ShipmentGrouping.GetEffectiveGroupId"/>를 사용하세요.
    /// </summary>
    public string? ShipmentGroupId { get; set; }
}
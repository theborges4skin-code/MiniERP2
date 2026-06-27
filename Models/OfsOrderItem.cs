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

    /// <summary>
    /// 발주일(채널 설정에서 "발주일" 헤더를 매핑한 경우만 채워짐). 누적발주서 채널(과거 이력까지
    /// 누적해서 담긴 발주서 파일)에서 발주 파일을 불러올 때 "최근 N일 이내" 항목만 골라 보여주는
    /// 선택창(Forms.CumulativeOrderSelectionDialog)에 쓰인다.
    /// </summary>
    public DateTime? OrderDate { get; set; }

    // 매핑/변환 데이터
    public string? MappedSku { get; set; }
    public string? Status { get; set; }
    public string? TrackingNo { get; set; }

    /// <summary>
    /// 매핑된 CSKU의 채널별 송장표시명입니다(SkuMapper가 매핑 시 채워줌, 설정이 없으면 null —
    /// 이 경우 <see cref="Utils.ShipmentGrouping"/>이 ProductName으로 대체합니다). 수량은 포함되지
    /// 않습니다 — 수량 표기 방식(택배사별 커스텀 형식)은 내보내기 시점에 따로 붙습니다.
    /// </summary>
    public string? InvoiceDisplayName { get; set; }

    /// <summary>
    /// 송장에 쓸 품목 표시문자열을 사용자가 직접 지정(override)한 경우에만 값이 채워집니다(OFS
    /// 미리보기에서 셀 직접편집/"CSKU 상품명으로 사용"/행 복사 등). null이면
    /// <see cref="Utils.ShipmentGrouping"/>이 InvoiceDisplayName+수량으로 자동 조합하고, 빈 문자열
    /// ("")이면 합포장된 다른 줄에 표시를 넘기고 이 줄은 표시에서 빠집니다.
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
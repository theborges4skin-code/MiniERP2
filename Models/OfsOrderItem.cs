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
    /// 이 줄이 속한 "묶음(송장 1건 단위)"을 명시적으로 지정합니다. 비어있으면 줄마다 별도 송장으로
    /// 취급됩니다(기본값 — 합포장은 택배사 프로그램이 다운스트림에서 주소 기준으로 자동 처리하므로
    /// MiniERP2가 임의로 합치지 않습니다). 사용자가 OFS 그리드 컨텍스트 메뉴로 분리배송/합포장을
    /// 지정하면 여기에 실제 값이 채워져 기본값을 덮어씁니다. 실제 유효 그룹 키 계산은
    /// <see cref="Utils.ShipmentGrouping.GetEffectiveGroupId"/>를 사용하세요.
    /// </summary>
    public string? ShipmentGroupId { get; set; }

    /// <summary>
    /// 이 줄이 발주 파일의 어느 행에서 왔는지 나타내는 안정 식별자입니다(<see cref="DataLoaders.OrderLoader"/>가
    /// "파일명#엑셀행번호"로 채움). 같은 파일을 다시 로드해도 같은 값이 나오므로, 출고이력에 저장할 때
    /// 객체 인스턴스 해시 대신 이 값으로 결정적인 <see cref="Utils.ShipmentGrouping.GetEffectiveGroupId"/>
    /// 키를 만들어 재로드 후 재저장 시에도 같은 발주 줄이 같은 이력 레코드로 매칭되게 한다. 수동 추가된
    /// 행처럼 파일에서 오지 않은 항목은 null로 남아 객체 해시 방식으로 폴백한다.
    /// </summary>
    public string? SourceRowKey { get; set; }

    /// <summary>
    /// 택배사 양식에 매핑된 OfsOrderItem 속성이 없는 헤더(예: 박스타입/내품수량/운임/선착불유무
    /// 등)의 값을 사용자가 직접 입력해 보관합니다(헤더 텍스트 → 값). OFS의 택배사 출력 미리보기에서
    /// 입력하며, 그 묶음의 대표 줄(Items[0])에만 저장됩니다 — <see cref="Utils.CourierFieldResolver"/>
    /// 참고.
    /// </summary>
    public Dictionary<string, string>? ManualFieldValues { get; set; }
}
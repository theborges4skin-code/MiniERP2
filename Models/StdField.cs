namespace MiniERP2.Models;

public enum StdField
{
    ProductName,
    OptionName,
    ProductNo,
    Quantity,
    SettlementAmount,
    ShippingFee,
    HandlingFee,
    Recipient,
    Phone,
    Address,
    DeliveryMessage,
    OrderDate,

    /// <summary>매출액 — 정산액(실제 지급액)과는 별개로, 정산파일에 따로 있는 총 판매금액 열.</summary>
    Revenue,

    /// <summary>
    /// 실제발송송장수 계산용 원본 송장번호 열. 행 단위로는 원본 값 그대로를 보관하고,
    /// 분석 시 중복을 제거한 개수를 채널 요약에만 표시한다(그리드에는 열로 노출하지 않음).
    /// </summary>
    TrackingNo,

    /// <summary>
    /// 주문번호 열. 쿠팡일반 등 주문단위 배송비 분배가 필요한 채널에서 채널설정 > 정산서 매핑에 지정한다.
    /// </summary>
    OrderNo,

    /// <summary>
    /// 세금계산서번호(계산서번호) 열. 쿠팡로켓 입고상세내역의 계산서번호 JOIN 키 및 쿠팡명세 시트 출력에 사용한다.
    /// </summary>
    TaxNo,

    /// <summary>
    /// 이벤트 유형 열. 아마존 Unified Transaction의 type 컬럼 등, 행 종류를 나타내는 값.
    /// Transfer(입금) 행 제거 등 채널별 후처리 필터에서 사용한다.
    /// </summary>
    EventType,

    /// <summary>
    /// 조건부매핑 상세조건에서 정산파일 원본 열(SettlementData.RawValues)을 가리키는 센티널.
    /// 실제 열 이름은 이 값을 쓰지 않고 MappingConditionDetail.RawFieldName에 별도로 담는다
    /// (예: 아마존 정산파일의 "sku" 열처럼 표준필드로 매핑되지 않은 원본 열을 조건으로 쓸 때).
    /// </summary>
    Raw,

    /// <summary>
    /// 비고(내부관리용 참고 메모). 배송메시지(DeliveryMessage)와 달리 택배 송장에는 실리지 않고,
    /// OFS 그리드에는 유무만 표시되며 발주확정 시 OutboundDetail.Remark로 이력에 남는다.
    /// 자동발주(표준) 채널 프리셋의 "비고" 열에서 주로 쓰인다.
    /// </summary>
    Remark,

    /// <summary>
    /// 채널힌트(자동발주(표준) 프리셋 전용). 이메일 원문에서 읽은 채널 단서 문자열 — 실제 채널
    /// 코드가 아니라 참고용 텍스트다. AutoOrderChannelResolver가 이 값을 각 채널의 AutoOrderHints와
    /// 대조해 실채널코드를 찾는 데만 쓰이고, 매핑/이력 등 다른 어떤 파이프라인에도 흘러가지 않는다.
    /// </summary>
    ChannelHint,
}

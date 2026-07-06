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
}

using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// 채널유형별로 어떤 표준필드가 매핑 대상인지 정의한다(§2.4). 채널설정 화면의 매핑 그리드와
/// 채널 일괄등록(엑셀) 기능이 같은 필드 목록을 공유해야 검증 기준이 어긋나지 않으므로 한 곳에 모아둔다.
/// </summary>
public static class ChannelFieldSets
{
    public static readonly StdField[] OrderMappingFields =
    [
        StdField.ProductNo, StdField.ProductName, StdField.OptionName, StdField.Quantity,
        StdField.Recipient, StdField.Phone, StdField.Address, StdField.DeliveryMessage, StdField.OrderDate,
        StdField.Remark, StdField.ChannelHint,
    ];

    /// <summary>쿠팡그로스/쿠팡로켓 외 모든 채널의 정산서 매핑 표준필드.
    /// OrderNo(주문번호)는 쿠팡일반/11번가처럼 배송비가 별도 행으로 분리되어 나오는 채널에서
    /// 주문 단위로 배송비를 상품 행에 재배분할 때 쓰인다(선택 매핑 — 비워두면 배분 없이 합산 처리).</summary>
    public static readonly StdField[] SettlementMappingFieldsDefault =
    [
        StdField.ProductName, StdField.OptionName, StdField.Quantity, StdField.OrderNo,
        StdField.Revenue, StdField.ShippingFee, StdField.SettlementAmount, StdField.TrackingNo,
    ];

    public static readonly StdField[] SettlementMappingFieldsCoupangGrowth =
    [
        StdField.ProductName, StdField.OptionName, StdField.Quantity,
        StdField.Revenue, StdField.SettlementAmount, StdField.ShippingFee, StdField.HandlingFee,
    ];

    /// <summary>쿠팡로켓 전용 필드: TaxNo(계산서번호)를 추가로 매핑한다.</summary>
    public static readonly StdField[] SettlementMappingFieldsCoupangRocket =
    [
        StdField.ProductName, StdField.OptionName, StdField.Quantity,
        StdField.Revenue, StdField.SettlementAmount, StdField.TrackingNo, StdField.TaxNo,
    ];

    /// <summary>채널설정 화면/일괄등록 검증 공통: 채널유형에 따른 허용 정산서 필드 목록.</summary>
    public static StdField[] ResolveSettlementMappingFields(ChannelType type) => type switch
    {
        ChannelType.CoupangGrowth => SettlementMappingFieldsCoupangGrowth,
        ChannelType.CoupangRocket => SettlementMappingFieldsCoupangRocket,
        _ => SettlementMappingFieldsDefault,
    };

    /// <summary>채널유형 무관 전체 정산서 표준필드 합집합(참고 시트/드롭다운용).</summary>
    public static readonly StdField[] AllSettlementMappingFields = SettlementMappingFieldsDefault
        .Concat(SettlementMappingFieldsCoupangGrowth)
        .Concat(SettlementMappingFieldsCoupangRocket)
        .Distinct()
        .ToArray();
}

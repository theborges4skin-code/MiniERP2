namespace MiniERP2.Utils;

/// <summary>
/// OutboundDetailTable 조회 시 LineKind 기준으로 어떤 라인을 포함할지 지정합니다
/// (샘플발송이력관리_개발기획서.md §4.4). WHERE 절 생성은
/// OutboundRepository.BuildLineKindWhere 한 곳으로 단일화합니다.
/// </summary>
public enum LineKindScope
{
    /// <summary>구분 무관 전체 라인.</summary>
    All,

    /// <summary>정상 거래 라인만(LineKind = '').</summary>
    SaleOnly,

    /// <summary>비매출 라인만(LineKind &lt;&gt; ''). kind를 지정하면 그 값으로 더 좁힌다.</summary>
    NonSaleOnly,
}

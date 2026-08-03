namespace MiniERP2.Models;

public class MappingRule
{
    public long Id { get; set; }
    public MappingRuleType RuleType { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string TargetSku { get; set; } = string.Empty;
    /// <summary>Settlement 전용 규칙: CSKU 없이 MSKU에만 매핑. TargetSku가 비면 이 값을 사용한다.</summary>
    public string TargetMsku { get; set; } = string.Empty;

    /// <summary>
    /// RuleExact/RuleTemp 전용(그 외 유형은 항상 null): 4필드(상품명+옵션명+수량+매출액) 신규 규칙의
    /// 수량. Quantity와 Price 둘 다 null이면 레거시(상품명+옵션명 2필드) 규칙으로 취급한다
    /// (매핑시스템 통합개편 기획서 §4.1).
    /// </summary>
    public int? Quantity { get; set; }

    /// <summary>RuleExact/RuleTemp 전용: 4필드 신규 규칙의 매출액(판매가) 기준. <see cref="Quantity"/> 참고.</summary>
    public decimal? Price { get; set; }
}

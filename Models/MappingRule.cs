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
}

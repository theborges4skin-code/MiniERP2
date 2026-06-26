namespace MiniERP2.Models;

public class MappingRule
{
    public long Id { get; set; }
    public MappingRuleType RuleType { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string TargetSku { get; set; } = string.Empty;
}

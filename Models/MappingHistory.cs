namespace MiniERP2.Models;

public class MappingHistory
{
    public long Id { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string OldSku { get; set; } = string.Empty;
    public string NewSku { get; set; } = string.Empty;
    public MappingRuleType MatchType { get; set; }
    public DateTime ChangedAt { get; set; }
}

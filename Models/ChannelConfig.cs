namespace MiniERP2.Models;

public class ChannelConfig
{
    public string ChannelCode { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public ChannelType ChannelType { get; set; } = ChannelType.General;
    public Dictionary<StdField, FieldMapping> FieldMappings { get; set; } = new();
    public List<GrowthAuxSource> GrowthAuxSources { get; set; } = new();
}

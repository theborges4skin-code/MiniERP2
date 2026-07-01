namespace MiniERP2.Models;

public class SalesChannel
{
    public string ChannelCode { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string? GroupName { get; set; }
    public bool IsFavorite { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime? LastUsedDate { get; set; }
}

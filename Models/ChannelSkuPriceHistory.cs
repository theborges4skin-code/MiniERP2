namespace MiniERP2.Models;

public class ChannelSkuPriceHistory
{
    public long Id { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string Msku { get; set; } = string.Empty;
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public DateTime ChangedAt { get; set; }
}

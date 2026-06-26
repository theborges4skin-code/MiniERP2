namespace MiniERP2.Models;

public class ChannelSkuPriceHistoryModel
{
    public long Id { get; set; }
    public required string ChannelCode { get; set; }
    public required string Msku { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public DateTime ChangedAt { get; set; }
}
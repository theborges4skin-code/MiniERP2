namespace MiniERP2.Models;

public class SettlementData
{
    public long Id { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string StdProductId { get; set; } = string.Empty;
    public decimal Amt { get; set; }
    public decimal Settlement { get; set; }
    public decimal Shipping { get; set; }
    public decimal Fee { get; set; }
}

namespace MiniERP2.Models;

public class OutboundDetail
{
    public long Id { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string TrackingNo { get; set; } = string.Empty;
    public string MskuCode { get; set; } = string.Empty;
    public int Qty { get; set; }
    public decimal SupplyPrice { get; set; }
    public DateTime CreatedAt { get; set; }
}

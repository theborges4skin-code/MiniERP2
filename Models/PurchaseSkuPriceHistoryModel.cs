namespace MiniERP2.Models;

/// <summary>매입SKU(PurchaseSkuTable)의 매입가 변경 이력입니다. 매출측 ChannelSkuPriceHistory와 대칭입니다.</summary>
public class PurchaseSkuPriceHistoryModel
{
    public long Id { get; set; }
    public required string ChannelCode { get; set; }
    public required string Msku { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Reason { get; set; }
    public string? Note { get; set; }
}

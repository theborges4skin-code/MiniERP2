namespace MiniERP2.Models;

public class ItemCostHistory
{
    public long Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal OldCost { get; set; }
    public decimal NewCost { get; set; }
    public DateTime ChangedAt { get; set; }
}

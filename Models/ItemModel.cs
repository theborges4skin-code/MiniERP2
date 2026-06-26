namespace MiniERP2.Models;

public class ItemModel
{
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal CostPrice { get; set; }
    public string? Reserve1 { get; set; }
    public string? Reserve2 { get; set; }
    public string? Reserve3 { get; set; }
    public string? ProductGroup { get; set; }
}

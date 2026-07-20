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

    /// <summary>B2B 등 kg 단위 매입/납품에 쓰는 기본 단위입니다. 기본값 "kg".</summary>
    public string Unit { get; set; } = "kg";
}

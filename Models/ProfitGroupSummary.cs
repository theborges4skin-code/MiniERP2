namespace MiniERP2.Models;

/// <summary>
/// 이익분석 결과를 마스터SKU의 상품그룹 단위로 집계한 한 줄.
/// </summary>
public class ProfitGroupSummary
{
    public string ProductGroup { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public int Qty { get; set; }
    public decimal Revenue { get; set; }
    public decimal Settlement { get; set; }
    public decimal Shipping { get; set; }
    public decimal Fee { get; set; }
    public decimal Profit { get; set; }
}

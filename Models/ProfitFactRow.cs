namespace MiniERP2.Models;

/// <summary>기간별·채널별·상품그룹별 이익 집계 팩트 (종합보고서용 피벗 소스).</summary>
public class ProfitFactRow
{
    public long Id { get; set; }
    public string Period { get; set; } = string.Empty;      // "YYYY-MM"
    public string ChannelCode { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string ProductGroup { get; set; } = string.Empty;
    public int Qty { get; set; }
    public decimal Revenue { get; set; }
    public decimal GrossProfit { get; set; }
    public string SavedAt { get; set; } = string.Empty;
}

/// <summary>기간별·채널별·상품그룹별 광고비 집계 팩트 (종합보고서용 피벗 소스).</summary>
public class AdFactRow
{
    public long Id { get; set; }
    public string Period { get; set; } = string.Empty;
    public string ChannelCode { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string ProductGroup { get; set; } = string.Empty;
    public decimal AdCost { get; set; }
    public string SavedAt { get; set; } = string.Empty;
}

namespace MiniERP2.Models;

/// <summary>정산 마진 계산기 그리드의 행 하나(CSKU/MSKU 한 건).</summary>
public class SimpleMarginCalcRow
{
    public string? ChannelCode { get; set; }
    public string? ChannelName { get; set; }
    public string? CskuCode { get; set; }
    public string? Msku { get; set; }
    public string? ItemName { get; set; }

    /// <summary>마스터/CSKU 원가 조회값(CSKU 불러오기 또는 조회 버튼으로 채워짐).</summary>
    public decimal CostPrice { get; set; }

    public decimal? Quantity { get; set; }
    public decimal? SaleAmount { get; set; }
    public decimal? SettlementAmount { get; set; }

    /// <summary>수수료율(소수, 0.1=10%).</summary>
    public decimal? FeeRate { get; set; }

    // ── 계산 결과 캐시(그리드 표시용) ──
    public bool IsComputable { get; set; }
    public string? ComputeReason { get; set; }
    public decimal? ProfitAmount { get; set; }
    public decimal? ProfitPerUnit { get; set; }
    public decimal? SalePerUnit { get; set; }
    public decimal? RevenueBasis { get; set; }
    public decimal? MarginRate { get; set; }
}

namespace MiniERP2.Models;

/// <summary>
/// 거래처 마감보드 [비매출 내역](샘플발송이력관리_개발기획서.md §6) — 거래처 × 구분 집계 한 줄.
/// 공급가·이익은 없다(비매출 라인의 SupplyPrice는 사실상 0) — 관리 대상은 나간 비용(원가)이다.
/// </summary>
public class NonSaleSummaryRow
{
    public string ChannelCode { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string LineKind { get; set; } = string.Empty;
    public int Count { get; set; }
    public int Qty { get; set; }
    public decimal CostAmount { get; set; }
    public List<NonSaleDetailLine> Lines { get; set; } = [];
}

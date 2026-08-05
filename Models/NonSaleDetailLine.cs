namespace MiniERP2.Models;

/// <summary>
/// 거래처 마감보드 [비매출 내역](샘플발송이력관리_개발기획서.md §6.3) 우측 그리드의 라인 상세.
/// </summary>
public class NonSaleDetailLine
{
    public DateTime LineDate { get; set; }
    public string CskuCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Qty { get; set; }

    /// <summary>원가(§6 규칙 — PartnerClosingLine.CostPrice와 동일한 우선순위로 산출).</summary>
    public decimal CostPrice { get; set; }

    public string Remark { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
}

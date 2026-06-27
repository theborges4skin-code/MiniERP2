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

    /// <summary>
    /// 발주이력 추적 상태입니다("발주확정"/"출고확정"). 실무에서 출고확정은 운송장번호가 있어야
    /// 성립하므로, 저장(발주확정) 시점에 운송장번호가 이미 있으면 "출고확정"으로 저장되고, 없으면
    /// "발주확정"으로 시작해 이후 운송장번호 업로드나 수동 발송확인 처리로 "출고확정"으로 바뀝니다.
    /// 마감 시점에 이 이력을 거래처 마감내역과 대조합니다.
    /// </summary>
    public string Status { get; set; } = "발주확정";

    /// <summary>
    /// "출고확정"으로 확정된 시각입니다. "발주확정" 상태면 null입니다.
    /// </summary>
    public DateTime? ConfirmedAt { get; set; }
}

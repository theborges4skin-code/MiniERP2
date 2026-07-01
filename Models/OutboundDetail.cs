namespace MiniERP2.Models;

public class OutboundDetail
{
    public long Id { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 이중 출고 방지(Upsert) 키. 분리배송이면 ShipmentGrouping.GetEffectiveGroupId 값,
    /// 일반 주문이면 OrderNo와 동일하다.
    /// </summary>
    public string ShipmentGroupKey { get; set; } = string.Empty;

    public string TrackingNo { get; set; } = string.Empty;
    public string MskuCode { get; set; } = string.Empty;
    public int Qty { get; set; }
    public decimal SupplyPrice { get; set; }
    public DateTime CreatedAt { get; set; }

    // 운송장번호를 나중에 업로드할 때 수령인 기준으로 매칭해야 하므로(택배사 운송장 파일에는
    // 주소/품목이 불분명하게 나와 신뢰할 수 없음) 발주확정 시점의 수령인/주소/품목명을 함께
    // 보관해둔다. 발주/출고 이력 관리창에서 동명이인 구분 시에도 이 정보를 보여준다.
    public string Recipient { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

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

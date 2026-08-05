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

    /// <summary>
    /// 이름과 달리 마스터SKU가 아니라 CSKU 코드입니다(OfsOrderItem.MappedSku를 그대로 저장 —
    /// SkuMapper가 매핑 규칙의 TargetSku, 즉 CSKU 코드로 해석하기 때문). B2B 원가 스냅샷 등에서
    /// 실제 마스터SKU가 필요하면 ChannelSkuRepository.ResolveMasterSku(ChannelCode, MskuCode)로
    /// 변환해야 한다.
    /// </summary>
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
    /// 비고(내부관리용 참고 메모). 발주확정 당시 <see cref="OfsOrderItem.Remark"/> 스냅샷 —
    /// 주로 자동발주(표준) 채널에서 채워지며, 발주/출고 이력 관리창에서 조회할 수 있다.
    /// </summary>
    public string Remark { get; set; } = string.Empty;

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

    // ── B2B 견적관리(§2.8) ──

    /// <summary>이 라인의 원가로 선택한 매입처 채널 코드입니다. null이면 ItemTable.CostPrice를 원가로 스냅샷합니다.</summary>
    public string? PurchaseChannelCode { get; set; }

    /// <summary>발주확정 당시 원가/kg 스냅샷입니다(PurchaseChannelCode 지정 시 그 매입가, 아니면 CostPrice).</summary>
    public decimal? PurchasePrice { get; set; }

    /// <summary>이 라인의 중량(kg)입니다. 발송헤더 운임을 라인별로 배부할 때 가중치로 사용합니다.</summary>
    public decimal? WeightKg { get; set; }

    /// <summary>
    /// G9 해소용 CSKU 전용 컬럼입니다(DbSchema.cs). 신규 라인부터만 채워지며, 과거분은 비어있으므로
    /// 이 값이 빈 문자열이면 <see cref="MskuCode"/>를 CSKU로 간주해 폴백해야 합니다.
    /// </summary>
    public string CskuCode { get; set; } = string.Empty;

    /// <summary>
    /// 거래처 마감보드(거래처마감보드_개발기획서.md §4) 귀속월 수동 고정값(YYYY-MM). 빈 문자열이면
    /// 미조정 상태이며, 보드가 ConfirmedAt의 연월로 자동 판정합니다.
    /// </summary>
    public string ClosingPeriod { get; set; } = string.Empty;

    /// <summary>
    /// 비매출성 발송 구분(샘플발송이력관리_개발기획서.md §2 D1). 비어있으면 정상 거래, 그 외에는
    /// <see cref="Utils.LineKinds.All"/>에 정의된 값 중 하나(샘플/CS/기타)이며 마감·정산·명세표
    /// 집계에서 자동 제외됩니다.
    /// </summary>
    public string LineKind { get; set; } = string.Empty;
}

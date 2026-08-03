namespace MiniERP2.Models;

/// <summary>
/// 거래처 마감보드(§C3) 대조 화면에 바인딩되는 요약 DTO. 확정 전이면 OutboundDetailTable에서
/// 즉시 계산한 라이브 집계이고, 확정 후면 PartnerClosingTable/PartnerClosingLineTable 스냅샷에서
/// 그대로 읽어온다 — 어느 쪽인지는 <see cref="ClosingId"/>의 존재 여부로 판단한다.
/// </summary>
public class PartnerClosingSummary
{
    public string Period { get; set; } = string.Empty;
    public string PartyKey { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public bool IsManual { get; set; }

    /// <summary>이미 PartnerClosingTable에 헤더 행이 있으면 그 Id, 없으면 null(라이브 집계 상태).</summary>
    public long? ClosingId { get; set; }

    /// <summary>미확인 / 대조중 / 확정 / 발행완료.</summary>
    public string Status { get; set; } = "미확인";

    public decimal TotalQty { get; set; }
    public decimal TotalSupply { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal FreightAllocated { get; set; }

    /// <summary>미출고 잔량 건수(§4 — 발주확정만 되고 아직 출고확정 안 된 건).</summary>
    public int UnshippedCount { get; set; }

    /// <summary>
    /// 미출고 잔량 라인 상세(라이브 집계일 때만 채워짐). 마감보드 라인 상세에서 확정 라인과 함께
    /// 보여줘 그 자리에서 수정·삭제·출고확정 처리를 할 수 있게 하기 위함이다.
    /// </summary>
    public List<PartnerClosingLine> UnshippedLines { get; set; } = [];

    /// <summary>
    /// 세션 한정 런타임 해시로 생성된(=SourceRowKey 없는 수동 추가) ShipmentGroupKey를 가진 라인이
    /// 섞여 있으면 true(§10 — 마감 확정 전 집계가 흔들릴 수 있다는 경고 배지용).
    /// </summary>
    public bool HasUnstableKeyLines { get; set; }

    public bool FreightFallbackByCount { get; set; }

    public string ReconcileNote { get; set; } = string.Empty;
    public DateTime? ConfirmedAt { get; set; }
    public long? DocHistoryId { get; set; }

    public List<PartnerClosingLine> Lines { get; set; } = [];
}

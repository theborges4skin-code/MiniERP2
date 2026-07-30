namespace MiniERP2.Models;

/// <summary>
/// 마감 확정 시점 라인 스냅샷(거래처마감보드_개발기획서.md §5.3). 확정 이후 원본
/// OutboundDetailTable 라인이 편집·삭제되어도 발행된 명세표와 어긋나지 않도록 값을 복사해 둔다.
/// </summary>
public class PartnerClosingLine
{
    public long Id { get; set; }

    public long ClosingId { get; set; }

    /// <summary>원본 라인 참조(수동 행은 null).</summary>
    public long? OutboundDetailId { get; set; }

    /// <summary>출고확정일.</summary>
    public DateTime? LineDate { get; set; }

    /// <summary>OutboundDetail.CskuCode 우선, 비어 있으면 MskuCode 폴백(§2 주의사항).</summary>
    public string CskuCode { get; set; } = string.Empty;

    /// <summary>ResolveMasterSku 변환 결과.</summary>
    public string MasterSku { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public decimal Qty { get; set; }

    /// <summary>납품가.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>원가(§6 규칙).</summary>
    public decimal CostPrice { get; set; }

    /// <summary>라인 이익(운임 미반영).</summary>
    public decimal Profit { get; set; }
}

namespace MiniERP2.Models;

/// <summary>
/// 거래처×월 마감 헤더(거래처마감보드_개발기획서.md §5.2). 라인 스냅샷은
/// <see cref="PartnerClosingLine"/>에 별도 보관한다.
/// </summary>
public class PartnerClosing
{
    public long Id { get; set; }

    /// <summary>YYYY-MM.</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>`CH:{채널코드}` 또는 `MANUAL:{순번}`.</summary>
    public string PartyKey { get; set; } = string.Empty;

    /// <summary>표시용 거래처명(수동 행은 직접 입력).</summary>
    public string PartyName { get; set; } = string.Empty;

    public bool IsManual { get; set; }

    /// <summary>미확인 / 대조중 / 확정 / 발행완료.</summary>
    public string Status { get; set; } = "미확인";

    public decimal TotalQty { get; set; }
    public decimal TotalSupply { get; set; }
    public decimal TotalCost { get; set; }

    /// <summary>운임 배부 반영 후 이익.</summary>
    public decimal TotalProfit { get; set; }

    public decimal FreightAllocated { get; set; }

    /// <summary>대조 비고(거래처 제공 마감내역과의 차이 등).</summary>
    public string ReconcileNote { get; set; } = string.Empty;

    /// <summary>확정 시각. null이면 미확정.</summary>
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>발행된 문서(DocHistoryTable.Id) 연결.</summary>
    public long? DocHistoryId { get; set; }
}

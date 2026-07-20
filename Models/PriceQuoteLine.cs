namespace MiniERP2.Models;

/// <summary>
/// 견적/가격 기록 관리(견적기록관리_개발기획서_확정본.md §3.2)의 견적 라인(품목 1줄).
/// </summary>
public class PriceQuoteLine
{
    public int Id { get; set; }
    public int QuoteId { get; set; }
    public int RowNo { get; set; }

    /// <summary>PriceKind=Supply일 때 필수(매입 견적은 CSKU 개념이 없어 공란).</summary>
    public string CskuCode { get; set; } = string.Empty;

    /// <summary>항상 채움 — 매입/집계 기준축.</summary>
    public string Msku { get; set; } = string.Empty;

    /// <summary>전달 당시 품명 스냅샷.</summary>
    public string ItemNameSnap { get; set; } = string.Empty;

    public string Spec { get; set; } = string.Empty;

    /// <summary>EA / kg / BOX …</summary>
    public string Unit { get; set; } = "EA";

    /// <summary>WithQty 양식에서만 유효(UnitOnly는 0).</summary>
    public decimal Qty { get; set; }

    /// <summary>직전 유효가. NULL = 신규품목.</summary>
    public decimal? OldPrice { get; set; }

    public decimal NewPrice { get; set; }

    /// <summary>계산 저장값 — 문서 재출력 시 당시 계산결과가 그대로 재현되도록 컬럼으로 보관한다.</summary>
    public decimal SupplyAmount { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }

    /// <summary>원가상승 / 환율 / 물동조정 / 신규 등.</summary>
    public string ChangeReason { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;
    public bool IsApplied { get; set; }

    /// <summary>자동 Draft 라인에서 승격된 경우 원본 라인 Id(§7.2, 미착수).</summary>
    public int? PromotedFrom { get; set; }
}

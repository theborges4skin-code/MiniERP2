namespace MiniERP2.Models;

/// <summary>
/// 견적/가격 기록 관리(견적기록관리_개발기획서_확정본.md §3.1)의 견적 헤더. 견적 1건 = 1회 전달 단위.
/// </summary>
public class PriceQuote
{
    public int Id { get; set; }
    public string QuoteNo { get; set; } = string.Empty;
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>Supply(납품) / Purchase(매입)</summary>
    public string PriceKind { get; set; } = "Supply";

    /// <summary>UnitOnly(기본형) / WithQty(상세형)</summary>
    public string QuoteFormType { get; set; } = "UnitOnly";

    /// <summary>Manual / OfsMapping / Import — OfsMapping은 자동 Draft(§7.2)로 만들어진 건.</summary>
    public string Origin { get; set; } = "Manual";

    public string Title { get; set; } = string.Empty;
    public DateTime? QuoteDate { get; set; }
    public DateTime? EffectiveFrom { get; set; }

    /// <summary>NULL = 무기한. 다음 Applied 견적이 생기면 자동으로 그 EffectiveFrom - 1일로 채워진다(R5, 미착수).</summary>
    public DateTime? EffectiveTo { get; set; }

    public bool AutoApply { get; set; }

    /// <summary>Draft / Sent / Scheduled / Applied / Superseded / Rejected / Void</summary>
    public string Status { get; set; } = "Draft";

    public string DeliveryMethod { get; set; } = string.Empty;
    public DateTime? DeliveredAt { get; set; }
    public string DeliveredTo { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    /// <summary>VatExcl / VatIncl</summary>
    public string PriceBasis { get; set; } = "VatExcl";

    /// <summary>리비전 체인 뿌리. 원본이면 NULL.</summary>
    public int? RootQuoteId { get; set; }

    /// <summary>0=원본, 1,2,… 개정 차수.</summary>
    public int RevisionNo { get; set; }

    /// <summary>이 건을 덮은(개정한) 견적 Id. NULL이면 최신본.</summary>
    public int? SupersededBy { get; set; }

    public string RevisionReason { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

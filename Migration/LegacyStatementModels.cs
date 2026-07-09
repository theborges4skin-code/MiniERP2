namespace MiniERP2.Migration;

/// <summary>레거시 거래명세표 시트 1장을 파싱한 결과. DB에는 아직 아무것도 쓰지 않는다(순수 파싱 레이어).</summary>
public class ParsedStatementSheet
{
    public string SourceFileName { get; set; } = "";
    public string SourceSheetName { get; set; } = "";

    /// <summary>스펙 §3.4 템플릿유형 코드. 예: "Y-SEP"(연도있음/분리형), "N-INC"(연도없음/포함단일형).</summary>
    public string TemplateSignature { get; set; } = "";

    public ParsedPartyInfo? Buyer { get; set; }
    public DateTime? IssueDate { get; set; }
    public List<ParsedStatementLine> Lines { get; set; } = new();

    /// <summary>노이즈/사본/폐기/총계불일치 등 — 스펙 §3.6 "차단 아님, 표기만" 원칙. 예: "NOISE_NO_HEADER", "COPY_SUSPECTED", "DISCARDED", "TOTALS_MISMATCH".</summary>
    public List<string> Flags { get; } = new();

    public decimal? TotalsRowQty { get; set; }
    public decimal? TotalsRowSupply { get; set; }
    public decimal? TotalsRowTax { get; set; }
    public decimal? TotalsRowTotal { get; set; }

    public bool TotalsReconciled =>
        !Flags.Contains("TOTALS_MISMATCH") && (TotalsRowTotal.HasValue || TotalsRowSupply.HasValue);
}

public class ParsedPartyInfo
{
    public string RegNo { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string CeoName { get; set; } = "";
    public string Address { get; set; } = "";
    public string BizType { get; set; } = "";
    public string BizItem { get; set; } = "";
}

public class ParsedStatementLine
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
    public string ItemName { get; set; } = "";
    public string Spec { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public bool UnitPriceVatIncluded { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string Note { get; set; } = "";
}

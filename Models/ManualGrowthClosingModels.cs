namespace MiniERP2.Models;

/// <summary>
/// 이공그로스수동마감(ManualGrowthClosing_Spec.md) 라인 1건. 파일에서 막 읽은 상태와 DB에 스냅샷된
/// 확정 상태를 같은 모델로 다룬다 — 금액 기준은 마감/이익분석(SettlementForm)과 동일하게 VAT포함이다
/// (매출 = 공급가액 + 세액, 원가 = CSKU 개별원가/마스터 대표원가(모두 VAT포함 저장), 이익 = 매출 − 원가×수량).
/// </summary>
public class ManualGrowthLine
{
    /// <summary>귀속 마감월 'yyyy-MM'. 시트명 앞 4자리(YYMM)로 정한다.</summary>
    public string Period { get; set; } = "";
    public string SourceSheetName { get; set; } = "";
    public int RowNo { get; set; }
    /// <summary>'yyyy-MM-dd'. 연/월/일 칸이 비어 있거나 잘못된 날짜면 빈 문자열.</summary>
    public string LineDate { get; set; } = "";
    public string ItemName { get; set; } = "";
    /// <summary>상품명을 정규화한 값 = CSKU코드(스펙 D2).</summary>
    public string CskuCode { get; set; } = "";
    public string MasterSku { get; set; } = "";
    public string ProductGroup { get; set; } = "";
    public decimal Qty { get; set; }
    /// <summary>명세표 단가 원문(양식에 따라 VAT별도/포함 — <see cref="UnitPriceVatIncluded"/>).</summary>
    public decimal UnitPrice { get; set; }
    public bool UnitPriceVatIncluded { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal Tax { get; set; }
    /// <summary>원가단가(VAT포함).</summary>
    public decimal CostPrice { get; set; }
    public decimal Profit { get; set; }
    public bool PriceMismatch { get; set; }
    /// <summary>라인 날짜의 월이 시트명 마감월과 다름(경고만, 차단 없음).</summary>
    public bool DateMismatch { get; set; }

    public decimal Revenue => SupplyAmount + Tax;
    public decimal Cost => CostPrice * Qty;
    public bool IsUnassigned => string.IsNullOrWhiteSpace(MasterSku);
    public decimal? MarginRate => Revenue == 0 ? null : Profit / Revenue;

    public string StatusText
    {
        get
        {
            if (IsUnassigned) return "미배정";
            var warnings = new List<string>();
            if (PriceMismatch) warnings.Add("단가불일치");
            if (DateMismatch) warnings.Add("월불일치");
            return warnings.Count == 0 ? "정상" : string.Join(",", warnings);
        }
    }
}

/// <summary>대상 시트 1장을 읽은 결과(구분행 제외·마감월 결정까지 끝난 상태).</summary>
public class ManualGrowthSheetResult
{
    public string SourceFileName { get; set; } = "";
    public string SheetName { get; set; } = "";
    public string Period { get; set; } = "";
    public List<ManualGrowthLine> Lines { get; set; } = new();
    public int ExcludedDividerCount { get; set; }
    public bool TotalsReconciled { get; set; }
    public List<string> Flags { get; set; } = new();
}

/// <summary>마감 헤더(ManualGrowthClosingTable). 한 마감월 = 한 레코드(같은 달 시트가 여럿이면 합친다).</summary>
public class ManualGrowthClosing
{
    public const string StatusConfirmed = "확정";
    public const string StatusUnconfirmed = "미확정";

    public long Id { get; set; }
    public string Period { get; set; } = "";
    public string ChannelCode { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public string SourceSheetName { get; set; } = "";
    public string Status { get; set; } = StatusUnconfirmed;
    public decimal TotalQty { get; set; }
    public decimal TotalSupply { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalProfit { get; set; }
    public string StatusFlags { get; set; } = "";
    public string? ConfirmedAt { get; set; }
    public string CreatedAt { get; set; } = "";
    public List<ManualGrowthLine> Lines { get; set; } = new();

    public decimal TotalRevenue => TotalSupply + TotalTax;
    public string MarginRateText => TotalRevenue == 0 ? "" : (TotalProfit / TotalRevenue).ToString("0.0%");
}

/// <summary>CSKU 요약 탭 1행(마감월×CSKU).</summary>
public class ManualGrowthCskuSummary
{
    public string Period { get; set; } = "";
    public string CskuCode { get; set; } = "";
    public string MasterSku { get; set; } = "";
    public string ProductGroup { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public decimal Profit { get; set; }
    public string MarginRateText => Revenue == 0 ? "" : (Profit / Revenue).ToString("0.0%");
}

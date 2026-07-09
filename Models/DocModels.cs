namespace MiniERP2.Models;

public class DocParty
{
    public int Id { get; set; }
    public string ProfileName { get; set; } = "";
    public string RegNo { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string CeoName { get; set; } = "";
    public string Address { get; set; } = "";
    public string BizType { get; set; } = "";
    public string BizItem { get; set; } = "";
    public string Tel { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsDefaultSupplier { get; set; }

    /// <summary>비어있지 않으면 특정 채널(SalesChannelTable.ChannelCode)의 공급받는자 프로필임을 뜻한다.</summary>
    public string ChannelCode { get; set; } = "";

    /// <summary>ChannelCode 연결 여부로 자동 계산된다(DocPartyRepository.Save 참고) — 직접 토글하지 않는다.</summary>
    public bool IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DocParty Clone() => (DocParty)MemberwiseClone();

    public override string ToString() => string.IsNullOrWhiteSpace(CompanyName) ? "(이름 없음)" : CompanyName;
}

// ── 거래명세표 ──────────────────────────────────────────────────────────

public class DocLineItem
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
    public string ItemName { get; set; } = "";
    public string Spec { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public string Note { get; set; } = "";

    public decimal SupplyAmount => Math.Round(Qty * UnitPrice, 0, MidpointRounding.AwayFromZero);
}

public enum DocType
{
    TradeStatementVatExcl,
    TradeStatementVatIncl,
    QuoteBasic,
    QuoteWithQty,
    PriceAdjustment,
    SalesLedger
}

public class TradeStatementDoc
{
    public DocType DocType { get; set; } = DocType.TradeStatementVatExcl;
    public DocParty Supplier { get; set; } = new();
    public DocParty Buyer { get; set; } = new();
    public List<DocLineItem> Lines { get; set; } = new();
    public DateTime IssueDate { get; set; } = DateTime.Today;
    public string FooterNote { get; set; } = "";
    public string TaxInvoiceEmail { get; set; } = "";
    public string Unpaid { get; set; } = "";
    public string Receiver { get; set; } = "";
    public string? StampImagePath { get; set; }

    public bool IsVatExcluded => DocType == DocType.TradeStatementVatExcl;
    public int MaxLines => IsVatExcluded ? 18 : 20;

    public decimal TotalQty => Lines.Where(l => !IsEmpty(l)).Sum(l => l.Qty);
    public decimal TotalSupply => Lines.Where(l => !IsEmpty(l)).Sum(l => l.SupplyAmount);
    public decimal TotalTax => Math.Round(TotalSupply * 0.1m, 0, MidpointRounding.AwayFromZero);
    public decimal GrandTotal => TotalSupply + TotalTax;

    private static bool IsEmpty(DocLineItem l) => string.IsNullOrWhiteSpace(l.ItemName) && l.Qty == 0 && l.UnitPrice == 0;
}

// ── 매출장(내부용) ──────────────────────────────────────────────────────

public class SalesLedgerLineItem
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
    public string ItemName { get; set; } = "";
    public string Spec { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal CostPrice { get; set; }
    public string Note { get; set; } = "";

    public decimal SupplyAmount => Math.Round(Qty * UnitPrice, 0, MidpointRounding.AwayFromZero);
    public decimal ProfitAmount => SupplyAmount - Math.Round(Qty * CostPrice, 0, MidpointRounding.AwayFromZero);
}

/// <summary>
/// 세금계산서가 발행되는 사업자(채널)별로 품목·원가·이익을 내부 검토용으로 정리하는 문서.
/// 거래명세표와 동일한 틀을 쓰되, 원가/이익 열을 각각 표시하거나 생략할 수 있다.
/// </summary>
public class SalesLedgerDoc
{
    public DocParty Supplier { get; set; } = new();
    public DocParty Buyer { get; set; } = new();
    public List<SalesLedgerLineItem> Lines { get; set; } = new();
    public DateTime IssueDate { get; set; } = DateTime.Today;
    public string FooterNote { get; set; } = "";
    public string? StampImagePath { get; set; }

    public bool ShowCost { get; set; } = true;
    public bool ShowProfit { get; set; } = true;

    public const int MaxLines = 18;

    public decimal TotalQty => Lines.Where(l => !IsEmpty(l)).Sum(l => l.Qty);
    public decimal TotalSupply => Lines.Where(l => !IsEmpty(l)).Sum(l => l.SupplyAmount);
    public decimal TotalCost => Lines.Where(l => !IsEmpty(l)).Sum(l => Math.Round(l.Qty * l.CostPrice, 0, MidpointRounding.AwayFromZero));
    public decimal TotalProfit => Lines.Where(l => !IsEmpty(l)).Sum(l => l.ProfitAmount);

    private static bool IsEmpty(SalesLedgerLineItem l) => string.IsNullOrWhiteSpace(l.ItemName) && l.Qty == 0 && l.UnitPrice == 0;
}

// ── 견적서 ──────────────────────────────────────────────────────────────

public class QuoteLineItem
{
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Packing { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal Qty { get; set; }       // 수량포함 버전에서만 사용
    public string Note { get; set; } = "";

    public decimal Amount => Math.Round(Qty * UnitPrice, 0, MidpointRounding.AwayFromZero);
}

public class QuoteDoc
{
    public DocType DocType { get; set; } = DocType.QuoteBasic;
    public DocParty Supplier { get; set; } = new();
    public string RecipientName { get; set; } = "";   // 수신
    public string DocTitle { get; set; } = "견 적 서";
    public string HeaderText { get; set; } = "";       // 인사문
    public string PriceBasis { get; set; } = "VAT 포함"; // 가격기준 라벨
    public List<QuoteLineItem> Lines { get; set; } = new();
    public string FooterNote { get; set; } = "";       // 기타 안내
    public DateTime IssueDate { get; set; } = DateTime.Today;
    public string? StampImagePath { get; set; }

    public bool IsBasic => DocType == DocType.QuoteBasic;

    public decimal TotalQty => NonEmpty.Sum(l => l.Qty);
    public decimal TotalAmount => NonEmpty.Sum(l => l.Amount);
    public decimal TotalTax => Math.Round(TotalAmount * 0.1m, 0, MidpointRounding.AwayFromZero);
    public decimal GrandTotal => TotalAmount + TotalTax;

    private IEnumerable<QuoteLineItem> NonEmpty =>
        Lines.Where(l => !string.IsNullOrWhiteSpace(l.ItemName) || l.UnitPrice != 0);
}

// ── 가격조정 공문 ────────────────────────────────────────────────────────

public class PriceAdjLineItem
{
    public int No { get; set; }
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal PriceBefore { get; set; }
    public decimal PriceAfter { get; set; }
    public string Note { get; set; } = "";
}

public class PriceAdjDoc
{
    public DocParty Supplier { get; set; } = new();
    public string RecipientName { get; set; } = "";
    public string DocNo { get; set; } = "";
    public string DocTitle { get; set; } = "제품 단가 조정에 관한 건";
    public string BodyText { get; set; } = "";        // 상단 안내문 (즐겨찾기 또는 직접 입력)
    public List<PriceAdjLineItem> Lines { get; set; } = new();
    public DateTime ApplyDate { get; set; } = DateTime.Today;
    public DateTime IssueDate { get; set; } = DateTime.Today;
    public string FooterNote { get; set; } = "";
    public string PriceBasis { get; set; } = "VAT 포함";
    public string? StampImagePath { get; set; }
}

// ── 즐겨찾기 문구 ────────────────────────────────────────────────────────

public class DocFavoritePhrase
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Category { get; set; } = "일반";  // 인상 / 인하 / 일반
    public bool IsFavorite { get; set; }

    public override string ToString() => $"[{Category}] {Title}";
}

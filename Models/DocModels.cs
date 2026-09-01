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

    /// <summary>
    /// 이 거래처(주로 공급자)를 불러올 때 자동으로 채울 기본 도장 이미지 경로입니다(거래처 관리
    /// 창에서 지정). 문서관리 화면 하단의 도장 업로드는 이 값과 별개이며, 그쪽에서 직접 업로드하면
    /// 그 값이 우선 적용됩니다(DocsForm.FillPartyFields 참고).
    /// </summary>
    public string? StampImagePath { get; set; }

    /// <summary>
    /// 온라인 거래처 취합(OnlinePartnerConsolidation_Spec.md §4.3·§5). 같은 CompanyName을 공유하는
    /// 여러 채널 중 납품단가(ChannelSkuTable.SupplyPrice) 조회의 기준이 되는 대표 채널인지.
    /// CompanyName 그룹당 1개여야 하며, DB 제약이 아니라 저장 시점(ChannelConfigForm)에 검증한다.
    /// DocPartyTable 전체에서 단일한 IsDefaultSupplier(대표 공급받는자)와는 스코프가 다르다.
    /// </summary>
    public bool IsPriceMaster { get; set; }

    /// <summary>배송 1건당 청구액(원). 온라인 거래처 취합의 배송비 청구액 계산에 쓰인다. 기본 3,000원.</summary>
    public decimal ShippingFeePerShipment { get; set; } = 3000m;

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

    /// <summary>
    /// true(기본값)면 CSKU 납품단가(VAT포함 기준, §PartnerClosingDocumentBuilder)를 10/11로 나눈
    /// 공급가 기준으로 단가/원가를 표시한다. false면 CSKU 값 그대로(VAT포함)를 표시한다.
    /// 어느 쪽이든 합계금액이 원본 라인 합계와 어긋나지 않도록 라인 병합 후 변환한다.
    /// </summary>
    public bool IsVatExcluded { get; set; } = true;

    public const int MaxLines = 18;

    public decimal TotalQty => Lines.Where(l => !IsEmpty(l)).Sum(l => l.Qty);
    public decimal TotalSupply => Lines.Where(l => !IsEmpty(l)).Sum(l => l.SupplyAmount);
    public decimal TotalCost => Lines.Where(l => !IsEmpty(l)).Sum(l => Math.Round(l.Qty * l.CostPrice, 0, MidpointRounding.AwayFromZero));
    public decimal TotalProfit => Lines.Where(l => !IsEmpty(l)).Sum(l => l.ProfitAmount);

    private static bool IsEmpty(SalesLedgerLineItem l) => string.IsNullOrWhiteSpace(l.ItemName) && l.Qty == 0 && l.UnitPrice == 0;
}

/// <summary>
/// 거래처 마감보드(거래처마감보드_개발기획서.md §9) 일괄 매출장 출력의 "현황" 시트 1행.
/// 마감확정 여부와 무관하게 선택된 거래처 전부를 나열하되(DocumentExporter.
/// ExportSalesLedgersCombined 참고), 실제 매출장 시트는 IsConfirmed인 거래처만 생긴다.
/// </summary>
public class SalesLedgerOverviewRow
{
    public string PartyName { get; set; } = "";
    public string Status { get; set; } = "";
    public bool IsConfirmed { get; set; }
    public decimal TotalQty { get; set; }
    public decimal TotalSupply { get; set; }
    public decimal TotalProfit { get; set; }
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

    /// <summary>입력한 단가×수량. VAT별도면 공급가(세전), VAT포함이면 그 자체가 합계금액(세후)이다.</summary>
    public decimal Amount => Math.Round(Qty * UnitPrice, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// VAT별도일 때만 공급가의 10%를 별도 세액으로 얹는다. VAT포함은 입력한 단가에 이미 세금이
    /// 포함돼 있다고 보고 추가로 얹지 않는다(0) — 거래명세표(TradeStatementDoc/DocumentExporter의
    /// WriteDataHeader)의 VAT포함 처리와 같은 원칙.
    /// </summary>
    public decimal Tax(bool vatExcluded) => vatExcluded ? Math.Round(Amount * 0.1m, 0, MidpointRounding.AwayFromZero) : 0m;

    public decimal LineTotal(bool vatExcluded) => Amount + Tax(vatExcluded);
}

public class QuoteDoc
{
    public DocType DocType { get; set; } = DocType.QuoteBasic;
    public DocParty Supplier { get; set; } = new();
    public string RecipientName { get; set; } = "";   // 수신
    public string DocTitle { get; set; } = "견 적 서";
    public string HeaderText { get; set; } = "";       // 인사문 — 줄바꿈으로 여러 줄 입력 가능(비우면 기본 문구 사용)
    public string PriceBasis { get; set; } = "VAT 포함"; // 가격기준 라벨 — 기본형은 표시 문구로만 쓰고, 수량포함형은 세액 계산/열 구성에도 실제로 반영한다(IsVatExcluded 참고).
    public List<QuoteLineItem> Lines { get; set; } = new();
    public string FooterNote { get; set; } = "";       // "2. 기타" 비고 — 줄바꿈으로 구분, 최대 10줄까지 표시
    public DateTime IssueDate { get; set; } = DateTime.Today;
    public string? StampImagePath { get; set; }

    public bool IsBasic => DocType == DocType.QuoteBasic;

    /// <summary>
    /// "VAT 별도" 문구인지 판별한다(기본형은 표시 문구로만 쓰이므로 실제 계산엔 영향 없음). 수량포함형만
    /// 이 값에 따라 공급가/세액 분리 여부가 갈린다(TotalTax/GrandTotal, DocumentExporter.ExportQuote 참고).
    /// </summary>
    public bool IsVatExcluded => PriceBasis.Replace(" ", "").Contains("별도");

    public decimal TotalQty => NonEmpty.Sum(l => l.Qty);
    public decimal TotalAmount => NonEmpty.Sum(l => l.Amount);
    public decimal TotalTax => IsVatExcluded ? Math.Round(TotalAmount * 0.1m, 0, MidpointRounding.AwayFromZero) : 0m;
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

    /// <summary>
    /// "CSKU 불러오기"로 채운 줄만 값이 있다(수기 입력 줄은 비어있음). 값이 있으면 "납품가 반영"
    /// 버튼으로 이 줄의 PriceAfter를 실제 ChannelSkuTable.SupplyPrice에 반영할 수 있다.
    /// </summary>
    public string? ChannelCode { get; set; }
    public string? CskuCode { get; set; }
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

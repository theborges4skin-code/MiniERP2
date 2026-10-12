namespace MiniERP2.Models;

/// <summary>
/// 수출요약보고서 화면(ExportSummaryForm)이 사용하는 설정. 트랙별(신고/판매/송금) 파일에서
/// 어느 컬럼을 읽을지, 마켓을 어떻게 식별할지를 정의한다. `export_summary_config.json`에 저장되며
/// 코드 수정 없이 값만 고쳐서 헤더 변경이나 신규 마켓 추가에 대응할 수 있다.
/// </summary>
public class ExportSummaryConfig
{
    public List<ExportSummaryMarket> Markets { get; set; } = new();
    public DeclarationTrackConfig DeclarationTrack { get; set; } = new();
    public List<SalesMarketMapping> SalesMarkets { get; set; } = new();
    public RemittanceTrackConfig RemittanceTrack { get; set; } = new();

    /// <summary>자사몰(PayPal 거래내역 CSV): 매출액=Gross, 정산액=Net.</summary>
    public PaypalTrackConfig PaypalTrack { get; set; } = new();

    /// <summary>자사몰 Wix 주문내역(Sales → Orders → Export CSV). 집계는 PayPal 기준이고, Wix는 PayPal 누락 확인용 대조에만 쓴다.</summary>
    public WixOrderTrackConfig WixOrderTrack { get; set; } = new();

    /// <summary>아마존 거래내역 CSV(Date Range Report → Transaction)에서 월별 매출액(Income 합계)을 계산하는 설정.</summary>
    public AmazonTransactionTrackConfig AmazonTransactionTrack { get; set; } = new();
}

/// <summary>보고서에 항상 표시할 마켓 마스터 목록(해당 월에 트랙별 데이터가 없어도 행은 유지).</summary>
public class ExportSummaryMarket
{
    public string MarketCode { get; set; } = "";
    public string MarketName { get; set; } = "";
    public string Currency { get; set; } = "";
    /// <summary>tidy 피벗에서 마켓을 묶는 상위 그룹. 예: 일피, 아마존US, 아마존JP, 미사몰, 직거래.</summary>
    public string MarketGroup { get; set; } = "";
}

public class DeclarationTrackConfig
{
    public int HeaderRow { get; set; } = 1;
    public string DateColumn { get; set; } = "신고일자";
    public string AmountColumn { get; set; } = "결제금액";
    public string CurrencyColumn { get; set; } = "통화코드";

    /// <summary>선택. 수출신고 대사 결과에 표시할 신고번호 컬럼. 파일에 없으면 빈 값으로 둔다.</summary>
    public string DeclarationNoColumn { get; set; } = "신고번호";

    /// <summary>통화코드 → 마켓코드. USD처럼 여러 마켓이 공유하는 통화는 여기 넣지 않고 미매핑으로
    /// 남겨 사용자가 별도 확인하게 한다(기획서 7장 확인 필요 사항 1번).</summary>
    public Dictionary<string, string> CurrencyToMarket { get; set; } = new();
}

/// <summary>판매(발주/주문) 파일 하나의 트랙B 컬럼 매핑. 마켓마다 헤더 언어/문구가 달라 마켓별로 분리한다.</summary>
public class SalesMarketMapping
{
    public string MarketCode { get; set; } = "";
    public int HeaderRow { get; set; } = 1;
    public string DateColumn { get; set; } = "";
    public string AmountColumn { get; set; } = "";

    /// <summary>선택. 지정하면 판매액 컬럼 값에 이 수량을 곱한다(판매액 컬럼이 단가일 때). 비우면 판매액을 그대로 쓴다.</summary>
    public string QuantityColumn { get; set; } = "";

    /// <summary>선택. 지정하면 주문 단위 모드: 판매액·차감 컬럼을 주문번호당 한 번만 센다(주문 전체 금액이 줄마다 반복되는 파일용).
    /// 이 모드에서는 QuantityColumn을 쓰지 않는다.</summary>
    public string OrderIdColumn { get; set; } = "";

    /// <summary>주문 단위 모드에서 판매액에서 뺄 컬럼들(예: 구매자 부담 배송비, 쿠폰). "헤더*0.01"처럼 배율 지정 가능.</summary>
    public List<string> SubtractColumns { get; set; } = new();

    /// <summary>파일명(확장자 제외)에 이 문자열 중 하나라도 포함되면 이 마켓 파일로 자동 인식.</summary>
    public List<string> FileNamePatterns { get; set; } = new();

    /// <summary>선택. 관세청 수출신고 내역과 주문을 맞춰보는 "수출신고 대사"용 컬럼. 없으면 이 마켓은 대사에서 빠진다.
    /// 주문 단위 모드(OrderIdColumn)가 전제다.</summary>
    public SalesReconMapping? Recon { get; set; }
}

/// <summary>
/// 수출신고 대사용 컬럼 매핑. 쇼피 판매분의 수출신고 금액은 소비자 실결제액이 아니라
/// "(할인가×수량 − 판매자 부담 쿠폰) × 마켓별 고정비율"로 잡힌다(2026-10-12 3분기 실데이터로 확인,
/// 쇼피 부담 쿠폰·코인은 빼지 않음). 신고일자 = 쇼피 출고처리일(한국시간)이라 이 둘로 주문과 신고를 짝짓는다.
/// </summary>
public class SalesReconMapping
{
    /// <summary>출고처리 시각 컬럼(쇼피 Ship Time 등). 신고일자와 짝짓는 기준.</summary>
    public string ShipDateColumn { get; set; } = "";

    /// <summary>출고 시각을 한국시간으로 바꿀 때 더할 시간(마켓 현지시간 기준 파일). 예: 싱가포르 1, 브라질 12.</summary>
    public double ShipDateOffsetHours { get; set; }

    /// <summary>판매자 할인 적용 후 단가(쇼피 Deal Price 등).</summary>
    public string UnitPriceColumn { get; set; } = "";
    public string QuantityColumn { get; set; } = "";

    /// <summary>판매자 부담 쿠폰(주문 단위, 줄마다 반복). 비우면 0.</summary>
    public string SellerVoucherColumn { get; set; } = "";

    /// <summary>선택. 대사 상세에 표시할 상품 SKU 컬럼.</summary>
    public string SkuColumn { get; set; } = "";

    /// <summary>신고액 ÷ 판매자기준가 비율의 기본값. 대사 때 데이터에서 다시 추정하고, 표본이 부족할 때만 이 값을 쓴다(0=없음).</summary>
    public decimal DeclaredRate { get; set; }
}

public class RemittanceTrackConfig
{
    public int HeaderRow { get; set; } = 1;
    public string DateColumn { get; set; } = "Date";
    public string AmountColumn { get; set; } = "Amount";
    public string DescriptionColumn { get; set; } = "Description";

    /// <summary>선택. 송금 통화 컬럼(페이오니어 Currency). 비거나 파일에 없으면 USD로 본다(아마존 일본은 JPY로 들어옴).</summary>
    public string CurrencyColumn { get; set; } = "Currency";

    /// <summary>이 문구가 포함된 행은 내부이체(예: Withdrawal to Woori)이므로 집계에서 제외한다.</summary>
    public List<string> ExcludeContains { get; set; } = new();

    public List<RemittanceRule> Rules { get; set; } = new();
}

/// <summary>Description에 Contains의 모든 문구가 포함되면(대소문자 무시) MarketCode로 분류한다. 위에서부터 순서대로 평가해 첫 매치를 사용한다.</summary>
public class RemittanceRule
{
    public List<string> Contains { get; set; } = new();
    public string MarketCode { get; set; } = "";
}

/// <summary>PayPal "Download.CSV" 거래내역. 2분기 보고서 기준: 자사몰 매출액 = Gross 합, 정산액 = Net 합(환불 행 포함).</summary>
public class PaypalTrackConfig
{
    public string MarketCode { get; set; } = "OWN_US";
    public string DateColumn { get; set; } = "Date";
    public string CurrencyColumn { get; set; } = "Currency";
    public string GrossColumn { get; set; } = "Gross";
    public string NetColumn { get; set; } = "Net";
    public string StatusColumn { get; set; } = "Status";

    /// <summary>이 상태인 행만 집계(비우면 전부).</summary>
    public string IncludeStatus { get; set; } = "Completed";
}

public class WixOrderTrackConfig
{
    public string OrderNumberColumn { get; set; } = "Order number";
    public string DateColumn { get; set; } = "Date created";
    public string TotalColumn { get; set; } = "Total";
    public string CurrencyColumn { get; set; } = "Currency";
    public string PaymentStatusColumn { get; set; } = "Payment status";

    /// <summary>이 결제상태인 주문만 PayPal과 대조(비우면 전부).</summary>
    public string IncludePaymentStatus { get; set; } = "Paid";
}

/// <summary>
/// 아마존 거래내역 CSV. 월 매출액 = 요약 PDF의 Income 합계("Net sales, credits, and refunds")와 같게 계산한다:
/// IncomeTypes 행의 IncomeColumns 합 + (ReimbursementType 행 중 설명에 ReimbursementContains가 있고 양수인 ReimbursementAmountColumn).
/// 2026-06 미국 실데이터로 PDF 28,227.85와 일치 확인. 통화는 파일 머리말 "All amounts in USD"에서 읽는다.
/// </summary>
public class AmazonTransactionTrackConfig
{
    public string DateTimeColumn { get; set; } = "date/time";
    public string TypeColumn { get; set; } = "type";
    public string DescriptionColumn { get; set; } = "description";
    public List<string> IncomeTypes { get; set; } = new() { "Order", "Refund", "Liquidations" };
    public List<string> IncomeColumns { get; set; } = new() { "product sales", "shipping credits", "gift wrap credits", "promotional rebates" };
    public string ReimbursementType { get; set; } = "Adjustment";
    public string ReimbursementContains { get; set; } = "Reimbursement";
    public string ReimbursementAmountColumn { get; set; } = "other";

    /// <summary>머리말의 통화 → 마켓코드.</summary>
    public Dictionary<string, string> CurrencyToMarket { get; set; } = new() { ["USD"] = "US", ["JPY"] = "JP" };
}

/// <summary>
/// 집계 전 원자료 한 줄. Track: "A"=신고, "B"=판매, "C"=송금.
/// Indicator: 지표명(신고액/매출액/실익액). Currency: 해당 지표의 실제 통화.
/// 두 필드는 기존 수동입력 호환을 위해 기본값 ""로 두고, Aggregate 시 Track에서 역산한다.
/// </summary>
public record ExportSummaryEntry(string Track, string MarketCode, string YearMonth, decimal Amount, string Indicator = "", string Currency = "");

/// <summary>트랙A USD 신고 행의 상세 데이터. Excel "USD상세" 시트에 원본 그대로 출력.</summary>
public record ExportSummaryRawEntry(string DateText, string Currency, decimal Amount);

/// <summary>ExportSummaryForm의 소스 목록 그리드 한 줄(불러온 파일 하나의 요약).</summary>
public record ExportSummaryLoadedSource(
    string TrackName, string MarketName, string FileName,
    int RowCount, int UnmatchedRowCount,
    List<ExportSummaryEntry> Entries,
    List<ExportSummaryRawEntry>? UsdRawEntries = null);

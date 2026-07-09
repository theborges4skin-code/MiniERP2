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

    /// <summary>파일명(확장자 제외)에 이 문자열 중 하나라도 포함되면 이 마켓 파일로 자동 인식.</summary>
    public List<string> FileNamePatterns { get; set; } = new();
}

public class RemittanceTrackConfig
{
    public int HeaderRow { get; set; } = 1;
    public string DateColumn { get; set; } = "Date";
    public string AmountColumn { get; set; } = "Amount";
    public string DescriptionColumn { get; set; } = "Description";

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

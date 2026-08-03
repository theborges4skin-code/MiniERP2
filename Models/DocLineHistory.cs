namespace MiniERP2.Models;

/// <summary>
/// 문서유형(Quote/Statement/PriceAdjustment)에 상관없이 한 행 = 한 품목줄로 정규화한 이력
/// 레코드입니다(tidy-long). 견적·거래명세표·가격조정 3종을 채널×CSKU×기간으로 통합 조회하는
/// 기능을 <see cref="Database.DocLineHistoryRepository"/>의 <c>DocLineHistoryTable</c>과 완전히
/// 독립적으로 단독 개발·검증하기 위한 임시 모델입니다(문서이력_조회축_갭재검토_A.md rev.2 참고).
/// </summary>
public class DocLineHistory
{
    public int Id { get; set; }

    /// <summary>같은 문서(발행 1건)에 속한 줄을 묶는 키. 비어있으면 그 줄 혼자 문서 1건으로 취급.</summary>
    public string DocGroupKey { get; set; } = "";

    public string DocNo { get; set; } = "";

    public DocLineHistoryType DocType { get; set; }

    public string ChannelCode { get; set; } = "";
    public string ChannelName { get; set; } = "";

    /// <summary>비어있으면 CSKU에 연결되지 않은 자유품목("미매핑" 버킷으로 조회됨).</summary>
    public string CskuCode { get; set; } = "";
    public string ItemNameSnap { get; set; } = "";

    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal SupplyAmount { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }

    /// <summary>귀속일 — 견적=발행일 / 명세=출고(확정)일 / 가격조정=적용일.</summary>
    public DateTime IssueDate { get; set; }

    /// <summary>원본 문서를 찾아가기 위한 느슨한 참조(파일 경로 등). 필수 아님.</summary>
    public string SourceRef { get; set; } = "";

    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    /// <summary>"yyyy-MM" — IssueDate에서 파생, 조회 편의를 위해 저장해둔다.</summary>
    public string YearMonth => IssueDate == default ? "" : IssueDate.ToString("yyyy-MM");

    /// <summary>"yyyy-Q1"~"yyyy-Q4" (역년 분기 기준) — IssueDate에서 파생.</summary>
    public string Quarter => IssueDate == default ? "" : $"{IssueDate:yyyy}-Q{(IssueDate.Month - 1) / 3 + 1}";
}

public enum DocLineHistoryType
{
    Quote,
    Statement,
    PriceAdjustment,
}

/// <summary>
/// CSKU 1건 = 1행으로 묶은 요약(문서관리 메인창 레벨1 그리드). "같은 CSKU가 시간이 지나도 하나로
/// 묶여 소팅되는가"라는 품목축 요구에 직접 답하기 위한 것으로, 최초/최근 단가 차이를 통해 가격
/// 추이를 한눈에 보여준다. <see cref="Database.DocLineHistoryRepository.GetCskuSummary"/> 참고.
/// </summary>
public class DocLineHistoryCskuSummary
{
    public required string ChannelCode { get; init; }
    public required string ChannelName { get; init; }

    /// <summary>비어있으면 "미매핑" 버킷.</summary>
    public required string CskuCode { get; init; }

    /// <summary>가장 최근 이력의 품목명 스냅샷.</summary>
    public required string LatestItemNameSnap { get; init; }

    public required int DocCount { get; init; }
    public required decimal FirstUnitPrice { get; init; }
    public required decimal LastUnitPrice { get; init; }
    public required DateTime FirstIssueDate { get; init; }
    public required DateTime LastIssueDate { get; init; }

    /// <summary>최초 대비 최근 단가 증감(양수=인상, 음수=인하, 0=변동없음).</summary>
    public decimal PriceChange => LastUnitPrice - FirstUnitPrice;
}

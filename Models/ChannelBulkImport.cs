namespace MiniERP2.Models;

/// <summary>채널 일괄등록(엑셀) 미리보기에서 행 1건의 처리 상태.</summary>
public enum ChannelImportRowStatus
{
    /// <summary>DB에 없는 신규 채널로 등록됨.</summary>
    New,

    /// <summary>기존 채널의 값 중 하나 이상이 바뀌어 갱신됨.</summary>
    Update,

    /// <summary>기존 채널과 값이 완전히 같아 실질 변경이 없음(라이트립 멱등성 판정용).</summary>
    Unchanged,

    /// <summary>검증 실패로 커밋 대상에서 제외됨.</summary>
    Error,

    /// <summary>행 자체가 공란이라 무시됨.</summary>
    Ignored,
}

/// <summary>`채널` 시트 1행. §4.1.</summary>
public class ChannelImportChannelRow
{
    public int RowNumber { get; set; }
    public string ChannelCodeInput { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string ChannelTypeLabel { get; set; } = "";
    public string? GroupName { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsPurchase { get; set; }
    public bool IsSales { get; set; } = true;
    public decimal ExchangeRate { get; set; } = 1m;
    public bool IsCumulativeOrderFile { get; set; }
    public int CumulativeOrderWindowDays { get; set; } = 5;
    public string AutoOrderHints { get; set; } = "";
    public string? CopySourceChannelName { get; set; }

    /// <summary>
    /// 셀 원문(공란이면 null)을 헤더명별로 보관한다. 설정복사원본 지정 시 "공란인 열은 복사본
    /// 값 유지"(§4.1.1) 판단에 필요 — 파싱 시 이미 기본값이 채워진 typed 속성만으로는 사용자가
    /// 셀을 비운 것인지, 기본값과 우연히 같은 값을 입력한 것인지 구분할 수 없기 때문이다.
    /// </summary>
    public Dictionary<string, string?> RawCells { get; } = new();

    public ChannelImportRowStatus Status { get; set; } = ChannelImportRowStatus.Ignored;
    public string? ResolvedChannelCode { get; set; }
    public ChannelType ResolvedChannelType { get; set; }

    /// <summary>
    /// 커밋 시 실제로 저장될 최종 객체. 미리보기에서 보여준 것과 커밋 결과가 어긋나지 않도록,
    /// 진단(상태 판정)과 커밋이 이 값을 그대로 재사용한다(재계산하지 않음).
    /// </summary>
    public SalesChannel? FinalChannel { get; set; }
    public ChannelConfig? FinalConfig { get; set; }
    public DocParty? FinalParty { get; set; }

    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>`발주서매핑`/`정산서매핑` 시트 1행. §4.1 시트2·3.</summary>
public class ChannelImportMappingRow
{
    public int RowNumber { get; set; }
    public bool IsSettlement { get; set; }
    public string ChannelName { get; set; } = "";
    public string StdFieldLabel { get; set; } = "";
    public string? SheetName { get; set; }
    public int HeaderRow { get; set; } = 1;
    public string? Column { get; set; }
    public string? FixedValue { get; set; }

    public StdField? ResolvedField { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>`거래처정보` 시트 1행. §4.1 시트4.</summary>
public class ChannelImportPartyRow
{
    public int RowNumber { get; set; }
    public string ChannelName { get; set; } = "";
    public string RegNo { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string CeoName { get; set; } = "";
    public string Address { get; set; } = "";
    public string BizType { get; set; } = "";
    public string BizItem { get; set; } = "";
    public string Tel { get; set; } = "";
    public string Email { get; set; } = "";

    public List<string> Errors { get; } = new();
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>엑셀 파싱 + 검증 결과 전체. §4.3/§4.4.</summary>
public class ChannelBulkImportResult
{
    public int SchemaVersion { get; set; }
    public List<string> FileErrors { get; } = new();
    public List<ChannelImportChannelRow> ChannelRows { get; } = new();
    public List<ChannelImportMappingRow> MappingRows { get; } = new();
    public List<ChannelImportPartyRow> PartyRows { get; } = new();

    /// <summary>하나라도 오류가 있으면 전체 커밋을 차단한다(§4.3 "오류 행 처리 정책: 전체 차단").</summary>
    public bool HasBlockingErrors =>
        FileErrors.Count > 0 ||
        ChannelRows.Any(r => r.HasErrors) ||
        MappingRows.Any(r => r.HasErrors) ||
        PartyRows.Any(r => r.HasErrors);

    public int NewCount => ChannelRows.Count(r => r.Status == ChannelImportRowStatus.New);
    public int UpdateCount => ChannelRows.Count(r => r.Status == ChannelImportRowStatus.Update);
    public int UnchangedCount => ChannelRows.Count(r => r.Status == ChannelImportRowStatus.Unchanged);
    public int ErrorCount => ChannelRows.Count(r => r.Status == ChannelImportRowStatus.Error);
}

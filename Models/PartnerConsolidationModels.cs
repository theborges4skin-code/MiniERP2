namespace MiniERP2.Models;

/// <summary>온라인 거래처 취합(OnlinePartnerConsolidation_Spec.md §6.1) — 행 1건의 분류 결과.</summary>
public enum PartnerConsolidationRowKind
{
    /// <summary>정상 매핑 + CSKU 정규화 성공.</summary>
    Mapped,

    /// <summary>매핑SKU 공란이거나 SettlementRowStatus.IsUnresolved에 해당하는 상태.</summary>
    Unmapped,

    /// <summary>예외처리 규칙으로 이미 제외된 행(SettlementRowStatus.IsExcludedByExceptionRule).</summary>
    Excluded,

    /// <summary>매핑은 됐으나 CSKU/마스터SKU 재대조 모두 실패(0개 또는 2개 이상 일치) — "CSKU 미확정".</summary>
    CskuUnresolved,
}

/// <summary>
/// 이익분석 내보내기 파일 1개를 취합 화면이 읽은 결과의 파일 단위 정보(§6.5 상단 파일 목록).
/// </summary>
public class PartnerConsolidationFile
{
    public required string FilePath { get; set; }

    public string FileName => Path.GetFileName(FilePath);

    /// <summary>_META의 company_name. 공란이면 화면에서 "(미지정)" 그룹으로 분류한다(D1/W1).</summary>
    public string CompanyName { get; set; } = "";

    public string ChannelCode { get; set; } = "";
    public string ChannelName { get; set; } = "";

    /// <summary>_META 시트 자체가 없던 파일인지(W4) — 목록에서 채널 수동 지정을 허용해야 한다.</summary>
    public bool HasMetaSheet { get; set; }

    /// <summary>_META는 있으나 company_name 키가 없던 v1 스키마 파일인지.</summary>
    public bool IsSchemaV1 { get; set; }

    public int RowCount { get; set; }

    /// <summary>'분석결과상세' 시트를 찾지 못하는 등 로드 자체에 실패한 사유. null이면 정상.</summary>
    public string? ErrorMessage { get; set; }

    public List<PartnerConsolidationRow> Rows { get; set; } = [];

    public bool LoadFailed => ErrorMessage != null;

    // ── 화면 표시용(취합 화면 §6.5 상단 파일 목록 그리드 바인딩) ──────────────

    public string CompanyNameDisplay => string.IsNullOrWhiteSpace(CompanyName) ? "(미지정)" : CompanyName;

    public string ChannelNameDisplay => string.IsNullOrWhiteSpace(ChannelName)
        ? (string.IsNullOrWhiteSpace(ChannelCode) ? "-" : ChannelCode)
        : ChannelName;

    public string StatusDisplay
    {
        get
        {
            if (LoadFailed) return $"오류: {ErrorMessage}";
            if (!HasMetaSheet) return "_META 없음 — 채널 수동 지정 필요(W4)";
            if (IsSchemaV1) return "구버전(v1) 파일 — 상호명 정보 없음";
            if (string.IsNullOrWhiteSpace(CompanyName)) return "상호명 공란";
            return "정상";
        }
    }
}

/// <summary>'분석결과상세' 시트 1행을 CSKU 축으로 정규화한 결과(§6.1 ③④).</summary>
public class PartnerConsolidationRow
{
    /// <summary>파일의 _META company_name(공란 가능) — 거래처 그룹 키.</summary>
    public required string CompanyName { get; set; }

    /// <summary>이 행 자체의 '채널' 컬럼 값(ChannelCode) — F2: 파일의 _META보다 이쪽이 항상 채워져 있다.</summary>
    public required string ChannelCode { get; set; }

    public string ChannelName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string OptionName { get; set; } = "";
    public int Quantity { get; set; }

    /// <summary>원본 '매핑SKU' 텍스트(CSKU 코드 또는 마스터SKU — F4).</summary>
    public string RawMappedSku { get; set; } = "";

    /// <summary>원본 '상태' 텍스트.</summary>
    public string RawStatus { get; set; } = "";

    public PartnerConsolidationRowKind Kind { get; set; }

    /// <summary>Kind == Mapped일 때만 채워진다.</summary>
    public string? ResolvedCskuCode { get; set; }

    /// <summary>Kind == Mapped일 때만 채워진다(제조원가 조회 등에 쓰는 마스터SKU).</summary>
    public string? ResolvedMsku { get; set; }

    public string SourceFileName { get; set; } = "";
}

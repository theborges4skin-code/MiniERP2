namespace MiniERP2.Models;

// ─── 마감 실행 단위 ───────────────────────────────────────────────────────────

public class ClosingRun
{
    public long Id { get; set; }
    public string FolderPath { get; set; } = "";
    public string Period { get; set; } = "";        // YYYY-MM
    public string Status { get; set; } = "draft";   // draft | confirmed
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    public List<ClosingStagedFile> Files { get; set; } = [];
}

// ─── 스캔된 파일 1건 ─────────────────────────────────────────────────────────

public class ClosingStagedFile
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public string ChannelCode { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string SourceType { get; set; } = "settlement"; // settlement | ad
    public string OriginalPath { get; set; } = "";
    public string FileCreatedAt { get; set; } = "";
    public string Status { get; set; } = "pending";        // pending | processed | error | skipped
    public int RowCount { get; set; }
    public int UnmappedCount { get; set; }
    public string? ErrorMessage { get; set; }

    // DB에 저장하지 않는 런타임 전용 필드
    public string FileName => Path.GetFileName(OriginalPath);
    public bool IsChannelAssigned => !string.IsNullOrEmpty(ChannelCode);
}

// ─── 전채널 통합 미매핑 항목 ─────────────────────────────────────────────────

public class ClosingUnmappedItem
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public string ChannelCode { get; set; } = "";
    // 가격 매핑 없는 채널: "상품명|옵션명". 가격 매핑 있는 채널: "상품명|옵션명|수량|매출액"
    // (매핑시스템 통합개편 기획서 §5 — 같은 상품명+옵션명이라도 가격이 다른 옵션이 한 미매핑 행으로
    // 뭉치지 않도록 4필드로 집계한다).
    public string SourceKey { get; set; } = "";
    public int OccurrenceCount { get; set; } = 1;
    public decimal SampleAmount { get; set; }
    public string? MappedSku { get; set; }          // 사용자가 매핑한 SKU (null = 미매핑)

    /// <summary>가격 매핑 채널에서만 채워짐(SourceKey의 4필드 집계와 짝을 이루는 대표 수량 샘플).</summary>
    public int? Quantity { get; set; }
    /// <summary>가격 매핑 채널에서만 채워짐(SourceKey의 4필드 집계와 짝을 이루는 대표 매출액 샘플).</summary>
    public decimal? SampleRevenue { get; set; }

    // 런타임 전용
    public string ChannelName { get; set; } = "";
    public string ProductName => SourceKey.Split('|').ElementAtOrDefault(0) ?? SourceKey;
    public string OptionName => SourceKey.Split('|').ElementAtOrDefault(1) ?? "";
}

// ─── _META 시트에서 읽은 채널 메타데이터 ────────────────────────────────────

public class FileMeta
{
    public string ChannelCode { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string SourceType { get; set; } = "settlement";
    public string FileCreatedAt { get; set; } = "";
    public string Period { get; set; } = "";
    public int SchemaVersion { get; set; } = 2;

    /// <summary>거래처 상호명(DocPartyTable.CompanyName). schema_version 2부터 기록. 없으면 공란.</summary>
    public string CompanyName { get; set; } = "";
}

// ─── 파일 스캔 결과 1건 ──────────────────────────────────────────────────────

public class ScannedFile
{
    public string FilePath { get; set; } = "";
    public FileMeta? Meta { get; set; }             // null = _META 없음
    public string? DetectedChannelCode { get; set; } // null = 자동탐지 실패

    public string FileName => Path.GetFileName(FilePath);
    public bool HasMeta => Meta != null;
    public bool IsDetected => !string.IsNullOrEmpty(DetectedChannelCode);
}

// ─── 채널 분析 결과 ───────────────────────────────────────────────────────────

public class ChannelAnalysisResult
{
    public string ChannelCode { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public List<MiniERP2.Models.SettlementData> Rows { get; set; } = [];
    public List<ClosingUnmappedItem> Unmapped { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

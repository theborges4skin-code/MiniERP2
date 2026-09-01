namespace MiniERP2.Models;

/// <summary>
/// CSKU별통계_개발기획서.md §1.3의 8종 상태 문자열을 3분류한 결과.
/// </summary>
public enum CskuStatRowClass
{
    /// <summary>매핑(1:1) / 매핑(조건) / 매핑(임시) / 매핑(예외) — 집계 대상.</summary>
    Normal,

    /// <summary>제외(배송비 등) — 예외규칙으로 의도적으로 제외된 행.</summary>
    Excluded,

    /// <summary>매핑 키 없음 / 매핑 실패 / 원가 정보 없음 / 수치 파싱 실패 — 확인 필요.</summary>
    Unmapped,
}

/// <summary>
/// "분석결과상세" 시트 한 행을 그대로 옮긴 값(§1.1) + 분류 결과. 집계 전 중간 표현.
/// </summary>
public class CskuStatSourceRow
{
    public string FileName { get; set; } = string.Empty;
    public CskuFileKind FileKind { get; set; }

    public string ChannelCode { get; set; } = string.Empty;
    public string ProductGroup { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string OptionName { get; set; } = string.Empty;

    /// <summary>매핑SKU 열 — 집계키(CSKU). 제외(배송비 등) 행은 공백일 수 있다.</summary>
    public string CskuCode { get; set; } = string.Empty;

    public int Qty { get; set; }
    public decimal Revenue { get; set; }
    public decimal Settlement { get; set; }
    public decimal Shipping { get; set; }
    public decimal Fee { get; set; }
    public decimal Profit { get; set; }

    public string Status { get; set; } = string.Empty;

    public CskuStatRowClass RowClass { get; set; }
}

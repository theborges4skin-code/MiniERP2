namespace MiniERP2.Models;

/// <summary>CSKU별 통계 로드 배치 1건(스냅샷 누적, §5).</summary>
public class CskuStatBatch
{
    public long Id { get; set; }
    public string Period { get; set; } = string.Empty;
    public string Memo { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public int FileCount { get; set; }
    public int RowCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

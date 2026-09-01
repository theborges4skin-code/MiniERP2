namespace MiniERP2.Models;

/// <summary>배치에 포함된 파일 이력 — 중복판정(§7) 근거.</summary>
public class CskuStatFile
{
    public long Id { get; set; }
    public long BatchId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public CskuFileKind FileKind { get; set; }
    public int RowCount { get; set; }
    public int SumQty { get; set; }
    public decimal SumRevenue { get; set; }
    public decimal SumProfit { get; set; }
    public DateTime LoadedAt { get; set; }
}

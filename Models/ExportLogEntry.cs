namespace MiniERP2.Models;

/// <summary>
/// 데이터 관리창에서 엑셀로 내보낸 기록 1건입니다("엑셀 내보내기할 때는 로그기록 기억할 것" 요청사항).
/// </summary>
public class ExportLogEntry
{
    public long Id { get; set; }
    public DateTime ExportedAt { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public string Headers { get; set; } = string.Empty;
}

namespace MiniERP2.Models;

public class DocHistoryRecord
{
    public int Id { get; set; }
    public string DocType { get; set; } = "";
    public DateTime IssueDate { get; set; }
    public string BuyerName { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string FilePath { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 발행 시점에 저장한 엑셀 파일의 원본 바이트(내장DB 백업). FilePath의 원본 파일이 사용자에
    /// 의해 이동/삭제되어도 이 값으로 복원해 열 수 있다. 목록 조회(DocHistoryRepository.Query)는
    /// 큰 BLOB을 매번 읽지 않도록 이 값을 채우지 않는다 — 실제로 열 때만 GetFileBytes로 조회한다.
    /// </summary>
    public byte[]? FileBytes { get; set; }
}

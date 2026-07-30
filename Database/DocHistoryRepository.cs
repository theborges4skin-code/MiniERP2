using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

public class DocHistoryRepository
{
    public long Add(DocHistoryRecord r)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO DocHistoryTable (DocType, IssueDate, BuyerName, TotalAmount, FilePath, CreatedAt, FileBytes, ChannelCode, Period)
            VALUES ($docType, $issueDate, $buyerName, $totalAmount, $filePath, $createdAt, $fileBytes, $channelCode, $period);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$docType",     r.DocType);
        cmd.Parameters.AddWithValue("$issueDate",   r.IssueDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$buyerName",   r.BuyerName);
        cmd.Parameters.AddWithValue("$totalAmount", (double)r.TotalAmount);
        cmd.Parameters.AddWithValue("$filePath",    r.FilePath);
        cmd.Parameters.AddWithValue("$createdAt",   r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$fileBytes",   (object?)r.FileBytes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$channelCode", r.ChannelCode);
        cmd.Parameters.AddWithValue("$period",      r.Period);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// 이력 저장 시 함께 백업해둔 원본 엑셀 바이트를 조회한다(없으면 null — FileBytes 도입 이전에
    /// 저장된 옛 이력이거나 백업 자체가 실패했던 경우). "파일 열기"에서 FilePath의 실제 파일을
    /// 찾을 수 없을 때만 이 메서드로 복원을 시도한다.
    /// </summary>
    public byte[]? GetFileBytes(int id)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT FileBytes FROM DocHistoryTable WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        var result = cmd.ExecuteScalar();
        return result is byte[] bytes ? bytes : null;
    }

    public List<DocHistoryRecord> Query(DateTime from, DateTime to, string? docType = null)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();

        string docTypeFilter = string.IsNullOrEmpty(docType)
            ? ""
            : " AND DocType = $docType";

        cmd.CommandText = $"""
            SELECT Id, DocType, IssueDate, BuyerName, TotalAmount, FilePath, CreatedAt, ChannelCode, Period
            FROM DocHistoryTable
            WHERE IssueDate >= $from AND IssueDate <= $to
            {docTypeFilter}
            ORDER BY IssueDate DESC, CreatedAt DESC
            """;
        cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to",   to.ToString("yyyy-MM-dd"));
        if (!string.IsNullOrEmpty(docType))
            cmd.Parameters.AddWithValue("$docType", docType);

        var list = new List<DocHistoryRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new DocHistoryRecord
            {
                Id          = reader.GetInt32(0),
                DocType     = reader.GetString(1),
                IssueDate   = DateTime.Parse(reader.GetString(2)),
                BuyerName   = reader.GetString(3),
                TotalAmount = (decimal)reader.GetDouble(4),
                FilePath    = reader.GetString(5),
                CreatedAt   = DateTime.Parse(reader.GetString(6)),
                ChannelCode = reader.IsDBNull(7) ? "" : reader.GetString(7),
                Period      = reader.IsDBNull(8) ? "" : reader.GetString(8),
            });
        }
        return list;
    }

    public void Delete(int id)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM DocHistoryTable WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}

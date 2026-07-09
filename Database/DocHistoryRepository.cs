using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

public class DocHistoryRepository
{
    public void Add(DocHistoryRecord r)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO DocHistoryTable (DocType, IssueDate, BuyerName, TotalAmount, FilePath, CreatedAt)
            VALUES ($docType, $issueDate, $buyerName, $totalAmount, $filePath, $createdAt)
            """;
        cmd.Parameters.AddWithValue("$docType",     r.DocType);
        cmd.Parameters.AddWithValue("$issueDate",   r.IssueDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$buyerName",   r.BuyerName);
        cmd.Parameters.AddWithValue("$totalAmount", (double)r.TotalAmount);
        cmd.Parameters.AddWithValue("$filePath",    r.FilePath);
        cmd.Parameters.AddWithValue("$createdAt",   r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    public List<DocHistoryRecord> Query(DateTime from, DateTime to, string? docType = null)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();

        string docTypeFilter = string.IsNullOrEmpty(docType)
            ? ""
            : " AND DocType = $docType";

        cmd.CommandText = $"""
            SELECT Id, DocType, IssueDate, BuyerName, TotalAmount, FilePath, CreatedAt
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

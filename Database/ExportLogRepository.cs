using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 데이터 관리창의 엑셀 내보내기 로그(언제/어떤 테이블/어떤 파일/몇 건/어떤 헤더로 내보냈는지)를 기록합니다.
/// </summary>
public class ExportLogRepository
{
    public void Add(ExportLogEntry entry)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ExportLogTable (ExportedAt, TableName, FilePath, RowCount, Headers)
            VALUES ($exportedAt, $tableName, $filePath, $rowCount, $headers)
            """;
        command.Parameters.AddWithValue("$exportedAt", DateTime.Now);
        command.Parameters.AddWithValue("$tableName", entry.TableName);
        command.Parameters.AddWithValue("$filePath", entry.FilePath);
        command.Parameters.AddWithValue("$rowCount", entry.RowCount);
        command.Parameters.AddWithValue("$headers", entry.Headers);
        command.ExecuteNonQuery();
    }

    /// <summary>최근 내보내기 기록을 최신순으로 가져옵니다.</summary>
    public List<ExportLogEntry> GetRecent(int limit = 200)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ExportedAt, TableName, FilePath, RowCount, Headers
            FROM ExportLogTable
            ORDER BY Id DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<ExportLogEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ExportLogEntry
            {
                Id = reader.GetInt64(0),
                ExportedAt = reader.GetDateTime(1),
                TableName = reader.GetString(2),
                FilePath = reader.GetString(3),
                RowCount = reader.GetInt32(4),
                Headers = reader.GetString(5),
            });
        }
        return results;
    }
}

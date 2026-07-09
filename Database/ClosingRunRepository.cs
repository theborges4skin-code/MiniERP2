using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// ClosingRun / ClosingStagedFile / ClosingUnmapped 세 테이블의 CRUD.
/// </summary>
public class ClosingRunRepository
{
    // ─── Run ──────────────────────────────────────────────────────────────────

    public long CreateRun(string folderPath, string period)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        cmd.CommandText = """
            INSERT INTO ClosingRun (FolderPath, Period, Status, CreatedAt, UpdatedAt)
            VALUES (@folder, @period, 'draft', @now, @now);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@folder", folderPath);
        cmd.Parameters.AddWithValue("@period", period);
        cmd.Parameters.AddWithValue("@now", now);
        return (long)(cmd.ExecuteScalar() ?? throw new InvalidOperationException());
    }

    public ClosingRun? GetRun(long runId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, FolderPath, Period, Status, CreatedAt, UpdatedAt FROM ClosingRun WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", runId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadRun(reader);
    }

    /// <summary>최근 run 목록 (최신순 N개).</summary>
    public List<ClosingRun> GetRecentRuns(int limit = 20)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, FolderPath, Period, Status, CreatedAt, UpdatedAt FROM ClosingRun ORDER BY Id DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        var list = new List<ClosingRun>();
        while (reader.Read()) list.Add(ReadRun(reader));
        return list;
    }

    public void UpdateRunStatus(long runId, string status)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE ClosingRun SET Status=@s, UpdatedAt=@now WHERE Id=@id";
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@id", runId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteRun(long runId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var tx = conn.BeginTransaction();

        Execute(conn, tx, "DELETE FROM ClosingUnmapped WHERE RunId=@id", ("@id", runId));
        Execute(conn, tx, "DELETE FROM ClosingStagedFile WHERE RunId=@id", ("@id", runId));
        Execute(conn, tx, "DELETE FROM ClosingRun WHERE Id=@id", ("@id", runId));

        tx.Commit();
    }

    // ─── StagedFile ───────────────────────────────────────────────────────────

    public long UpsertStagedFile(ClosingStagedFile f)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ClosingStagedFile
                (RunId, ChannelCode, ChannelName, SourceType, OriginalPath, FileCreatedAt, Status, RowCount, UnmappedCount, ErrorMessage)
            VALUES (@runId, @code, @name, @srcType, @path, @fca, @status, @rows, @unmapped, @err)
            ON CONFLICT DO NOTHING;
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@runId", f.RunId);
        cmd.Parameters.AddWithValue("@code", f.ChannelCode);
        cmd.Parameters.AddWithValue("@name", f.ChannelName);
        cmd.Parameters.AddWithValue("@srcType", f.SourceType);
        cmd.Parameters.AddWithValue("@path", f.OriginalPath);
        cmd.Parameters.AddWithValue("@fca", f.FileCreatedAt);
        cmd.Parameters.AddWithValue("@status", f.Status);
        cmd.Parameters.AddWithValue("@rows", f.RowCount);
        cmd.Parameters.AddWithValue("@unmapped", f.UnmappedCount);
        cmd.Parameters.AddWithValue("@err", (object?)f.ErrorMessage ?? DBNull.Value);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void UpdateStagedFile(ClosingStagedFile f)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE ClosingStagedFile
            SET ChannelCode=@code, ChannelName=@name, Status=@status,
                RowCount=@rows, UnmappedCount=@unmapped, ErrorMessage=@err
            WHERE Id=@id
            """;
        cmd.Parameters.AddWithValue("@code", f.ChannelCode);
        cmd.Parameters.AddWithValue("@name", f.ChannelName);
        cmd.Parameters.AddWithValue("@status", f.Status);
        cmd.Parameters.AddWithValue("@rows", f.RowCount);
        cmd.Parameters.AddWithValue("@unmapped", f.UnmappedCount);
        cmd.Parameters.AddWithValue("@err", (object?)f.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", f.Id);
        cmd.ExecuteNonQuery();
    }

    public List<ClosingStagedFile> GetStagedFiles(long runId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ClosingStagedFile WHERE RunId=@id ORDER BY Id";
        cmd.Parameters.AddWithValue("@id", runId);
        using var reader = cmd.ExecuteReader();
        var list = new List<ClosingStagedFile>();
        while (reader.Read()) list.Add(ReadStagedFile(reader));
        return list;
    }

    // ─── Unmapped ─────────────────────────────────────────────────────────────

    /// <summary>미매핑 목록 저장 (run 기준 replace).</summary>
    public void ReplaceUnmapped(long runId, string channelCode, IEnumerable<ClosingUnmappedItem> items)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var tx = conn.BeginTransaction();

        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM ClosingUnmapped WHERE RunId=@rid AND ChannelCode=@cc";
        del.Parameters.AddWithValue("@rid", runId);
        del.Parameters.AddWithValue("@cc", channelCode);
        del.ExecuteNonQuery();

        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = """
            INSERT INTO ClosingUnmapped (RunId, ChannelCode, SourceKey, OccurrenceCount, SampleAmount)
            VALUES (@rid, @cc, @key, @cnt, @amt)
            """;
        ins.Parameters.Add("@rid", SqliteType.Integer);
        ins.Parameters.Add("@cc", SqliteType.Text);
        ins.Parameters.Add("@key", SqliteType.Text);
        ins.Parameters.Add("@cnt", SqliteType.Integer);
        ins.Parameters.Add("@amt", SqliteType.Real);

        foreach (var item in items)
        {
            ins.Parameters["@rid"].Value = runId;
            ins.Parameters["@cc"].Value = channelCode;
            ins.Parameters["@key"].Value = item.SourceKey;
            ins.Parameters["@cnt"].Value = item.OccurrenceCount;
            ins.Parameters["@amt"].Value = (double)item.SampleAmount;
            ins.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public List<ClosingUnmappedItem> GetUnmapped(long runId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.Id, u.RunId, u.ChannelCode, u.SourceKey, u.OccurrenceCount, u.SampleAmount, u.MappedSku,
                   c.ChannelName
            FROM ClosingUnmapped u
            LEFT JOIN SalesChannelTable c ON c.ChannelCode = u.ChannelCode
            WHERE u.RunId=@id AND u.MappedSku IS NULL
            ORDER BY u.OccurrenceCount DESC, u.ChannelCode
            """;
        cmd.Parameters.AddWithValue("@id", runId);
        using var reader = cmd.ExecuteReader();
        var list = new List<ClosingUnmappedItem>();
        while (reader.Read())
        {
            list.Add(new ClosingUnmappedItem
            {
                Id = reader.GetInt64(0),
                RunId = reader.GetInt64(1),
                ChannelCode = reader.GetString(2),
                SourceKey = reader.GetString(3),
                OccurrenceCount = reader.GetInt32(4),
                SampleAmount = (decimal)reader.GetDouble(5),
                MappedSku = reader.IsDBNull(6) ? null : reader.GetString(6),
                ChannelName = reader.IsDBNull(7) ? reader.GetString(2) : reader.GetString(7),
            });
        }
        return list;
    }

    public int GetUnmappedCount(long runId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ClosingUnmapped WHERE RunId=@id AND MappedSku IS NULL";
        cmd.Parameters.AddWithValue("@id", runId);
        return (int)(long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void SaveUnmappedMapping(long itemId, string sku)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE ClosingUnmapped SET MappedSku=@sku WHERE Id=@id";
        cmd.Parameters.AddWithValue("@sku", sku);
        cmd.Parameters.AddWithValue("@id", itemId);
        cmd.ExecuteNonQuery();
    }

    // ─── 헬퍼 ────────────────────────────────────────────────────────────────

    private static ClosingRun ReadRun(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        FolderPath = r.GetString(1),
        Period = r.GetString(2),
        Status = r.GetString(3),
        CreatedAt = r.GetString(4),
        UpdatedAt = r.GetString(5),
    };

    private static ClosingStagedFile ReadStagedFile(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        RunId = r.GetInt64(1),
        ChannelCode = r.GetString(2),
        ChannelName = r.GetString(3),
        SourceType = r.GetString(4),
        OriginalPath = r.GetString(5),
        FileCreatedAt = r.GetString(6),
        Status = r.GetString(7),
        RowCount = r.GetInt32(8),
        UnmappedCount = r.GetInt32(9),
        ErrorMessage = r.IsDBNull(10) ? null : r.GetString(10),
    };

    private static void Execute(SqliteConnection conn, SqliteTransaction tx, string sql, params (string name, object value)[] parms)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parms) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}

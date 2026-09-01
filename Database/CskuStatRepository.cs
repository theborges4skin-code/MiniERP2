using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// CskuStatBatchTable / CskuStatLineTable / CskuStatFileTable의 CRUD와 중복 파일 판정
/// (CSKU별통계_개발기획서.md §5, §7 — S3, S5).
/// </summary>
public class CskuStatRepository
{
    // ─── Batch 저장/조회 ──────────────────────────────────────────────────────

    /// <summary>배치(헤더+라인+파일이력)를 한 번에 저장한다. 스냅샷 누적이므로 항상 신규 INSERT.</summary>
    public long SaveBatch(CskuStatBatch batch, IReadOnlyList<CskuStatLine> lines, IReadOnlyList<CskuStatFile> files)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var tx = conn.BeginTransaction();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        long batchId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO CskuStatBatchTable (Period, Memo, ExchangeRate, FileCount, RowCount, CreatedAt)
                VALUES (@period, @memo, @rate, @fileCount, @rowCount, @now);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("@period", batch.Period);
            cmd.Parameters.AddWithValue("@memo", batch.Memo);
            cmd.Parameters.AddWithValue("@rate", (double)batch.ExchangeRate);
            cmd.Parameters.AddWithValue("@fileCount", files.Count);
            cmd.Parameters.AddWithValue("@rowCount", lines.Sum(l => l.RowCount));
            cmd.Parameters.AddWithValue("@now", now);
            batchId = (long)(cmd.ExecuteScalar() ?? throw new InvalidOperationException());
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO CskuStatLineTable
                    (BatchId, FileKind, ChannelCode, ChannelName, CskuCode, ProductGroup, ProductName,
                     RowCount, Qty, Revenue, Settlement, Shipping, Fee, Profit)
                VALUES
                    (@batchId, @fileKind, @channelCode, @channelName, @cskuCode, @productGroup, @productName,
                     @rowCount, @qty, @revenue, @settlement, @shipping, @fee, @profit)
                """;
            cmd.Parameters.Add("@batchId", SqliteType.Integer);
            cmd.Parameters.Add("@fileKind", SqliteType.Text);
            cmd.Parameters.Add("@channelCode", SqliteType.Text);
            cmd.Parameters.Add("@channelName", SqliteType.Text);
            cmd.Parameters.Add("@cskuCode", SqliteType.Text);
            cmd.Parameters.Add("@productGroup", SqliteType.Text);
            cmd.Parameters.Add("@productName", SqliteType.Text);
            cmd.Parameters.Add("@rowCount", SqliteType.Integer);
            cmd.Parameters.Add("@qty", SqliteType.Integer);
            cmd.Parameters.Add("@revenue", SqliteType.Real);
            cmd.Parameters.Add("@settlement", SqliteType.Real);
            cmd.Parameters.Add("@shipping", SqliteType.Real);
            cmd.Parameters.Add("@fee", SqliteType.Real);
            cmd.Parameters.Add("@profit", SqliteType.Real);

            foreach (var line in lines)
            {
                cmd.Parameters["@batchId"].Value = batchId;
                cmd.Parameters["@fileKind"].Value = line.FileKind.ToString();
                cmd.Parameters["@channelCode"].Value = line.ChannelCode;
                cmd.Parameters["@channelName"].Value = line.ChannelName;
                cmd.Parameters["@cskuCode"].Value = line.CskuCode;
                cmd.Parameters["@productGroup"].Value = line.ProductGroup;
                cmd.Parameters["@productName"].Value = line.ProductName;
                cmd.Parameters["@rowCount"].Value = line.RowCount;
                cmd.Parameters["@qty"].Value = line.Qty;
                cmd.Parameters["@revenue"].Value = (double)line.Revenue;
                cmd.Parameters["@settlement"].Value = (double)line.Settlement;
                cmd.Parameters["@shipping"].Value = (double)line.Shipping;
                cmd.Parameters["@fee"].Value = (double)line.Fee;
                cmd.Parameters["@profit"].Value = (double)line.Profit;
                cmd.ExecuteNonQuery();
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO CskuStatFileTable (BatchId, FileName, FileKind, RowCount, SumQty, SumRevenue, SumProfit, LoadedAt)
                VALUES (@batchId, @fileName, @fileKind, @rowCount, @sumQty, @sumRevenue, @sumProfit, @now)
                """;
            cmd.Parameters.Add("@batchId", SqliteType.Integer);
            cmd.Parameters.Add("@fileName", SqliteType.Text);
            cmd.Parameters.Add("@fileKind", SqliteType.Text);
            cmd.Parameters.Add("@rowCount", SqliteType.Integer);
            cmd.Parameters.Add("@sumQty", SqliteType.Integer);
            cmd.Parameters.Add("@sumRevenue", SqliteType.Real);
            cmd.Parameters.Add("@sumProfit", SqliteType.Real);
            cmd.Parameters.Add("@now", SqliteType.Text);

            foreach (var file in files)
            {
                cmd.Parameters["@batchId"].Value = batchId;
                cmd.Parameters["@fileName"].Value = file.FileName;
                cmd.Parameters["@fileKind"].Value = file.FileKind.ToString();
                cmd.Parameters["@rowCount"].Value = file.RowCount;
                cmd.Parameters["@sumQty"].Value = file.SumQty;
                cmd.Parameters["@sumRevenue"].Value = (double)file.SumRevenue;
                cmd.Parameters["@sumProfit"].Value = (double)file.SumProfit;
                cmd.Parameters["@now"].Value = now;
                cmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
        return batchId;
    }

    public List<CskuStatBatch> GetBatches()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Period, Memo, ExchangeRate, FileCount, RowCount, CreatedAt FROM CskuStatBatchTable ORDER BY Id DESC";
        using var reader = cmd.ExecuteReader();
        var list = new List<CskuStatBatch>();
        while (reader.Read()) list.Add(ReadBatch(reader));
        return list;
    }

    public CskuStatBatch? GetBatch(long batchId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Period, Memo, ExchangeRate, FileCount, RowCount, CreatedAt FROM CskuStatBatchTable WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", batchId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadBatch(reader) : null;
    }

    public List<CskuStatLine> GetLines(long batchId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, BatchId, FileKind, ChannelCode, ChannelName, CskuCode, ProductGroup, ProductName,
                   RowCount, Qty, Revenue, Settlement, Shipping, Fee, Profit
            FROM CskuStatLineTable WHERE BatchId = @id
            """;
        cmd.Parameters.AddWithValue("@id", batchId);
        using var reader = cmd.ExecuteReader();
        var list = new List<CskuStatLine>();
        while (reader.Read()) list.Add(ReadLine(reader));
        return list;
    }

    public List<CskuStatFile> GetFiles(long batchId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, BatchId, FileName, FileKind, RowCount, SumQty, SumRevenue, SumProfit, LoadedAt
            FROM CskuStatFileTable WHERE BatchId = @id
            """;
        cmd.Parameters.AddWithValue("@id", batchId);
        using var reader = cmd.ExecuteReader();
        var list = new List<CskuStatFile>();
        while (reader.Read()) list.Add(ReadFile(reader));
        return list;
    }

    /// <summary>배치(헤더+라인+파일이력)를 삭제한다.</summary>
    public void DeleteBatch(long batchId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var tx = conn.BeginTransaction();
        Execute(conn, tx, "DELETE FROM CskuStatLineTable WHERE BatchId=@id", batchId);
        Execute(conn, tx, "DELETE FROM CskuStatFileTable WHERE BatchId=@id", batchId);
        Execute(conn, tx, "DELETE FROM CskuStatBatchTable WHERE Id=@id", batchId);
        tx.Commit();
    }

    // ─── 중복 파일 판정 (§7) ─────────────────────────────────────────────────

    /// <summary>
    /// FileName/RowCount/SumQty/SumRevenue/SumProfit 5개가 모두 일치하는 저장된 파일을 찾는다.
    /// 일치하면 어느 배치와 겹치는지 안내할 수 있도록 (파일, 배치)를 함께 반환한다.
    /// </summary>
    public (CskuStatFile File, CskuStatBatch Batch)? FindDuplicateFile(
        string fileName, int rowCount, int sumQty, decimal sumRevenue, decimal sumProfit)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.Id, f.BatchId, f.FileName, f.FileKind, f.RowCount, f.SumQty, f.SumRevenue, f.SumProfit, f.LoadedAt,
                   b.Id, b.Period, b.Memo, b.ExchangeRate, b.FileCount, b.RowCount, b.CreatedAt
            FROM CskuStatFileTable f
            JOIN CskuStatBatchTable b ON b.Id = f.BatchId
            WHERE f.FileName = @fileName AND f.RowCount = @rowCount AND f.SumQty = @sumQty
              AND f.SumRevenue = @sumRevenue AND f.SumProfit = @sumProfit
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@fileName", fileName);
        cmd.Parameters.AddWithValue("@rowCount", rowCount);
        cmd.Parameters.AddWithValue("@sumQty", sumQty);
        cmd.Parameters.AddWithValue("@sumRevenue", (double)sumRevenue);
        cmd.Parameters.AddWithValue("@sumProfit", (double)sumProfit);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var file = ReadFile(reader, offset: 0);
        var batch = ReadBatch(reader, offset: 9);
        return (file, batch);
    }

    // ─── 헬퍼 ────────────────────────────────────────────────────────────────

    private static CskuStatBatch ReadBatch(SqliteDataReader r, int offset = 0) => new()
    {
        Id = r.GetInt64(offset),
        Period = r.GetString(offset + 1),
        Memo = r.GetString(offset + 2),
        ExchangeRate = (decimal)r.GetDouble(offset + 3),
        FileCount = r.GetInt32(offset + 4),
        RowCount = r.GetInt32(offset + 5),
        CreatedAt = DateTime.Parse(r.GetString(offset + 6)),
    };

    private static CskuStatLine ReadLine(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        BatchId = r.GetInt64(1),
        FileKind = Enum.Parse<CskuFileKind>(r.GetString(2)),
        ChannelCode = r.GetString(3),
        ChannelName = r.GetString(4),
        CskuCode = r.GetString(5),
        ProductGroup = r.GetString(6),
        ProductName = r.GetString(7),
        RowCount = r.GetInt32(8),
        Qty = r.GetInt32(9),
        Revenue = (decimal)r.GetDouble(10),
        Settlement = (decimal)r.GetDouble(11),
        Shipping = (decimal)r.GetDouble(12),
        Fee = (decimal)r.GetDouble(13),
        Profit = (decimal)r.GetDouble(14),
    };

    private static CskuStatFile ReadFile(SqliteDataReader r, int offset = 0) => new()
    {
        Id = r.GetInt64(offset),
        BatchId = r.GetInt64(offset + 1),
        FileName = r.GetString(offset + 2),
        FileKind = Enum.Parse<CskuFileKind>(r.GetString(offset + 3)),
        RowCount = r.GetInt32(offset + 4),
        SumQty = r.GetInt32(offset + 5),
        SumRevenue = (decimal)r.GetDouble(offset + 6),
        SumProfit = (decimal)r.GetDouble(offset + 7),
        LoadedAt = DateTime.Parse(r.GetString(offset + 8)),
    };

    private static void Execute(SqliteConnection conn, SqliteTransaction tx, string sql, long id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }
}

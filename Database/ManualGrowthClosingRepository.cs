using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 이공그로스수동마감 헤더/라인 저장소(ManualGrowthClosing_Spec.md §6.1). (Period, ChannelCode)당 1건이며
/// 다시 저장하면 헤더는 갱신, 라인은 전부 교체한다 — 라인은 CSKU/MSKU/원가를 스냅샷한 값이다.
/// </summary>
public class ManualGrowthClosingRepository
{
    public ManualGrowthClosing? Get(string period, string channelCode)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"{SelectHeaderSql} WHERE Period = $p AND ChannelCode = $c";
        cmd.Parameters.AddWithValue("$p", period);
        cmd.Parameters.AddWithValue("$c", channelCode);
        ManualGrowthClosing? closing;
        using (var reader = cmd.ExecuteReader())
            closing = reader.Read() ? ReadHeader(reader) : null;
        if (closing != null) closing.Lines = LoadLines(conn, closing.Id, closing.Period);
        return closing;
    }

    /// <summary>헤더 목록(라인 제외), 최신 마감월 순. channelCode가 null이면 전체 채널.</summary>
    public List<ManualGrowthClosing> GetHeaders(string? channelCode = null)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectHeaderSql + (channelCode == null ? "" : " WHERE ChannelCode = $c") + " ORDER BY Period DESC, ChannelCode";
        if (channelCode != null) cmd.Parameters.AddWithValue("$c", channelCode);
        using var reader = cmd.ExecuteReader();
        var result = new List<ManualGrowthClosing>();
        while (reader.Read()) result.Add(ReadHeader(reader));
        return result;
    }

    /// <summary>헤더를 (Period, ChannelCode) 기준으로 넣거나 갱신하고 라인을 통째로 교체한다. 저장된 Id를 반환.</summary>
    public long Save(ManualGrowthClosing closing)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var tx = conn.BeginTransaction();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using (var upsert = conn.CreateCommand())
        {
            upsert.Transaction = tx;
            upsert.CommandText = """
                INSERT INTO ManualGrowthClosingTable
                    (Period, ChannelCode, SourceFileName, SourceSheetName, Status, TotalQty, TotalSupply, TotalTax,
                     TotalCost, TotalProfit, StatusFlags, ConfirmedAt, CreatedAt)
                VALUES ($p, $c, $file, $sheet, $status, $qty, $supply, $tax, $cost, $profit, $flags, $confirmedAt, $now)
                ON CONFLICT(Period, ChannelCode) DO UPDATE SET
                    SourceFileName = excluded.SourceFileName,
                    SourceSheetName = excluded.SourceSheetName,
                    Status = excluded.Status,
                    TotalQty = excluded.TotalQty,
                    TotalSupply = excluded.TotalSupply,
                    TotalTax = excluded.TotalTax,
                    TotalCost = excluded.TotalCost,
                    TotalProfit = excluded.TotalProfit,
                    StatusFlags = excluded.StatusFlags,
                    ConfirmedAt = excluded.ConfirmedAt
                """;
            upsert.Parameters.AddWithValue("$p", closing.Period);
            upsert.Parameters.AddWithValue("$c", closing.ChannelCode);
            upsert.Parameters.AddWithValue("$file", closing.SourceFileName);
            upsert.Parameters.AddWithValue("$sheet", closing.SourceSheetName);
            upsert.Parameters.AddWithValue("$status", closing.Status);
            upsert.Parameters.AddWithValue("$qty", (double)closing.TotalQty);
            upsert.Parameters.AddWithValue("$supply", (double)closing.TotalSupply);
            upsert.Parameters.AddWithValue("$tax", (double)closing.TotalTax);
            upsert.Parameters.AddWithValue("$cost", (double)closing.TotalCost);
            upsert.Parameters.AddWithValue("$profit", (double)closing.TotalProfit);
            upsert.Parameters.AddWithValue("$flags", closing.StatusFlags);
            upsert.Parameters.AddWithValue("$confirmedAt", (object?)closing.ConfirmedAt ?? DBNull.Value);
            upsert.Parameters.AddWithValue("$now", now);
            upsert.ExecuteNonQuery();
        }

        long id;
        using (var idCmd = conn.CreateCommand())
        {
            idCmd.Transaction = tx;
            idCmd.CommandText = "SELECT Id, CreatedAt FROM ManualGrowthClosingTable WHERE Period = $p AND ChannelCode = $c";
            idCmd.Parameters.AddWithValue("$p", closing.Period);
            idCmd.Parameters.AddWithValue("$c", closing.ChannelCode);
            using var reader = idCmd.ExecuteReader();
            reader.Read();
            id = reader.GetInt64(0);
            closing.CreatedAt = reader.GetString(1);
        }

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM ManualGrowthClosingLineTable WHERE ClosingId = $id";
            del.Parameters.AddWithValue("$id", id);
            del.ExecuteNonQuery();
        }

        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO ManualGrowthClosingLineTable
                    (ClosingId, RowNo, LineDate, ItemName, CskuCode, MasterSku, ProductGroup, Qty, UnitPrice,
                     SupplyAmount, Tax, CostPrice, Profit, PriceMismatch)
                VALUES ($id, $rowNo, $date, $item, $csku, $msku, $group, $qty, $unit, $supply, $tax, $cost, $profit, $mismatch)
                """;
            ins.Parameters.AddWithValue("$id", id);
            foreach (var name in new[] { "$rowNo", "$date", "$item", "$csku", "$msku", "$group", "$qty", "$unit", "$supply", "$tax", "$cost", "$profit", "$mismatch" })
                ins.Parameters.Add(new SqliteParameter { ParameterName = name });

            foreach (var line in closing.Lines)
            {
                ins.Parameters["$rowNo"].Value = line.RowNo;
                ins.Parameters["$date"].Value = line.LineDate;
                ins.Parameters["$item"].Value = line.ItemName;
                ins.Parameters["$csku"].Value = line.CskuCode;
                ins.Parameters["$msku"].Value = line.MasterSku;
                ins.Parameters["$group"].Value = line.ProductGroup;
                ins.Parameters["$qty"].Value = (double)line.Qty;
                ins.Parameters["$unit"].Value = (double)line.UnitPrice;
                ins.Parameters["$supply"].Value = (double)line.SupplyAmount;
                ins.Parameters["$tax"].Value = (double)line.Tax;
                ins.Parameters["$cost"].Value = (double)line.CostPrice;
                ins.Parameters["$profit"].Value = (double)line.Profit;
                ins.Parameters["$mismatch"].Value = line.PriceMismatch ? 1 : 0;
                ins.ExecuteNonQuery();
            }
        }

        tx.Commit();
        closing.Id = id;
        return id;
    }

    /// <summary>확정/확정해제. 확정해제해도 라인 스냅샷은 남겨 이력에서 다시 볼 수 있게 한다.</summary>
    public void SetStatus(long id, string status)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE ManualGrowthClosingTable SET Status = $s, ConfirmedAt = $at WHERE Id = $id";
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$at", status == ManualGrowthClosing.StatusConfirmed
            ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var tx = conn.BeginTransaction();
        foreach (var sql in new[]
                 {
                     "DELETE FROM ManualGrowthClosingLineTable WHERE ClosingId = $id",
                     "DELETE FROM ManualGrowthClosingTable WHERE Id = $id",
                 })
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────

    private const string SelectHeaderSql = """
        SELECT Id, Period, ChannelCode, SourceFileName, SourceSheetName, Status, TotalQty, TotalSupply, TotalTax,
               TotalCost, TotalProfit, StatusFlags, ConfirmedAt, CreatedAt
        FROM ManualGrowthClosingTable
        """;

    private static ManualGrowthClosing ReadHeader(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Period = r.GetString(1),
        ChannelCode = r.GetString(2),
        SourceFileName = r.GetString(3),
        SourceSheetName = r.GetString(4),
        Status = r.GetString(5),
        TotalQty = (decimal)r.GetDouble(6),
        TotalSupply = (decimal)r.GetDouble(7),
        TotalTax = (decimal)r.GetDouble(8),
        TotalCost = (decimal)r.GetDouble(9),
        TotalProfit = (decimal)r.GetDouble(10),
        StatusFlags = r.GetString(11),
        ConfirmedAt = r.IsDBNull(12) ? null : r.GetString(12),
        CreatedAt = r.GetString(13),
    };

    private static List<ManualGrowthLine> LoadLines(SqliteConnection conn, long closingId, string period)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT RowNo, LineDate, ItemName, CskuCode, MasterSku, ProductGroup, Qty, UnitPrice, SupplyAmount, Tax,
                   CostPrice, Profit, PriceMismatch
            FROM ManualGrowthClosingLineTable WHERE ClosingId = $id ORDER BY RowNo, Id
            """;
        cmd.Parameters.AddWithValue("$id", closingId);
        using var r = cmd.ExecuteReader();
        var lines = new List<ManualGrowthLine>();
        while (r.Read())
        {
            lines.Add(new ManualGrowthLine
            {
                Period = period,
                RowNo = r.GetInt32(0),
                LineDate = r.GetString(1),
                ItemName = r.GetString(2),
                CskuCode = r.GetString(3),
                MasterSku = r.GetString(4),
                ProductGroup = r.GetString(5),
                Qty = (decimal)r.GetDouble(6),
                UnitPrice = (decimal)r.GetDouble(7),
                SupplyAmount = (decimal)r.GetDouble(8),
                Tax = (decimal)r.GetDouble(9),
                CostPrice = (decimal)r.GetDouble(10),
                Profit = (decimal)r.GetDouble(11),
                PriceMismatch = r.GetInt64(12) != 0,
                DateMismatch = r.GetString(1).Length >= 7 && r.GetString(1)[5..7] != period[5..7],
            });
        }
        return lines;
    }
}

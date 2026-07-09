using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

public class ProfitFactRepository
{
    // ───────────── ProfitFactTable ─────────────

    /// <summary>동일 (Period, ChannelCode) 조합은 교체(삭제 후 재삽입).</summary>
    public void SaveProfitFacts(string period, string channelCode, string channelName, IEnumerable<ProfitFactRow> rows)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var tx = conn.BeginTransaction();

        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM ProfitFactTable WHERE Period = @p AND ChannelCode = @c";
        del.Parameters.AddWithValue("@p", period);
        del.Parameters.AddWithValue("@c", channelCode);
        del.ExecuteNonQuery();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = """
            INSERT INTO ProfitFactTable (Period, ChannelCode, ChannelName, ProductGroup, Qty, Revenue, GrossProfit, SavedAt)
            VALUES (@period, @channelCode, @channelName, @productGroup, @qty, @revenue, @grossProfit, @savedAt)
            """;
        ins.Parameters.Add("@period", SqliteType.Text);
        ins.Parameters.Add("@channelCode", SqliteType.Text);
        ins.Parameters.Add("@channelName", SqliteType.Text);
        ins.Parameters.Add("@productGroup", SqliteType.Text);
        ins.Parameters.Add("@qty", SqliteType.Integer);
        ins.Parameters.Add("@revenue", SqliteType.Real);
        ins.Parameters.Add("@grossProfit", SqliteType.Real);
        ins.Parameters.Add("@savedAt", SqliteType.Text);

        foreach (var row in rows)
        {
            ins.Parameters["@period"].Value = period;
            ins.Parameters["@channelCode"].Value = channelCode;
            ins.Parameters["@channelName"].Value = channelName;
            ins.Parameters["@productGroup"].Value = row.ProductGroup;
            ins.Parameters["@qty"].Value = row.Qty;
            ins.Parameters["@revenue"].Value = (double)row.Revenue;
            ins.Parameters["@grossProfit"].Value = (double)row.GrossProfit;
            ins.Parameters["@savedAt"].Value = now;
            ins.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public List<ProfitFactRow> GetProfitFacts(IReadOnlyList<string>? periods = null, IReadOnlyList<string>? channels = null, IReadOnlyList<string>? groups = null)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        var where = BuildWhereClause(periods, channels, groups, cmd);
        cmd.CommandText = $"SELECT Id,Period,ChannelCode,ChannelName,ProductGroup,Qty,Revenue,GrossProfit,SavedAt FROM ProfitFactTable{where} ORDER BY Period,ChannelCode,ProductGroup";
        using var reader = cmd.ExecuteReader();
        var result = new List<ProfitFactRow>();
        while (reader.Read())
        {
            result.Add(new ProfitFactRow
            {
                Id = reader.GetInt64(0),
                Period = reader.GetString(1),
                ChannelCode = reader.GetString(2),
                ChannelName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ProductGroup = reader.GetString(4),
                Qty = reader.GetInt32(5),
                Revenue = (decimal)reader.GetDouble(6),
                GrossProfit = (decimal)reader.GetDouble(7),
                SavedAt = reader.GetString(8),
            });
        }
        return result;
    }

    public List<string> GetDistinctProfitPeriods()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT Period FROM ProfitFactTable ORDER BY Period DESC";
        using var reader = cmd.ExecuteReader();
        var result = new List<string>();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    public bool HasData(string period, string channelCode)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM ProfitFactTable WHERE Period=@p AND ChannelCode=@c";
        cmd.Parameters.AddWithValue("@p", period);
        cmd.Parameters.AddWithValue("@c", channelCode);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    // ───────────── AdFactTable ─────────────

    public void SaveAdFacts(string period, string channelCode, string channelName, IEnumerable<AdFactRow> rows)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var tx = conn.BeginTransaction();

        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM AdFactTable WHERE Period = @p AND ChannelCode = @c";
        del.Parameters.AddWithValue("@p", period);
        del.Parameters.AddWithValue("@c", channelCode);
        del.ExecuteNonQuery();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = """
            INSERT INTO AdFactTable (Period, ChannelCode, ChannelName, ProductGroup, AdCost, SavedAt)
            VALUES (@period, @channelCode, @channelName, @productGroup, @adCost, @savedAt)
            """;
        ins.Parameters.Add("@period", SqliteType.Text);
        ins.Parameters.Add("@channelCode", SqliteType.Text);
        ins.Parameters.Add("@channelName", SqliteType.Text);
        ins.Parameters.Add("@productGroup", SqliteType.Text);
        ins.Parameters.Add("@adCost", SqliteType.Real);
        ins.Parameters.Add("@savedAt", SqliteType.Text);

        foreach (var row in rows)
        {
            ins.Parameters["@period"].Value = period;
            ins.Parameters["@channelCode"].Value = channelCode;
            ins.Parameters["@channelName"].Value = channelName;
            ins.Parameters["@productGroup"].Value = row.ProductGroup;
            ins.Parameters["@adCost"].Value = (double)row.AdCost;
            ins.Parameters["@savedAt"].Value = now;
            ins.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public List<AdFactRow> GetAdFacts(IReadOnlyList<string>? periods = null, IReadOnlyList<string>? channels = null, IReadOnlyList<string>? groups = null)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        var where = BuildWhereClause(periods, channels, groups, cmd);
        cmd.CommandText = $"SELECT Id,Period,ChannelCode,ChannelName,ProductGroup,AdCost,SavedAt FROM AdFactTable{where} ORDER BY Period,ChannelCode,ProductGroup";
        using var reader = cmd.ExecuteReader();
        var result = new List<AdFactRow>();
        while (reader.Read())
        {
            result.Add(new AdFactRow
            {
                Id = reader.GetInt64(0),
                Period = reader.GetString(1),
                ChannelCode = reader.GetString(2),
                ChannelName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ProductGroup = reader.GetString(4),
                AdCost = (decimal)reader.GetDouble(5),
                SavedAt = reader.GetString(6),
            });
        }
        return result;
    }

    public List<string> GetDistinctAdPeriods()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT Period FROM AdFactTable ORDER BY Period DESC";
        using var reader = cmd.ExecuteReader();
        var result = new List<string>();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    public bool HasAdData(string period, string channelCode)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM AdFactTable WHERE Period=@p AND ChannelCode=@c";
        cmd.Parameters.AddWithValue("@p", period);
        cmd.Parameters.AddWithValue("@c", channelCode);
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    // ───────────── helpers ─────────────

    private static string BuildWhereClause(IReadOnlyList<string>? periods, IReadOnlyList<string>? channels, IReadOnlyList<string>? groups, SqliteCommand cmd)
    {
        var clauses = new List<string>();

        if (periods is { Count: > 0 })
        {
            var pParams = AddListParams(cmd, "@p", periods);
            clauses.Add($"Period IN ({string.Join(",", pParams)})");
        }
        if (channels is { Count: > 0 })
        {
            var cParams = AddListParams(cmd, "@c", channels);
            clauses.Add($"ChannelCode IN ({string.Join(",", cParams)})");
        }
        if (groups is { Count: > 0 })
        {
            var gParams = AddListParams(cmd, "@g", groups);
            clauses.Add($"ProductGroup IN ({string.Join(",", gParams)})");
        }

        return clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "";
    }

    private static List<string> AddListParams(SqliteCommand cmd, string prefix, IReadOnlyList<string> values)
    {
        var names = new List<string>();
        for (int i = 0; i < values.Count; i++)
        {
            var name = $"{prefix}{i}";
            cmd.Parameters.AddWithValue(name, values[i]);
            names.Add(name);
        }
        return names;
    }
}

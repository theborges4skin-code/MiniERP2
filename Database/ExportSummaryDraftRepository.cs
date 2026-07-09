using Microsoft.Data.Sqlite;

namespace MiniERP2.Database;

public class ExportSummaryDraftRow
{
    public long Id { get; set; }
    public string MarketCode { get; set; } = "";
    public string YearMonth { get; set; } = "";
    public string Indicator { get; set; } = "";
    public string Currency { get; set; } = "";
    public decimal Amount { get; set; }
    public string SavedAt { get; set; } = "";
}

/// <summary>수동 입력 임시저장 CRUD.</summary>
public class ExportSummaryDraftRepository
{
    public List<ExportSummaryDraftRow> GetByMarket(string marketCode)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, MarketCode, YearMonth, Indicator, Currency, Amount, SavedAt
            FROM ExportSummaryDraftEntry
            WHERE MarketCode = @mc
            ORDER BY YearMonth, Indicator
            """;
        cmd.Parameters.AddWithValue("@mc", marketCode);
        return ReadRows(cmd);
    }

    public List<ExportSummaryDraftRow> GetAll()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, MarketCode, YearMonth, Indicator, Currency, Amount, SavedAt
            FROM ExportSummaryDraftEntry
            ORDER BY MarketCode, YearMonth, Indicator
            """;
        return ReadRows(cmd);
    }

    /// <summary>마켓 단위로 전체 교체(기존 삭제 후 재삽입).</summary>
    public void SaveForMarket(string marketCode, IEnumerable<ExportSummaryDraftRow> rows)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var tx = conn.BeginTransaction();

        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM ExportSummaryDraftEntry WHERE MarketCode = @mc";
        del.Parameters.AddWithValue("@mc", marketCode);
        del.ExecuteNonQuery();

        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = """
            INSERT INTO ExportSummaryDraftEntry (MarketCode, YearMonth, Indicator, Currency, Amount, SavedAt)
            VALUES (@mc, @ym, @ind, @cur, @amt, @at)
            """;
        ins.Parameters.Add("@mc", SqliteType.Text);
        ins.Parameters.Add("@ym", SqliteType.Text);
        ins.Parameters.Add("@ind", SqliteType.Text);
        ins.Parameters.Add("@cur", SqliteType.Text);
        ins.Parameters.Add("@amt", SqliteType.Real);
        ins.Parameters.Add("@at", SqliteType.Text);

        foreach (var r in rows)
        {
            ins.Parameters["@mc"].Value = marketCode;
            ins.Parameters["@ym"].Value = r.YearMonth;
            ins.Parameters["@ind"].Value = r.Indicator;
            ins.Parameters["@cur"].Value = r.Currency;
            ins.Parameters["@amt"].Value = (double)r.Amount;
            ins.Parameters["@at"].Value = now;
            ins.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public List<string> GetDistinctMarkets()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT MarketCode FROM ExportSummaryDraftEntry ORDER BY MarketCode";
        using var reader = cmd.ExecuteReader();
        var list = new List<string>();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    private static List<ExportSummaryDraftRow> ReadRows(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var list = new List<ExportSummaryDraftRow>();
        while (reader.Read())
        {
            list.Add(new ExportSummaryDraftRow
            {
                Id = reader.GetInt64(0),
                MarketCode = reader.GetString(1),
                YearMonth = reader.GetString(2),
                Indicator = reader.GetString(3),
                Currency = reader.GetString(4),
                Amount = (decimal)reader.GetDouble(5),
                SavedAt = reader.IsDBNull(6) ? "" : reader.GetString(6),
            });
        }
        return list;
    }
}

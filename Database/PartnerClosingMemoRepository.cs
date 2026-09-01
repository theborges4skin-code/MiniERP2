using System.Globalization;
using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 거래처 마감보드 메모(PartnerClosingMemoTable) CRUD. 거래처 전체 메모(OutboundDetailIds='')와
/// 라인 참조 메모(쉼표구분 Id 목록)를 같은 테이블에서 다룬다.
/// </summary>
public class PartnerClosingMemoRepository
{
    private const string Cols = "Id, Period, PartyKey, MemoText, ShowOnStatement, ShowOnLedger, OutboundDetailIds, CreatedAt";

    public List<PartnerClosingMemo> GetForParty(string period, string partyKey)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Cols} FROM PartnerClosingMemoTable WHERE Period = $period AND PartyKey = $key ORDER BY CreatedAt";
        command.Parameters.AddWithValue("$period", period);
        command.Parameters.AddWithValue("$key", partyKey);

        var result = new List<PartnerClosingMemo>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(ReadMemo(reader));
        return result;
    }

    public long Add(PartnerClosingMemo memo)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PartnerClosingMemoTable (Period, PartyKey, MemoText, ShowOnStatement, ShowOnLedger, OutboundDetailIds, CreatedAt)
            VALUES ($period, $key, $text, $statement, $ledger, $ids, $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$period", memo.Period);
        command.Parameters.AddWithValue("$key", memo.PartyKey);
        command.Parameters.AddWithValue("$text", memo.MemoText);
        command.Parameters.AddWithValue("$statement", memo.ShowOnStatement ? 1 : 0);
        command.Parameters.AddWithValue("$ledger", memo.ShowOnLedger ? 1 : 0);
        command.Parameters.AddWithValue("$ids", string.Join(",", memo.OutboundDetailIds));
        command.Parameters.AddWithValue("$createdAt", memo.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        return (long)command.ExecuteScalar()!;
    }

    public void Delete(long id)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PartnerClosingMemoTable WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static PartnerClosingMemo ReadMemo(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Period = r.GetString(1),
        PartyKey = r.GetString(2),
        MemoText = r.GetString(3),
        ShowOnStatement = r.GetInt32(4) == 1,
        ShowOnLedger = r.GetInt32(5) == 1,
        OutboundDetailIds = r.GetString(6).Length == 0
            ? []
            : r.GetString(6).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToList(),
        CreatedAt = DateTime.TryParse(r.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.Now,
    };
}

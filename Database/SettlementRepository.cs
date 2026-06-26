using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 이익분석 결과(정산 데이터)에 대한 데이터베이스 작업을 처리합니다.
/// </summary>
public class SettlementRepository
{
    public void Insert(IEnumerable<SettlementData> rows)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SettlementData (ChannelCode, ProductName, OptionName, Msku, Qty, Settlement, Shipping, Fee, Profit, Status)
            VALUES ($channelCode, $productName, $optionName, $msku, $qty, $settlement, $shipping, $fee, $profit, $status)
            """;

        foreach (var row in rows)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$channelCode", row.ChannelCode);
            command.Parameters.AddWithValue("$productName", (object?)row.ProductName ?? DBNull.Value);
            command.Parameters.AddWithValue("$optionName", (object?)row.OptionName ?? DBNull.Value);
            command.Parameters.AddWithValue("$msku", (object?)row.Msku ?? DBNull.Value);
            command.Parameters.AddWithValue("$qty", row.Qty);
            command.Parameters.AddWithValue("$settlement", row.Settlement);
            command.Parameters.AddWithValue("$shipping", row.Shipping);
            command.Parameters.AddWithValue("$fee", row.Fee);
            command.Parameters.AddWithValue("$profit", row.Profit);
            command.Parameters.AddWithValue("$status", (object?)row.Status ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public List<SettlementData> GetByChannel(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ChannelCode, ProductName, OptionName, Msku, Qty, Settlement, Shipping, Fee, Profit, Status
            FROM SettlementData
            WHERE ChannelCode = $channelCode
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);

        var results = new List<SettlementData>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadSettlementData(reader));
        }
        return results;
    }

    private static SettlementData ReadSettlementData(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ChannelCode = reader.GetString(1),
        ProductName = reader.IsDBNull(2) ? null : reader.GetString(2),
        OptionName = reader.IsDBNull(3) ? null : reader.GetString(3),
        Msku = reader.IsDBNull(4) ? null : reader.GetString(4),
        Qty = reader.GetInt32(5),
        Settlement = reader.GetDecimal(6),
        Shipping = reader.GetDecimal(7),
        Fee = reader.GetDecimal(8),
        Profit = reader.GetDecimal(9),
        Status = reader.IsDBNull(10) ? null : reader.GetString(10),
    };
}

using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

public class ChannelSkuRepository
{
    public void Upsert(ChannelSkuModel csku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var existing = GetByChannelAndMsku(connection, csku.ChannelCode, csku.Msku);
            if (existing is not null && existing.SupplyPrice != csku.SupplyPrice)
            {
                using var historyCommand = connection.CreateCommand();
                historyCommand.Transaction = transaction;
                historyCommand.CommandText = """
                    INSERT INTO ChannelSkuPriceHistory (ChannelCode, Msku, OldPrice, NewPrice, ChangedAt)
                    VALUES ($channelCode, $msku, $oldPrice, $newPrice, $changedAt)
                    """;
                historyCommand.Parameters.AddWithValue("$channelCode", csku.ChannelCode);
                historyCommand.Parameters.AddWithValue("$msku", csku.Msku);
                historyCommand.Parameters.AddWithValue("$oldPrice", existing.SupplyPrice);
                historyCommand.Parameters.AddWithValue("$newPrice", csku.SupplyPrice);
                historyCommand.Parameters.AddWithValue("$changedAt", DateTime.UtcNow);
                historyCommand.ExecuteNonQuery();
            }

            using var upsertCommand = connection.CreateCommand();
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandText = """
                INSERT INTO ChannelSkuTable (ChannelCode, Msku, SupplyPrice, InvoiceDisplayName)
                VALUES ($channelCode, $msku, $supplyPrice, $invoiceDisplayName)
                ON CONFLICT(ChannelCode, Msku) DO UPDATE SET
                    SupplyPrice = excluded.SupplyPrice,
                    InvoiceDisplayName = excluded.InvoiceDisplayName
                """;
            upsertCommand.Parameters.AddWithValue("$channelCode", csku.ChannelCode);
            upsertCommand.Parameters.AddWithValue("$msku", csku.Msku);
            upsertCommand.Parameters.AddWithValue("$supplyPrice", csku.SupplyPrice);
            upsertCommand.Parameters.AddWithValue("$invoiceDisplayName", csku.InvoiceDisplayName ?? (object)DBNull.Value);
            upsertCommand.ExecuteNonQuery();

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void Delete(string channelCode, string msku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            // 1. Delete price history first.
            using var historyCommand = connection.CreateCommand();
            historyCommand.Transaction = transaction;
            historyCommand.CommandText = "DELETE FROM ChannelSkuPriceHistory WHERE ChannelCode = $channelCode AND Msku = $msku";
            historyCommand.Parameters.AddWithValue("$channelCode", channelCode);
            historyCommand.Parameters.AddWithValue("$msku", msku);
            historyCommand.ExecuteNonQuery();

            // 2. Delete the channel SKU.
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM ChannelSkuTable WHERE ChannelCode = $channelCode AND Msku = $msku";
            deleteCommand.Parameters.AddWithValue("$channelCode", channelCode);
            deleteCommand.Parameters.AddWithValue("$msku", msku);
            deleteCommand.ExecuteNonQuery();

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public ChannelSkuModel? GetByChannelAndMsku(string channelCode, string msku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        return GetByChannelAndMsku(connection, channelCode, msku);
    }

    public List<ChannelSkuModel> GetAllByMsku(string msku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ChannelCode, Msku, SupplyPrice, InvoiceDisplayName
            FROM ChannelSkuTable
            WHERE Msku = $msku
            """;
        command.Parameters.AddWithValue("$msku", msku);

        var cskus = new List<ChannelSkuModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cskus.Add(ReadChannelSku(reader));
        }
        return cskus;
    }

    public List<ChannelSkuPriceHistoryModel> GetPriceHistory(string channelCode, string msku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ChannelCode, Msku, OldPrice, NewPrice, ChangedAt
            FROM ChannelSkuPriceHistory
            WHERE ChannelCode = $channelCode AND Msku = $msku
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$msku", msku);

        var history = new List<ChannelSkuPriceHistoryModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            history.Add(new ChannelSkuPriceHistoryModel
            {
                Id = reader.GetInt64(0),
                ChannelCode = reader.GetString(1),
                Msku = reader.GetString(2),
                OldPrice = reader.GetDecimal(3),
                NewPrice = reader.GetDecimal(4),
                ChangedAt = reader.GetDateTime(5),
            });
        }
        return history;
    }

    /// <summary>
    /// 지정된 채널의 모든 CSKU(채널별 SKU 설정: 납품가/송장표시명)를 한 번에 가져옵니다.
    /// SkuMapper가 매핑 시마다 따로 조회하지 않도록 채널 단위로 묶어서 제공합니다.
    /// </summary>
    public List<ChannelSkuModel> GetAllByChannel(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ChannelCode, Msku, SupplyPrice, InvoiceDisplayName FROM ChannelSkuTable WHERE ChannelCode = $channelCode";
        command.Parameters.AddWithValue("$channelCode", channelCode);

        var cskus = new List<ChannelSkuModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cskus.Add(ReadChannelSku(reader));
        }
        return cskus;
    }

    private static ChannelSkuModel? GetByChannelAndMsku(SqliteConnection connection, string channelCode, string msku)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ChannelCode, Msku, SupplyPrice, InvoiceDisplayName FROM ChannelSkuTable WHERE ChannelCode = $channelCode AND Msku = $msku";
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$msku", msku);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadChannelSku(reader) : null;
    }

    private static ChannelSkuModel ReadChannelSku(SqliteDataReader reader) => new()
    {
        ChannelCode = reader.GetString(0),
        Msku = reader.GetString(1),
        SupplyPrice = reader.GetDecimal(2),
        InvoiceDisplayName = reader.IsDBNull(3) ? null : reader.GetString(3),
    };
}
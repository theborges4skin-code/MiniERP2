using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 매입SKU(PurchaseSkuTable)에 대한 데이터베이스 작업을 처리합니다. 매출측 ChannelSkuRepository와
/// 대칭 구조이되, CSKU 분화가 없어 (ChannelCode, Msku)가 그대로 고유키입니다(§D3).
/// </summary>
public class PurchaseSkuRepository
{
    /// <param name="reason">매입가가 실제로 바뀔 때만 PurchaseSkuPriceHistory.Reason에 함께 기록되는 사유.</param>
    public void Upsert(PurchaseSkuModel sku, string? reason = null)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var existing = GetByChannelAndMsku(connection, sku.ChannelCode, sku.Msku);
            var now = DateTime.Now;

            if (existing is not null && existing.PurchasePrice != sku.PurchasePrice)
            {
                using var historyCommand = connection.CreateCommand();
                historyCommand.Transaction = transaction;
                historyCommand.CommandText = """
                    INSERT INTO PurchaseSkuPriceHistory (ChannelCode, Msku, OldPrice, NewPrice, ChangedAt, Reason)
                    VALUES ($channelCode, $msku, $oldPrice, $newPrice, $changedAt, $reason)
                    """;
                historyCommand.Parameters.AddWithValue("$channelCode", sku.ChannelCode);
                historyCommand.Parameters.AddWithValue("$msku", sku.Msku);
                historyCommand.Parameters.AddWithValue("$oldPrice", existing.PurchasePrice);
                historyCommand.Parameters.AddWithValue("$newPrice", sku.PurchasePrice);
                historyCommand.Parameters.AddWithValue("$changedAt", now);
                historyCommand.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
                historyCommand.ExecuteNonQuery();
            }

            using var upsertCommand = connection.CreateCommand();
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandText = """
                INSERT INTO PurchaseSkuTable (ChannelCode, Msku, PurchasePrice, Unit, Note, UpdatedAt)
                VALUES ($channelCode, $msku, $purchasePrice, $unit, $note, $updatedAt)
                ON CONFLICT(ChannelCode, Msku) DO UPDATE SET
                    PurchasePrice = excluded.PurchasePrice,
                    Unit = excluded.Unit,
                    Note = excluded.Note,
                    UpdatedAt = excluded.UpdatedAt
                """;
            upsertCommand.Parameters.AddWithValue("$channelCode", sku.ChannelCode);
            upsertCommand.Parameters.AddWithValue("$msku", sku.Msku);
            upsertCommand.Parameters.AddWithValue("$purchasePrice", sku.PurchasePrice);
            upsertCommand.Parameters.AddWithValue("$unit", sku.Unit);
            upsertCommand.Parameters.AddWithValue("$note", sku.Note ?? (object)DBNull.Value);
            upsertCommand.Parameters.AddWithValue("$updatedAt", now);
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
            using var historyCommand = connection.CreateCommand();
            historyCommand.Transaction = transaction;
            historyCommand.CommandText = "DELETE FROM PurchaseSkuPriceHistory WHERE ChannelCode = $channelCode AND Msku = $msku";
            historyCommand.Parameters.AddWithValue("$channelCode", channelCode);
            historyCommand.Parameters.AddWithValue("$msku", msku);
            historyCommand.ExecuteNonQuery();

            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM PurchaseSkuTable WHERE ChannelCode = $channelCode AND Msku = $msku";
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

    public PurchaseSkuModel? GetByChannelAndMsku(string channelCode, string msku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        return GetByChannelAndMsku(connection, channelCode, msku);
    }

    /// <summary>지정된 마스터SKU를 매입하는 모든 매입처(채널)를 가져옵니다(통합 조회창의 "매입처들 매입가" 열용).</summary>
    public List<PurchaseSkuModel> GetAllByMsku(string msku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ChannelCode, Msku, PurchasePrice, Unit, Note, UpdatedAt
            FROM PurchaseSkuTable
            WHERE Msku = $msku
            """;
        command.Parameters.AddWithValue("$msku", msku);

        var skus = new List<PurchaseSkuModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            skus.Add(ReadPurchaseSku(reader));
        }
        return skus;
    }

    /// <summary>지정된 매입처(채널)가 매입하는 모든 마스터SKU를 가져옵니다(매입SKU CRUD 화면용).</summary>
    public List<PurchaseSkuModel> GetAllByChannel(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ChannelCode, Msku, PurchasePrice, Unit, Note, UpdatedAt FROM PurchaseSkuTable WHERE ChannelCode = $channelCode";
        command.Parameters.AddWithValue("$channelCode", channelCode);

        var skus = new List<PurchaseSkuModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            skus.Add(ReadPurchaseSku(reader));
        }
        return skus;
    }

    /// <summary>매입처 불문하고 모든 매입SKU를 가져옵니다(데이터 관리창 전체 조회/내보내기용).</summary>
    public List<PurchaseSkuModel> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ChannelCode, Msku, PurchasePrice, Unit, Note, UpdatedAt FROM PurchaseSkuTable";

        var skus = new List<PurchaseSkuModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            skus.Add(ReadPurchaseSku(reader));
        }
        return skus;
    }

    public List<PurchaseSkuPriceHistoryModel> GetPriceHistory(string channelCode, string msku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ChannelCode, Msku, OldPrice, NewPrice, ChangedAt, Reason, Note
            FROM PurchaseSkuPriceHistory
            WHERE ChannelCode = $channelCode AND Msku = $msku
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$msku", msku);

        var history = new List<PurchaseSkuPriceHistoryModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            history.Add(new PurchaseSkuPriceHistoryModel
            {
                Id = reader.GetInt64(0),
                ChannelCode = reader.GetString(1),
                Msku = reader.GetString(2),
                OldPrice = reader.GetDecimal(3),
                NewPrice = reader.GetDecimal(4),
                ChangedAt = reader.GetDateTime(5),
                Reason = reader.IsDBNull(6) ? null : reader.GetString(6),
                Note = reader.IsDBNull(7) ? null : reader.GetString(7),
            });
        }
        return history;
    }

    private static PurchaseSkuModel? GetByChannelAndMsku(SqliteConnection connection, string channelCode, string msku)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ChannelCode, Msku, PurchasePrice, Unit, Note, UpdatedAt FROM PurchaseSkuTable WHERE ChannelCode = $channelCode AND Msku = $msku";
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$msku", msku);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadPurchaseSku(reader) : null;
    }

    private static PurchaseSkuModel ReadPurchaseSku(SqliteDataReader reader) => new()
    {
        ChannelCode = reader.GetString(0),
        Msku = reader.GetString(1),
        PurchasePrice = reader.GetDecimal(2),
        Unit = reader.IsDBNull(3) ? "kg" : reader.GetString(3),
        Note = reader.IsDBNull(4) ? null : reader.GetString(4),
        UpdatedAt = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
    };
}

using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// CSKU(채널별 SKU)에 대한 데이터베이스 작업을 처리합니다. (ChannelCode, CskuCode)가 고유키이며,
/// 같은 Msku(마스터SKU)를 가리키는 CskuCode가 한 채널에 여러 개 있을 수 있습니다(채널 옵션 분화).
/// </summary>
public class ChannelSkuRepository
{
    public void Upsert(ChannelSkuModel csku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var existing = GetByChannelAndCskuCode(connection, csku.ChannelCode, csku.CskuCode);
            if (existing is not null && existing.SupplyPrice != csku.SupplyPrice)
            {
                using var historyCommand = connection.CreateCommand();
                historyCommand.Transaction = transaction;
                historyCommand.CommandText = """
                    INSERT INTO ChannelSkuPriceHistory (ChannelCode, Msku, OldPrice, NewPrice, ChangedAt)
                    VALUES ($channelCode, $cskuCode, $oldPrice, $newPrice, $changedAt)
                    """;
                historyCommand.Parameters.AddWithValue("$channelCode", csku.ChannelCode);
                historyCommand.Parameters.AddWithValue("$cskuCode", csku.CskuCode);
                historyCommand.Parameters.AddWithValue("$oldPrice", existing.SupplyPrice);
                historyCommand.Parameters.AddWithValue("$newPrice", csku.SupplyPrice);
                historyCommand.Parameters.AddWithValue("$changedAt", DateTime.UtcNow);
                historyCommand.ExecuteNonQuery();
            }

            using var upsertCommand = connection.CreateCommand();
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandText = """
                INSERT INTO ChannelSkuTable (ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName)
                VALUES ($channelCode, $cskuCode, $msku, $supplyPrice, $invoiceDisplayName)
                ON CONFLICT(ChannelCode, CskuCode) DO UPDATE SET
                    Msku = excluded.Msku,
                    SupplyPrice = excluded.SupplyPrice,
                    InvoiceDisplayName = excluded.InvoiceDisplayName
                """;
            upsertCommand.Parameters.AddWithValue("$channelCode", csku.ChannelCode);
            upsertCommand.Parameters.AddWithValue("$cskuCode", csku.CskuCode);
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

    public void Delete(string channelCode, string cskuCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            // 1. Delete price history first.
            using var historyCommand = connection.CreateCommand();
            historyCommand.Transaction = transaction;
            historyCommand.CommandText = "DELETE FROM ChannelSkuPriceHistory WHERE ChannelCode = $channelCode AND Msku = $cskuCode";
            historyCommand.Parameters.AddWithValue("$channelCode", channelCode);
            historyCommand.Parameters.AddWithValue("$cskuCode", cskuCode);
            historyCommand.ExecuteNonQuery();

            // 2. Delete the channel SKU.
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM ChannelSkuTable WHERE ChannelCode = $channelCode AND CskuCode = $cskuCode";
            deleteCommand.Parameters.AddWithValue("$channelCode", channelCode);
            deleteCommand.Parameters.AddWithValue("$cskuCode", cskuCode);
            deleteCommand.ExecuteNonQuery();

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// (채널, CSKU 코드)로 CSKU 1건을 조회합니다. 매핑 규칙의 TargetSku/OfsOrderItem.MappedSku가
    /// 실제로 가리키는 값이 CskuCode이므로, 매핑/출고/정산 전반에서 이 메서드가 1차 조회 지점이 됩니다.
    /// </summary>
    public ChannelSkuModel? GetByChannelAndCskuCode(string channelCode, string cskuCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        return GetByChannelAndCskuCode(connection, channelCode, cskuCode);
    }

    /// <summary>
    /// 매핑 규칙의 TargetSku(=code)가 가리키는 실제 마스터SKU를 찾습니다. CSKU로 등록되어 있으면
    /// 그 CSKU의 Msku를 반환하고, CSKU 레코드가 없으면(과거 방식으로 마스터SKU를 그대로 TargetSku로
    /// 쓴 단순 1:1/임시 규칙) code 자체를 마스터SKU로 간주해 그대로 반환합니다.
    /// </summary>
    public string ResolveMasterSku(string channelCode, string code)
    {
        var csku = GetByChannelAndCskuCode(channelCode, code);
        return csku?.Msku ?? code;
    }

    /// <summary>
    /// 지정된 마스터SKU를 가리키는 모든 CSKU를 채널 불문하고 가져옵니다.
    /// </summary>
    public List<ChannelSkuModel> GetAllByMsku(string msku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName
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

    public List<ChannelSkuPriceHistoryModel> GetPriceHistory(string channelCode, string cskuCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ChannelCode, Msku, OldPrice, NewPrice, ChangedAt
            FROM ChannelSkuPriceHistory
            WHERE ChannelCode = $channelCode AND Msku = $cskuCode
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$cskuCode", cskuCode);

        var history = new List<ChannelSkuPriceHistoryModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            history.Add(new ChannelSkuPriceHistoryModel
            {
                Id = reader.GetInt64(0),
                ChannelCode = reader.GetString(1),
                CskuCode = reader.GetString(2),
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
        command.CommandText = "SELECT ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName FROM ChannelSkuTable WHERE ChannelCode = $channelCode";
        command.Parameters.AddWithValue("$channelCode", channelCode);

        var cskus = new List<ChannelSkuModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cskus.Add(ReadChannelSku(reader));
        }
        return cskus;
    }

    /// <summary>채널 불문하고 모든 CSKU를 가져옵니다(데이터 관리창의 전체 내보내기/조회용).</summary>
    public List<ChannelSkuModel> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName FROM ChannelSkuTable";

        var cskus = new List<ChannelSkuModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cskus.Add(ReadChannelSku(reader));
        }
        return cskus;
    }

    private static ChannelSkuModel? GetByChannelAndCskuCode(SqliteConnection connection, string channelCode, string cskuCode)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName FROM ChannelSkuTable WHERE ChannelCode = $channelCode AND CskuCode = $cskuCode";
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$cskuCode", cskuCode);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadChannelSku(reader) : null;
    }

    private static ChannelSkuModel ReadChannelSku(SqliteDataReader reader) => new()
    {
        ChannelCode = reader.GetString(0),
        CskuCode = reader.GetString(1),
        Msku = reader.GetString(2),
        SupplyPrice = reader.GetDecimal(3),
        InvoiceDisplayName = reader.IsDBNull(4) ? null : reader.GetString(4),
    };
}

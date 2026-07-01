using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

public class ItemRepository
{
    public void Upsert(ItemModel item)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var existing = GetBySku(connection, item.Sku);
            if (existing is not null && existing.CostPrice != item.CostPrice)
            {
                using var historyCommand = connection.CreateCommand();
                historyCommand.Transaction = transaction;
                historyCommand.CommandText = """
                    INSERT INTO ItemCostHistory (Sku, OldCost, NewCost, ChangedAt)
                    VALUES ($sku, $oldCost, $newCost, $changedAt)
                    """;
                historyCommand.Parameters.AddWithValue("$sku", item.Sku);
                historyCommand.Parameters.AddWithValue("$oldCost", existing.CostPrice);
                historyCommand.Parameters.AddWithValue("$newCost", item.CostPrice);
                historyCommand.Parameters.AddWithValue("$changedAt", DateTime.Now);
                historyCommand.ExecuteNonQuery();
            }

            using var upsertCommand = connection.CreateCommand();
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandText = """
                INSERT INTO ItemTable (Sku, ItemName, CostPrice, Reserve1, Reserve2, Reserve3, ProductGroup)
                VALUES ($sku, $itemName, $costPrice, $reserve1, $reserve2, $reserve3, $productGroup)
                ON CONFLICT(Sku) DO UPDATE SET
                    ItemName = excluded.ItemName,
                    CostPrice = excluded.CostPrice,
                    Reserve1 = excluded.Reserve1,
                    Reserve2 = excluded.Reserve2,
                    Reserve3 = excluded.Reserve3,
                    ProductGroup = excluded.ProductGroup
                """;
            upsertCommand.Parameters.AddWithValue("$sku", item.Sku);
            upsertCommand.Parameters.AddWithValue("$itemName", item.ItemName);
            upsertCommand.Parameters.AddWithValue("$costPrice", item.CostPrice);
            upsertCommand.Parameters.AddWithValue("$reserve1", (object?)item.Reserve1 ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("$reserve2", (object?)item.Reserve2 ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("$reserve3", (object?)item.Reserve3 ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("$productGroup", (object?)item.ProductGroup ?? DBNull.Value);
            upsertCommand.ExecuteNonQuery();

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void Delete(string sku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            // 1. 원가 변경 이력을 먼저 삭제합니다.
            using var historyCommand = connection.CreateCommand();
            historyCommand.Transaction = transaction;
            historyCommand.CommandText = "DELETE FROM ItemCostHistory WHERE Sku = $sku";
            historyCommand.Parameters.AddWithValue("$sku", sku);
            historyCommand.ExecuteNonQuery();

            // 2. 마스터 품목을 삭제합니다.
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM ItemTable WHERE Sku = $sku";
            deleteCommand.Parameters.AddWithValue("$sku", sku);
            deleteCommand.ExecuteNonQuery();

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public ItemModel? GetBySku(string sku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        return GetBySku(connection, sku);
    }

    public List<ItemModel> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Sku, ItemName, CostPrice, Reserve1, Reserve2, Reserve3, ProductGroup FROM ItemTable";

        var items = new List<ItemModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(ReadItem(reader));
        }
        return items;
    }

    public List<ItemCostHistory> GetCostHistory(string sku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Sku, OldCost, NewCost, ChangedAt
            FROM ItemCostHistory
            WHERE Sku = $sku
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("$sku", sku);

        var history = new List<ItemCostHistory>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            history.Add(new ItemCostHistory
            {
                Id = reader.GetInt64(0),
                Sku = reader.GetString(1),
                OldCost = reader.GetDecimal(2),
                NewCost = reader.GetDecimal(3),
                ChangedAt = reader.GetDateTime(4),
            });
        }
        return history;
    }

    private static ItemModel? GetBySku(SqliteConnection connection, string sku)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Sku, ItemName, CostPrice, Reserve1, Reserve2, Reserve3, ProductGroup FROM ItemTable WHERE Sku = $sku";
        command.Parameters.AddWithValue("$sku", sku);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadItem(reader) : null;
    }

    private static ItemModel ReadItem(SqliteDataReader reader) => new()
    {
        Sku = reader.GetString(0),
        ItemName = reader.GetString(1),
        CostPrice = reader.GetDecimal(2),
        Reserve1 = reader.IsDBNull(3) ? null : reader.GetString(3),
        Reserve2 = reader.IsDBNull(4) ? null : reader.GetString(4),
        Reserve3 = reader.IsDBNull(5) ? null : reader.GetString(5),
        ProductGroup = reader.IsDBNull(6) ? null : reader.GetString(6),
    };
}

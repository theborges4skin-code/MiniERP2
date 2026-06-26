using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

public class ItemRepository
{
    public void Upsert(ItemModel item)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();

        var existing = GetBySku(connection, item.Sku);
        if (existing is not null && existing.CostPrice != item.CostPrice)
        {
            using var historyCommand = connection.CreateCommand();
            historyCommand.CommandText = """
                INSERT INTO ItemCostHistory (Sku, OldCost, NewCost, ChangedAt)
                VALUES ($sku, $oldCost, $newCost, $changedAt)
                """;
            historyCommand.Parameters.AddWithValue("$sku", item.Sku);
            historyCommand.Parameters.AddWithValue("$oldCost", existing.CostPrice);
            historyCommand.Parameters.AddWithValue("$newCost", item.CostPrice);
            historyCommand.Parameters.AddWithValue("$changedAt", DateTime.UtcNow.ToString("O"));
            historyCommand.ExecuteNonQuery();
        }

        using var upsertCommand = connection.CreateCommand();
        upsertCommand.CommandText = """
            INSERT INTO ItemTable (Sku, ItemName, CostPrice)
            VALUES ($sku, $itemName, $costPrice)
            ON CONFLICT(Sku) DO UPDATE SET
                ItemName = excluded.ItemName,
                CostPrice = excluded.CostPrice
            """;
        upsertCommand.Parameters.AddWithValue("$sku", item.Sku);
        upsertCommand.Parameters.AddWithValue("$itemName", item.ItemName);
        upsertCommand.Parameters.AddWithValue("$costPrice", item.CostPrice);
        upsertCommand.ExecuteNonQuery();
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
        command.CommandText = "SELECT Sku, ItemName, CostPrice FROM ItemTable";

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
                OldCost = (decimal)reader.GetDouble(2),
                NewCost = (decimal)reader.GetDouble(3),
                ChangedAt = DateTime.Parse(reader.GetString(4)),
            });
        }
        return history;
    }

    private static ItemModel? GetBySku(SqliteConnection connection, string sku)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Sku, ItemName, CostPrice FROM ItemTable WHERE Sku = $sku";
        command.Parameters.AddWithValue("$sku", sku);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadItem(reader) : null;
    }

    private static ItemModel ReadItem(SqliteDataReader reader) => new()
    {
        Sku = reader.GetString(0),
        ItemName = reader.GetString(1),
        CostPrice = (decimal)reader.GetDouble(2),
    };
}

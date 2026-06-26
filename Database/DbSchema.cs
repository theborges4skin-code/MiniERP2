using Microsoft.Data.Sqlite;

namespace MiniERP2.Database;

public static class DbSchema
{
    public static void EnsureCreated(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ItemTable (
                Sku TEXT PRIMARY KEY,
                ItemName TEXT NOT NULL,
                CostPrice REAL NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ItemCostHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Sku TEXT NOT NULL,
                OldCost REAL NOT NULL,
                NewCost REAL NOT NULL,
                ChangedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ChannelSkuTable (
                ChannelCode TEXT NOT NULL,
                Msku TEXT NOT NULL,
                SupplyPrice REAL NOT NULL,
                PRIMARY KEY (ChannelCode, Msku)
            );

            CREATE TABLE IF NOT EXISTS ChannelSkuPriceHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Msku TEXT NOT NULL,
                OldPrice REAL NOT NULL,
                NewPrice REAL NOT NULL,
                ChangedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS CourierMasterTable (
                CourierName TEXT PRIMARY KEY,
                HeaderMappingJson TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SalesChannelTable (
                ChannelCode TEXT PRIMARY KEY,
                ChannelName TEXT NOT NULL,
                GroupName TEXT,
                IsFavorite INTEGER NOT NULL DEFAULT 0,
                DisplayOrder INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS SettlementData (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                StdProductId TEXT NOT NULL,
                Amt REAL NOT NULL,
                Settlement REAL NOT NULL,
                Shipping REAL NOT NULL,
                Fee REAL NOT NULL
            );

            CREATE TABLE IF NOT EXISTS OutboundDetailTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrderNo TEXT NOT NULL,
                TrackingNo TEXT NOT NULL,
                MskuCode TEXT NOT NULL,
                Qty INTEGER NOT NULL,
                SupplyPrice REAL NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RuleExact (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                TargetSku TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RuleCondition (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                TargetSku TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RuleTemp (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                TargetSku TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RuleException (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                TargetSku TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS MappingHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                OldSku TEXT NOT NULL,
                NewSku TEXT NOT NULL,
                MatchType TEXT NOT NULL,
                ChangedAt TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}

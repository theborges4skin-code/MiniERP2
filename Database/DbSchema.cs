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
                CostPrice REAL NOT NULL,
                Reserve1 TEXT,
                Reserve2 TEXT,
                Reserve3 TEXT,
                ProductGroup TEXT
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
                ProductName TEXT,
                OptionName TEXT,
                Msku TEXT,
                Qty INTEGER NOT NULL,
                Settlement REAL NOT NULL,
                Shipping REAL NOT NULL,
                Fee REAL NOT NULL,
                Profit REAL NOT NULL,
                Status TEXT
            );

            CREATE TABLE IF NOT EXISTS OutboundDetailTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL DEFAULT '',
                OrderNo TEXT NOT NULL,
                TrackingNo TEXT NOT NULL,
                MskuCode TEXT NOT NULL,
                Qty INTEGER NOT NULL,
                SupplyPrice REAL NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT ''
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

            CREATE TABLE IF NOT EXISTS RuleConditionDetail (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RuleId INTEGER NOT NULL,
                HeaderField TEXT NOT NULL,
                Operator TEXT NOT NULL,
                TargetValue TEXT NOT NULL,
                Logic TEXT NOT NULL
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

        // 이중 출고 방지(Upsert)를 위한 유니크 인덱스. 기존 DB에 이미 중복 데이터가 있으면
        // 인덱스 생성이 실패할 수 있으므로(과거 버그로 쌓인 중복 데이터), 앱 시작을 막지 않도록 무시한다.
        try
        {
            using var indexCommand = connection.CreateCommand();
            indexCommand.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_OutboundDetailTable_OrderNo_MskuCode ON OutboundDetailTable (OrderNo, MskuCode)";
            indexCommand.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // 기존 중복 데이터로 인덱스 생성이 실패해도 무시하고 계속 진행한다.
        }

        // CREATE TABLE IF NOT EXISTS는 이미 존재하는 테이블에 새 컬럼을 추가해주지 않으므로,
        // 이전 버전의 DB 파일에서도 신규 컬럼이 누락되지 않도록 직접 보강한다.
        EnsureColumn(connection, "ItemTable", "Reserve1", "TEXT");
        EnsureColumn(connection, "ItemTable", "Reserve2", "TEXT");
        EnsureColumn(connection, "ItemTable", "Reserve3", "TEXT");
        EnsureColumn(connection, "ItemTable", "ProductGroup", "TEXT");
        EnsureColumn(connection, "ChannelSkuTable", "InvoiceDisplayName", "TEXT");
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnType)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = checkCommand.ExecuteReader();
        while (reader.Read())
        {
            // PRAGMA table_info 결과의 두 번째 컬럼(인덱스 1)이 컬럼 이름이다.
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return; // 이미 존재함
            }
        }
        reader.Close();

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}";
        alterCommand.ExecuteNonQuery();
    }
}

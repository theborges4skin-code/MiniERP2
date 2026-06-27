using Microsoft.Data.Sqlite;

namespace MiniERP2.Database;

public static class DbSchema
{
    public static void EnsureCreated(SqliteConnection connection)
    {
        // ChannelSkuTable의 기본키를 (ChannelCode, Msku)에서 (ChannelCode, CskuCode)로 바꿔야 해서
        // (한 마스터SKU가 채널 안에서 여러 CSKU로 분화될 수 있게) ALTER로는 처리할 수 없다.
        // 아래 CREATE TABLE IF NOT EXISTS가 실행되기 전에 먼저 옛 스키마를 감지해 옮겨준다.
        MigrateChannelSkuTableToCskuCodeIfNeeded(connection);

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
                CskuCode TEXT NOT NULL,
                Msku TEXT NOT NULL,
                SupplyPrice REAL NOT NULL,
                InvoiceDisplayName TEXT,
                PRIMARY KEY (ChannelCode, CskuCode)
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
                HeaderMappingJson TEXT NOT NULL,
                TrackingImportHeaderRow INTEGER NOT NULL DEFAULT 1,
                TrackingImportRecipientHeader TEXT NOT NULL DEFAULT '',
                TrackingImportTrackingNoHeader TEXT NOT NULL DEFAULT '',
                QuantityNotationFormat TEXT NOT NULL DEFAULT ''
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
                CreatedAt TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT '발송대기',
                ConfirmedAt TEXT,
                Recipient TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',
                ProductName TEXT NOT NULL DEFAULT ''
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
        EnsureColumn(connection, "OutboundDetailTable", "Status", "TEXT NOT NULL DEFAULT '발송대기'");
        EnsureColumn(connection, "OutboundDetailTable", "ConfirmedAt", "TEXT");
        EnsureColumn(connection, "OutboundDetailTable", "Recipient", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "OutboundDetailTable", "Address", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "OutboundDetailTable", "ProductName", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportHeaderRow", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportRecipientHeader", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportTrackingNoHeader", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "CourierMasterTable", "QuantityNotationFormat", "TEXT NOT NULL DEFAULT ''");

        // 발주확정/출고확정 용어로 바뀌기 전에 저장된 옛 상태값("발송대기"/"발송완료")이 남아있으면
        // 발주/출고 이력 관리창의 상태 콤보(두 값만 허용)에서 DataGridViewComboBoxCell 오류가 난다.
        // 기동 시마다 실행해도 안전한 정규화(이미 새 값이면 매치 없음 → no-op)이다.
        NormalizeLegacyOutboundStatus(connection);
    }

    private static void NormalizeLegacyOutboundStatus(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OutboundDetailTable SET Status = '발주확정' WHERE Status = '발송대기';
            UPDATE OutboundDetailTable SET Status = '출고확정' WHERE Status = '발송완료';
            """;
        command.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnType)
    {
        if (HasColumn(connection, tableName, columnName)) return;

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}";
        alterCommand.ExecuteNonQuery();
    }

    private static bool HasColumn(SqliteConnection connection, string tableName, string columnName)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = checkCommand.ExecuteReader();
        while (reader.Read())
        {
            // PRAGMA table_info 결과의 두 번째 컬럼(인덱스 1)이 컬럼 이름이다.
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 옛 버전의 ChannelSkuTable(기본키 ChannelCode+Msku)을 새 스키마(기본키 ChannelCode+CskuCode)로
    /// 옮긴다. SQLite는 기존 테이블의 기본키를 ALTER로 바꿀 수 없어 이름 변경 → 새 테이블 생성 →
    /// 데이터 복사(CskuCode는 옛 Msku 값을 그대로 사용) → 옛 테이블 삭제 순으로 처리한다.
    /// 신규 DB(테이블이 아예 없음)거나 이미 마이그레이션된 DB는 아무 일도 하지 않는다.
    /// </summary>
    private static void MigrateChannelSkuTableToCskuCodeIfNeeded(SqliteConnection connection)
    {
        using var checkExistsCommand = connection.CreateCommand();
        checkExistsCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ChannelSkuTable'";
        if (checkExistsCommand.ExecuteScalar() == null) return;

        if (HasColumn(connection, "ChannelSkuTable", "CskuCode")) return;

        var hasInvoiceDisplayName = HasColumn(connection, "ChannelSkuTable", "InvoiceDisplayName");

        using var renameCommand = connection.CreateCommand();
        renameCommand.CommandText = "ALTER TABLE ChannelSkuTable RENAME TO ChannelSkuTable_Legacy";
        renameCommand.ExecuteNonQuery();

        using var createCommand = connection.CreateCommand();
        createCommand.CommandText = """
            CREATE TABLE ChannelSkuTable (
                ChannelCode TEXT NOT NULL,
                CskuCode TEXT NOT NULL,
                Msku TEXT NOT NULL,
                SupplyPrice REAL NOT NULL,
                InvoiceDisplayName TEXT,
                PRIMARY KEY (ChannelCode, CskuCode)
            )
            """;
        createCommand.ExecuteNonQuery();

        using var copyCommand = connection.CreateCommand();
        copyCommand.CommandText = hasInvoiceDisplayName
            ? "INSERT INTO ChannelSkuTable (ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName) SELECT ChannelCode, Msku, Msku, SupplyPrice, InvoiceDisplayName FROM ChannelSkuTable_Legacy"
            : "INSERT INTO ChannelSkuTable (ChannelCode, CskuCode, Msku, SupplyPrice) SELECT ChannelCode, Msku, Msku, SupplyPrice FROM ChannelSkuTable_Legacy";
        copyCommand.ExecuteNonQuery();

        using var dropCommand = connection.CreateCommand();
        dropCommand.CommandText = "DROP TABLE ChannelSkuTable_Legacy";
        dropCommand.ExecuteNonQuery();
    }
}

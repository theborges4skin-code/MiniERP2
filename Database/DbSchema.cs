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

            CREATE TABLE IF NOT EXISTS ChannelSkuFieldHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                CskuCode TEXT NOT NULL,
                FieldName TEXT NOT NULL,
                OldValue TEXT,
                NewValue TEXT,
                ChangedAt TEXT NOT NULL
            );

            -- B2B 견적관리(매입측): 채널(매입처)별 마스터SKU 매입가. 매출측 ChannelSkuTable과 대칭 구조.
            CREATE TABLE IF NOT EXISTS PurchaseSkuTable (
                ChannelCode TEXT NOT NULL,
                Msku TEXT NOT NULL,
                PurchasePrice REAL NOT NULL,
                Unit TEXT NOT NULL DEFAULT 'kg',
                Note TEXT,
                UpdatedAt TEXT,
                PRIMARY KEY (ChannelCode, Msku)
            );

            CREATE TABLE IF NOT EXISTS PurchaseSkuPriceHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Msku TEXT NOT NULL,
                OldPrice REAL NOT NULL,
                NewPrice REAL NOT NULL,
                ChangedAt TEXT NOT NULL,
                Reason TEXT,
                Note TEXT
            );

            -- B2B 견적관리: 발송헤더(ShipmentGroupKey) 1건 = 그 발송에 속한 모든 출고 라인이 공통으로
            -- 나누는 실운임. OutboundDetailTable.ShipmentGroupKey와 같은 값으로 연결한다.
            CREATE TABLE IF NOT EXISTS OutboundShipmentTable (
                ShipmentGroupKey TEXT PRIMARY KEY,
                FreightCost REAL NOT NULL DEFAULT 0,
                ShippedAt TEXT,
                Note TEXT
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
                ShipmentGroupKey TEXT NOT NULL DEFAULT '',
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
                TargetSku TEXT NOT NULL,
                TargetMsku TEXT NOT NULL DEFAULT ''
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

            CREATE TABLE IF NOT EXISTS ExportLogTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ExportedAt TEXT NOT NULL,
                TableName TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                RowCount INTEGER NOT NULL,
                Headers TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdRuleTemp (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                TargetGroup TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdRuleCondition (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                TargetGroup TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdRuleConditionDetail (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RuleId INTEGER NOT NULL,
                HeaderField TEXT NOT NULL,
                Operator TEXT NOT NULL,
                TargetValue TEXT NOT NULL,
                Logic TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdRuleException (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                HeaderField TEXT NOT NULL,
                Operator TEXT NOT NULL,
                TargetValue TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ClosingRun (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FolderPath TEXT NOT NULL,
                Period TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'draft',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ClosingStagedFile (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId INTEGER NOT NULL,
                ChannelCode TEXT NOT NULL,
                ChannelName TEXT NOT NULL DEFAULT '',
                SourceType TEXT NOT NULL DEFAULT 'settlement',
                OriginalPath TEXT NOT NULL,
                FileCreatedAt TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'pending',
                RowCount INTEGER NOT NULL DEFAULT 0,
                UnmappedCount INTEGER NOT NULL DEFAULT 0,
                ErrorMessage TEXT
            );

            CREATE TABLE IF NOT EXISTS ClosingUnmapped (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId INTEGER NOT NULL,
                ChannelCode TEXT NOT NULL,
                SourceKey TEXT NOT NULL,
                OccurrenceCount INTEGER NOT NULL DEFAULT 1,
                SampleAmount REAL NOT NULL DEFAULT 0,
                MappedSku TEXT
            );

            CREATE TABLE IF NOT EXISTS ProfitFactTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Period TEXT NOT NULL,
                ChannelCode TEXT NOT NULL,
                ChannelName TEXT NOT NULL DEFAULT '',
                ProductGroup TEXT NOT NULL,
                Qty INTEGER NOT NULL DEFAULT 0,
                Revenue REAL NOT NULL DEFAULT 0,
                GrossProfit REAL NOT NULL DEFAULT 0,
                SavedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdFactTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Period TEXT NOT NULL,
                ChannelCode TEXT NOT NULL,
                ChannelName TEXT NOT NULL DEFAULT '',
                ProductGroup TEXT NOT NULL,
                AdCost REAL NOT NULL DEFAULT 0,
                SavedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ExportSummaryDraftEntry (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MarketCode TEXT NOT NULL,
                YearMonth TEXT NOT NULL,
                Indicator TEXT NOT NULL,
                Currency TEXT NOT NULL DEFAULT '',
                Amount REAL NOT NULL DEFAULT 0,
                SavedAt TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS DocFavoritePhraseTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL DEFAULT '',
                Body TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT '일반',
                IsFavorite INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS DocPartyTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileName TEXT NOT NULL DEFAULT '',
                RegNo TEXT NOT NULL DEFAULT '',
                CompanyName TEXT NOT NULL DEFAULT '',
                CeoName TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',
                BizType TEXT NOT NULL DEFAULT '',
                BizItem TEXT NOT NULL DEFAULT '',
                Tel TEXT NOT NULL DEFAULT '',
                Email TEXT NOT NULL DEFAULT '',
                IsDefaultSupplier INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS DocStatementTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PartyId INTEGER NOT NULL,
                IssueDate TEXT,
                IssueYearMonth TEXT NOT NULL DEFAULT '',
                TotalSupply REAL NOT NULL DEFAULT 0,
                TotalTax REAL NOT NULL DEFAULT 0,
                TotalAmount REAL NOT NULL DEFAULT 0,
                TotalQty REAL NOT NULL DEFAULT 0,
                CarryoverBalance REAL NOT NULL DEFAULT 0,
                ReconcileNote TEXT NOT NULL DEFAULT '',
                TemplateSignature TEXT NOT NULL DEFAULT '',
                StatusFlags TEXT NOT NULL DEFAULT '',
                SourceFileName TEXT NOT NULL DEFAULT '',
                SourceSheetName TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT '',
                UNIQUE(SourceFileName, SourceSheetName)
            );

            CREATE TABLE IF NOT EXISTS DocStatementLineTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StatementId INTEGER NOT NULL,
                RowNo INTEGER NOT NULL DEFAULT 0,
                LineDate TEXT,
                ItemName TEXT NOT NULL DEFAULT '',
                Spec TEXT NOT NULL DEFAULT '',
                Qty REAL NOT NULL DEFAULT 0,
                UnitPrice REAL NOT NULL DEFAULT 0,
                UnitPriceVatIncluded INTEGER NOT NULL DEFAULT 0,
                SupplyAmount REAL NOT NULL DEFAULT 0,
                Tax REAL NOT NULL DEFAULT 0,
                Total REAL NOT NULL DEFAULT 0,
                Note TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS DocHistoryTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DocType TEXT NOT NULL DEFAULT '',
                IssueDate TEXT NOT NULL DEFAULT '',
                BuyerName TEXT NOT NULL DEFAULT '',
                TotalAmount REAL NOT NULL DEFAULT 0,
                FilePath TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS FboCskuMaster (
                Csku TEXT PRIMARY KEY,
                FboItemCode TEXT NOT NULL DEFAULT '',
                ItemName TEXT NOT NULL DEFAULT '',
                QtyPerBox INTEGER NOT NULL DEFAULT 0,
                BoxType TEXT NOT NULL DEFAULT '소',
                FreightType TEXT,
                IsActive INTEGER NOT NULL DEFAULT 1,
                UpdatedAt TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS FboChannelConfig (
                ChannelId TEXT PRIMARY KEY,
                ChannelName TEXT NOT NULL DEFAULT '',
                ReceiverName TEXT NOT NULL DEFAULT '',
                Phone TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',
                ReceiverSeqFormat TEXT NOT NULL DEFAULT '{name}{seq:00}',
                ChannelLabel TEXT NOT NULL DEFAULT '',
                OrderNoPrefix TEXT NOT NULL DEFAULT '#FBO',
                InboundType TEXT NOT NULL DEFAULT '31',
                IsDefault INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS FboOrder (
                FboNo TEXT PRIMARY KEY,
                OrderDate TEXT NOT NULL,
                ChannelId TEXT NOT NULL,
                ReceiverName TEXT NOT NULL DEFAULT '',
                Phone TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT '작성중',
                Memo TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT '',
                UpdatedAt TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS FboBox (
                FboNo TEXT NOT NULL,
                BoxSeq INTEGER NOT NULL,
                ReceiverDisplayName TEXT NOT NULL DEFAULT '',
                MatchKey TEXT NOT NULL DEFAULT '',
                BoxType TEXT NOT NULL DEFAULT '소',
                TrackingNo TEXT,
                TrackingLoadedAt TEXT,
                Status TEXT NOT NULL DEFAULT '대기',
                PRIMARY KEY (FboNo, BoxSeq)
            );

            CREATE TABLE IF NOT EXISTS FboBoxItem (
                FboNo TEXT NOT NULL,
                BoxSeq INTEGER NOT NULL,
                ItemSeq INTEGER NOT NULL,
                Csku TEXT NOT NULL,
                FboItemCode TEXT NOT NULL DEFAULT '',
                ItemName TEXT NOT NULL DEFAULT '',
                QtyPerBox INTEGER NOT NULL DEFAULT 0,
                Qty INTEGER NOT NULL DEFAULT 0,
                ExpiryDate TEXT,
                PRIMARY KEY (FboNo, BoxSeq, ItemSeq)
            );

            CREATE INDEX IF NOT EXISTS IX_FboBox_MatchKey ON FboBox (MatchKey);

            -- 견적/가격 기록 관리(견적기록관리_개발기획서_확정본.md §3.1~3.2): 견적 1건 = 1회 전달
            -- 단위(헤더) + 품목별 라인. 기존 ChannelSkuPriceHistory/PurchaseSkuPriceHistory는 Upsert가
            -- 만드는 감사로그라 "이번 달 단가표 1건 = N품목" 업무 단위를 담지 못해서 별도로 신설한다.
            CREATE TABLE IF NOT EXISTS PriceQuoteTable (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                QuoteNo         TEXT NOT NULL DEFAULT '',
                ChannelCode     TEXT NOT NULL,
                PriceKind       TEXT NOT NULL DEFAULT 'Supply',
                QuoteFormType   TEXT NOT NULL DEFAULT 'UnitOnly',
                Origin          TEXT NOT NULL DEFAULT 'Manual',
                Title           TEXT NOT NULL DEFAULT '',
                QuoteDate       TEXT NOT NULL DEFAULT '',
                EffectiveFrom   TEXT NOT NULL DEFAULT '',
                EffectiveTo     TEXT,
                AutoApply       INTEGER NOT NULL DEFAULT 0,
                Status          TEXT NOT NULL DEFAULT 'Draft',
                DeliveryMethod  TEXT NOT NULL DEFAULT '',
                DeliveredAt     TEXT,
                DeliveredTo     TEXT NOT NULL DEFAULT '',
                Note            TEXT NOT NULL DEFAULT '',
                PriceBasis      TEXT NOT NULL DEFAULT 'VatExcl',
                RootQuoteId     INTEGER,
                RevisionNo      INTEGER NOT NULL DEFAULT 0,
                SupersededBy    INTEGER,
                RevisionReason  TEXT NOT NULL DEFAULT '',
                CreatedAt       TEXT NOT NULL DEFAULT '',
                UpdatedAt       TEXT NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS IX_PriceQuote_Channel_Eff ON PriceQuoteTable (ChannelCode, EffectiveFrom);
            CREATE INDEX IF NOT EXISTS IX_PriceQuote_Revision ON PriceQuoteTable (RootQuoteId, RevisionNo);
            CREATE INDEX IF NOT EXISTS IX_PriceQuote_Origin ON PriceQuoteTable (ChannelCode, Origin, Status);
            CREATE UNIQUE INDEX IF NOT EXISTS UX_PriceQuote_QuoteNo ON PriceQuoteTable (QuoteNo);

            CREATE TABLE IF NOT EXISTS PriceQuoteLineTable (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                QuoteId       INTEGER NOT NULL,
                RowNo         INTEGER NOT NULL DEFAULT 0,
                CskuCode      TEXT NOT NULL DEFAULT '',
                Msku          TEXT NOT NULL DEFAULT '',
                ItemNameSnap  TEXT NOT NULL DEFAULT '',
                Spec          TEXT NOT NULL DEFAULT '',
                Unit          TEXT NOT NULL DEFAULT 'EA',
                Qty           REAL NOT NULL DEFAULT 0,
                OldPrice      REAL,
                NewPrice      REAL NOT NULL DEFAULT 0,
                SupplyAmount  REAL NOT NULL DEFAULT 0,
                Tax           REAL NOT NULL DEFAULT 0,
                Total         REAL NOT NULL DEFAULT 0,
                ChangeReason  TEXT NOT NULL DEFAULT '',
                Note          TEXT NOT NULL DEFAULT '',
                IsApplied     INTEGER NOT NULL DEFAULT 0,
                PromotedFrom  INTEGER
            );

            CREATE INDEX IF NOT EXISTS IX_PriceQuoteLine_Quote_Row ON PriceQuoteLineTable (QuoteId, RowNo);
            CREATE INDEX IF NOT EXISTS IX_PriceQuoteLine_Csku ON PriceQuoteLineTable (CskuCode);
            CREATE INDEX IF NOT EXISTS IX_PriceQuoteLine_Msku ON PriceQuoteLineTable (Msku);
            """;
        command.ExecuteNonQuery();

        // RuleCondition에 TargetMsku 추가 — Settlement 전용 규칙(CSKU 없이 MSKU만 매핑)에 사용한다.
        EnsureColumn(connection, "RuleCondition", "TargetMsku", "TEXT NOT NULL DEFAULT ''");

        // 이중 출고 방지(Upsert) 유니크 인덱스.
        // 분리배송(ShipmentGroupId가 다른 동일 OrderNo) 지원을 위해 ShipmentGroupKey 기준으로 교체한다.
        // 기존 DB는 ShipmentGroupKey=''이므로, 먼저 OrderNo로 채운 뒤 인덱스를 전환한다.
        EnsureColumn(connection, "OutboundDetailTable", "ShipmentGroupKey", "TEXT NOT NULL DEFAULT ''");
        try
        {
            using var fillCmd = connection.CreateCommand();
            fillCmd.CommandText = "UPDATE OutboundDetailTable SET ShipmentGroupKey = OrderNo WHERE ShipmentGroupKey = ''";
            fillCmd.ExecuteNonQuery();

            using var dropCmd = connection.CreateCommand();
            dropCmd.CommandText = "DROP INDEX IF EXISTS IX_OutboundDetailTable_OrderNo_MskuCode";
            dropCmd.ExecuteNonQuery();

            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_OutboundDetailTable_ShipmentGroupKey_MskuCode ON OutboundDetailTable (ShipmentGroupKey, MskuCode)";
            createCmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // 기존 중복 데이터로 인덱스 전환이 실패해도 무시하고 계속 진행한다.
        }

        // CREATE TABLE IF NOT EXISTS는 이미 존재하는 테이블에 새 컬럼을 추가해주지 않으므로,
        // 이전 버전의 DB 파일에서도 신규 컬럼이 누락되지 않도록 직접 보강한다.
        EnsureColumn(connection, "ItemTable", "Reserve1", "TEXT");
        EnsureColumn(connection, "ItemTable", "Reserve2", "TEXT");
        EnsureColumn(connection, "ItemTable", "Reserve3", "TEXT");
        EnsureColumn(connection, "ItemTable", "ProductGroup", "TEXT");
        EnsureColumn(connection, "ChannelSkuTable", "InvoiceDisplayName", "TEXT");
        EnsureColumn(connection, "ChannelSkuTable", "Note", "TEXT");
        EnsureColumn(connection, "ChannelSkuTable", "UpdatedAt", "TEXT");
        EnsureColumn(connection, "ChannelSkuTable", "Unit", "TEXT NOT NULL DEFAULT 'kg'");
        EnsureColumn(connection, "ChannelSkuTable", "Packing", "TEXT");
        EnsureColumn(connection, "ChannelSkuPriceHistory", "Reason", "TEXT");
        EnsureColumn(connection, "ChannelSkuPriceHistory", "Note", "TEXT");
        EnsureColumn(connection, "ItemTable", "Unit", "TEXT NOT NULL DEFAULT 'kg'");
        EnsureColumn(connection, "OutboundDetailTable", "Status", "TEXT NOT NULL DEFAULT '발송대기'");
        EnsureColumn(connection, "OutboundDetailTable", "ConfirmedAt", "TEXT");
        EnsureColumn(connection, "OutboundDetailTable", "Recipient", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "OutboundDetailTable", "Address", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "OutboundDetailTable", "ProductName", "TEXT NOT NULL DEFAULT ''");
        // B2B 견적관리(§2.8): 판매 출고 라인에 매입처/원가 스냅샷/중량을 붙인다. CSKU는 별도 컬럼을
        // 두지 않는다 — MskuCode가 이름과 달리 이미 CSKU 코드를 저장하고 있다(OfsOrderItem.MappedSku).
        EnsureColumn(connection, "OutboundDetailTable", "PurchaseChannelCode", "TEXT");
        EnsureColumn(connection, "OutboundDetailTable", "PurchasePrice", "REAL");
        EnsureColumn(connection, "OutboundDetailTable", "WeightKg", "REAL");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportHeaderRow", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportRecipientHeader", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportTrackingNoHeader", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "CourierMasterTable", "QuantityNotationFormat", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "SalesChannelTable", "LastUsedDate", "TEXT");
        // B2B 견적관리(§2.1): 한 채널이 매입·매출을 동시에 겸할 수 있어 별도 VendorTable 대신 플래그로
        // 구분한다. 기존 채널은 전부 판매 채널이었으므로 IsSales 기본값 1, IsPurchase 기본값 0.
        EnsureColumn(connection, "SalesChannelTable", "IsPurchase", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "SalesChannelTable", "IsSales", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "DocPartyTable", "ChannelCode", "TEXT");
        EnsureColumn(connection, "DocPartyTable", "IsActive", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "DocPartyTable", "CreatedAt", "TEXT");
        EnsureColumn(connection, "DocPartyTable", "StampImagePath", "TEXT");
        // FBO 발주처리 — 하배출고이서(택배사 출력양식)용 품목 표시명을 내부 관리용 ItemName과
        // 분리한다(OFS ChannelSkuTable.InvoiceDisplayName과 같은 목적). FboBoxItem은 저장 시점의
        // 스냅샷을 갖는다.
        EnsureColumn(connection, "FboCskuMaster", "InvoiceDisplayName", "TEXT");
        EnsureColumn(connection, "FboBoxItem", "InvoiceDisplayName", "TEXT");

        // 견적/가격 기록 관리(견적기록관리_개발기획서_확정본.md §3.3~3.4, Step 1).
        EnsureColumn(connection, "ChannelSkuPriceHistory", "QuoteId", "INTEGER");
        EnsureColumn(connection, "PurchaseSkuTable", "IsPrimary", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "PurchaseSkuPriceHistory", "QuoteId", "INTEGER");
        EnsureColumn(connection, "SalesChannelTable", "AutoQuoteDraft", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "DocHistoryTable", "SourceQuoteId", "INTEGER");
        // OutboundDetailTable.CskuCode(G9 해소) — MskuCode는 이름과 달리 이미 CSKU 코드를 저장하고
        // 있어(§7.0) 당장 필수는 아니지만, §7.1 실적 조회에서 CSKU 기준 명시적 조회가 필요해 추가한다.
        EnsureColumn(connection, "OutboundDetailTable", "CskuCode", "TEXT NOT NULL DEFAULT ''");

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

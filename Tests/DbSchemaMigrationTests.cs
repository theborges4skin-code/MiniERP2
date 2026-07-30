using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

/// <summary>
/// CREATE TABLE IF NOT EXISTS는 이미 존재하는 테이블에 새 컬럼을 추가해주지 않는다.
/// 구버전 스키마로 만들어진 DB 파일을 열었을 때도 신규 컬럼이 자동으로 보강되는지 검증한다.
/// </summary>
[TestClass]
public class DbSchemaMigrationTests
{
    private string _testFolder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    [TestMethod]
    public void EnsureCreated_OnLegacyItemTable_AddsMissingColumnsWithoutError()
    {
        // 구버전 스키마(Reserve1~3, ProductGroup 컬럼이 없는 ItemTable)를 직접 만든다.
        using (var legacyConnection = new SqliteConnection($"Data Source={PathProvider.DatabaseFilePath}"))
        {
            legacyConnection.Open();
            using var command = legacyConnection.CreateCommand();
            command.CommandText = """
                CREATE TABLE ItemTable (
                    Sku TEXT PRIMARY KEY,
                    ItemName TEXT NOT NULL,
                    CostPrice REAL NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        // SqliteConnectionFactory.OpenConnection()이 호출할 때마다 DbSchema.EnsureCreated가 실행된다.
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "SKU-1", ItemName = "마이그레이션테스트", CostPrice = 100m, ProductGroup = "그룹A" });

        var saved = repository.GetBySku("SKU-1");

        Assert.IsNotNull(saved);
        Assert.AreEqual("그룹A", saved.ProductGroup);
    }

    /// <summary>
    /// 구버전 ChannelSkuTable(기본키 ChannelCode+Msku, CskuCode 컬럼 없음)을 가진 DB를 열었을 때,
    /// 기존 데이터를 잃지 않고 새 스키마(기본키 ChannelCode+CskuCode)로 옮겨지는지 검증한다.
    /// 기존 행은 CskuCode = 옛 Msku 값으로 채워져, 매핑 규칙의 TargetSku가 그대로 유효해야 한다.
    /// </summary>
    [TestMethod]
    public void EnsureCreated_OnLegacyChannelSkuTable_MigratesDataToCskuCodeSchema()
    {
        using (var legacyConnection = new SqliteConnection($"Data Source={PathProvider.DatabaseFilePath}"))
        {
            legacyConnection.Open();
            using var command = legacyConnection.CreateCommand();
            command.CommandText = """
                CREATE TABLE ChannelSkuTable (
                    ChannelCode TEXT NOT NULL,
                    Msku TEXT NOT NULL,
                    SupplyPrice REAL NOT NULL,
                    InvoiceDisplayName TEXT,
                    PRIMARY KEY (ChannelCode, Msku)
                );
                INSERT INTO ChannelSkuTable (ChannelCode, Msku, SupplyPrice, InvoiceDisplayName)
                VALUES ('CH01', 'SKU-LEGACY', 5000, '레거시 표시명');
                """;
            command.ExecuteNonQuery();
        }

        var repository = new ChannelSkuRepository();

        // 옛 데이터는 CskuCode = 옛 Msku 값으로 그대로 조회되어야 한다.
        var migrated = repository.GetByChannelAndCskuCode("CH01", "SKU-LEGACY");
        Assert.IsNotNull(migrated);
        Assert.AreEqual("SKU-LEGACY", migrated.Msku);
        Assert.AreEqual(5000m, migrated.SupplyPrice);
        Assert.AreEqual("레거시 표시명", migrated.InvoiceDisplayName);

        // 마이그레이션 이후로는 같은 마스터SKU에 다른 CskuCode를 추가할 수 있어야 한다(옵션 분화).
        repository.Upsert(new ChannelSkuModel { ChannelCode = "CH01", CskuCode = "SKU-LEGACY_2", Msku = "SKU-LEGACY", SupplyPrice = 5500m });
        var secondOption = repository.GetByChannelAndCskuCode("CH01", "SKU-LEGACY_2");
        Assert.IsNotNull(secondOption);
        Assert.AreEqual("SKU-LEGACY", secondOption.Msku);
    }

    /// <summary>
    /// 발주확정/출고확정 용어로 바뀌기 전("발송대기"/"발송완료")에 저장된 OutboundDetailTable 데이터가
    /// 있으면, 발주/출고 이력 관리창의 상태 콤보(두 값만 허용)에서 DataGridViewComboBoxCell 오류가
    /// 났다. 기동 시 자동으로 새 용어로 정규화되는지 검증한다.
    /// </summary>
    [TestMethod]
    public void EnsureCreated_OnLegacyOutboundStatus_NormalizesToCurrentTerminology()
    {
        using (var legacyConnection = new SqliteConnection($"Data Source={PathProvider.DatabaseFilePath}"))
        {
            legacyConnection.Open();
            using var command = legacyConnection.CreateCommand();
            command.CommandText = """
                CREATE TABLE OutboundDetailTable (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ChannelCode TEXT NOT NULL DEFAULT '',
                    OrderNo TEXT NOT NULL,
                    TrackingNo TEXT NOT NULL,
                    MskuCode TEXT NOT NULL,
                    Qty INTEGER NOT NULL,
                    SupplyPrice REAL NOT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT '발송대기'
                );
                INSERT INTO OutboundDetailTable (OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status)
                VALUES ('ORDER-LEGACY-1', '', 'SKU-1', 1, 1000, '2024-01-01', '발송대기');
                INSERT INTO OutboundDetailTable (OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status)
                VALUES ('ORDER-LEGACY-2', 'T001', 'SKU-1', 1, 1000, '2024-01-01', '발송완료');
                """;
            command.ExecuteNonQuery();
        }

        var repository = new OutboundRepository();
        var results = repository.GetHistory(null, new DateTime(2023, 1, 1), DateTime.Now);

        Assert.AreEqual("발주확정", results.Single(r => r.OrderNo == "ORDER-LEGACY-1").Status);
        Assert.AreEqual("출고확정", results.Single(r => r.OrderNo == "ORDER-LEGACY-2").Status);
    }

    /// <summary>
    /// 거래처 마감보드(거래처마감보드_개발기획서.md §5.4) 도입 전 스키마(ClosingPeriod/ChannelCode/
    /// Period 컬럼 없음)의 OutboundDetailTable·DocHistoryTable을 열었을 때, 기존 데이터를 잃지 않고
    /// 신규 컬럼이 기본값('')으로 보강되는지 검증한다.
    /// </summary>
    [TestMethod]
    public void EnsureCreated_OnLegacyOutboundAndDocHistoryTables_AddsPartnerClosingColumns()
    {
        using (var legacyConnection = new SqliteConnection($"Data Source={PathProvider.DatabaseFilePath}"))
        {
            legacyConnection.Open();
            using var command = legacyConnection.CreateCommand();
            command.CommandText = """
                CREATE TABLE OutboundDetailTable (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ChannelCode TEXT NOT NULL DEFAULT '',
                    OrderNo TEXT NOT NULL,
                    TrackingNo TEXT NOT NULL,
                    MskuCode TEXT NOT NULL,
                    Qty INTEGER NOT NULL,
                    SupplyPrice REAL NOT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT '발주확정'
                );
                INSERT INTO OutboundDetailTable (ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status)
                VALUES ('CH01', 'ORDER-LEGACY-1', '', 'SKU-1', 1, 1000, '2024-01-01', '발주확정');

                CREATE TABLE DocHistoryTable (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DocType TEXT NOT NULL DEFAULT '',
                    IssueDate TEXT NOT NULL DEFAULT '',
                    BuyerName TEXT NOT NULL DEFAULT '',
                    TotalAmount REAL NOT NULL DEFAULT 0,
                    FilePath TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL DEFAULT ''
                );
                INSERT INTO DocHistoryTable (DocType, IssueDate, BuyerName, TotalAmount, FilePath, CreatedAt)
                VALUES ('TradeStatementVatExcl', '2024-01-01', '레거시거래처', 10000, 'C:\\legacy.xlsx', '2024-01-01 00:00:00');
                """;
            command.ExecuteNonQuery();
        }

        var outboundRepo = new OutboundRepository();
        var outboundResults = outboundRepo.GetHistory(null, new DateTime(2023, 1, 1), DateTime.Now);
        Assert.AreEqual("", outboundResults.Single(r => r.OrderNo == "ORDER-LEGACY-1").ClosingPeriod);

        var docHistoryRepo = new DocHistoryRepository();
        var docResults = docHistoryRepo.Query(new DateTime(2023, 1, 1), DateTime.Now);
        var legacyDoc = docResults.Single(d => d.BuyerName == "레거시거래처");
        Assert.AreEqual("", legacyDoc.ChannelCode);
        Assert.AreEqual("", legacyDoc.Period);
    }

    /// <summary>
    /// B2B 견적관리(§2.1) 도입 전 스키마(IsPurchase/IsSales 컬럼 없음)의 SalesChannelTable을 열었을 때
    /// 기존 채널 데이터를 잃지 않고, 신규 컬럼이 기본값(IsSales=1/IsPurchase=0)으로 보강되는지 검증한다.
    /// </summary>
    [TestMethod]
    public void EnsureCreated_OnLegacySalesChannelTable_AddsPurchaseSalesFlagsWithSalesDefault()
    {
        using (var legacyConnection = new SqliteConnection($"Data Source={PathProvider.DatabaseFilePath}"))
        {
            legacyConnection.Open();
            using var command = legacyConnection.CreateCommand();
            command.CommandText = """
                CREATE TABLE SalesChannelTable (
                    ChannelCode TEXT PRIMARY KEY,
                    ChannelName TEXT NOT NULL,
                    GroupName TEXT,
                    IsFavorite INTEGER NOT NULL DEFAULT 0,
                    DisplayOrder INTEGER NOT NULL DEFAULT 0
                );
                INSERT INTO SalesChannelTable (ChannelCode, ChannelName) VALUES ('CH01', '레거시채널');
                """;
            command.ExecuteNonQuery();
        }

        var repository = new SalesChannelRepository();
        var migrated = repository.GetAll().Single(c => c.ChannelCode == "CH01");

        Assert.IsTrue(migrated.IsSales, "기존 채널은 전부 판매 채널이었으므로 IsSales 기본값은 true여야 한다.");
        Assert.IsFalse(migrated.IsPurchase);
    }

    /// <summary>B2B 견적관리(§2.7) 도입 전 스키마(Unit 컬럼 없음)의 ItemTable이 기본값 "kg"으로 보강되는지 검증한다.</summary>
    [TestMethod]
    public void EnsureCreated_OnLegacyItemTableWithoutUnit_AddsUnitColumnWithKgDefault()
    {
        using (var legacyConnection = new SqliteConnection($"Data Source={PathProvider.DatabaseFilePath}"))
        {
            legacyConnection.Open();
            using var command = legacyConnection.CreateCommand();
            command.CommandText = """
                CREATE TABLE ItemTable (
                    Sku TEXT PRIMARY KEY,
                    ItemName TEXT NOT NULL,
                    CostPrice REAL NOT NULL
                );
                INSERT INTO ItemTable (Sku, ItemName, CostPrice) VALUES ('SKU-LEGACY', '레거시품목', 500);
                """;
            command.ExecuteNonQuery();
        }

        var repository = new ItemRepository();
        var migrated = repository.GetBySku("SKU-LEGACY");

        Assert.IsNotNull(migrated);
        Assert.AreEqual("kg", migrated.Unit);
    }

    /// <summary>견적기록관리_개발기획서_확정본.md Step 1 — 신규 PriceQuoteTable/PriceQuoteLineTable이 만들어지는지 검증한다.</summary>
    [TestMethod]
    public void EnsureCreated_CreatesPriceQuoteTablesWithExpectedColumns()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();

        Assert.IsTrue(TableExists(connection, "PriceQuoteTable"));
        Assert.IsTrue(TableExists(connection, "PriceQuoteLineTable"));
        Assert.IsTrue(HasColumn(connection, "PriceQuoteTable", "QuoteNo"));
        Assert.IsTrue(HasColumn(connection, "PriceQuoteTable", "RootQuoteId"));
        Assert.IsTrue(HasColumn(connection, "PriceQuoteTable", "SupersededBy"));
        Assert.IsTrue(HasColumn(connection, "PriceQuoteLineTable", "CskuCode"));
        Assert.IsTrue(HasColumn(connection, "PriceQuoteLineTable", "PromotedFrom"));
    }

    /// <summary>견적기록관리_개발기획서_확정본.md §3.3/§3.4 — 기존 테이블에 견적 연계 컬럼 4건이 보강되는지 검증한다.</summary>
    [TestMethod]
    public void EnsureCreated_AddsPriceQuoteRelatedColumnsToExistingTables()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();

        Assert.IsTrue(HasColumn(connection, "ChannelSkuPriceHistory", "QuoteId"));
        Assert.IsTrue(HasColumn(connection, "PurchaseSkuTable", "IsPrimary"));
        Assert.IsTrue(HasColumn(connection, "PurchaseSkuPriceHistory", "QuoteId"));
        Assert.IsTrue(HasColumn(connection, "SalesChannelTable", "AutoQuoteDraft"));
        Assert.IsTrue(HasColumn(connection, "DocHistoryTable", "SourceQuoteId"));
        Assert.IsTrue(HasColumn(connection, "OutboundDetailTable", "CskuCode"));
    }

    /// <summary>
    /// 문서발행 이력에서 FilePath만으로는 원본 파일이 이동/삭제되면 다시 열 수 없는 문제(사용자
    /// 신고)를 막기 위해, 발행 시점의 엑셀 바이트를 DB에도 백업해두는 FileBytes 컬럼이 보강되는지
    /// 검증한다.
    /// </summary>
    [TestMethod]
    public void EnsureCreated_AddsFileBytesColumnToDocHistoryTable()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();

        Assert.IsTrue(HasColumn(connection, "DocHistoryTable", "FileBytes"));
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$name";
        command.Parameters.AddWithValue("$name", tableName);
        return command.ExecuteScalar() != null;
    }

    private static bool HasColumn(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

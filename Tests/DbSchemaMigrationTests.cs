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
}

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
}

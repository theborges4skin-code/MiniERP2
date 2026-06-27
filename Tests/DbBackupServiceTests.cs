using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;

namespace MiniERP2.Tests;

[TestClass]
public class DbBackupServiceTests
{
    private string _testFolder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;

        // DB 파일이 실제로 존재해야 백업할 수 있다(평소엔 SqliteConnectionFactory가 처음 연결할 때 생성됨).
        new ItemRepository().GetAll();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    [TestMethod]
    public void CreateBackup_CreatesFileInBackupsFolder()
    {
        var service = new DbBackupService();

        var path = service.CreateBackup("manual");

        Assert.IsTrue(File.Exists(path));
        Assert.Contains("manual", Path.GetFileName(path));
    }

    [TestMethod]
    public void CreateBackup_KeepsOnlyLatestThree()
    {
        var service = new DbBackupService();

        for (int i = 0; i < 5; i++)
        {
            service.CreateBackup($"backup{i}");
            Thread.Sleep(10); // 파일명에 쓰이는 타임스탬프가 겹치지 않도록 한다.
        }

        Assert.HasCount(3, service.GetBackups());
    }

    [TestMethod]
    public void Restore_OverwritesCurrentDatabaseFileWithBackupContent()
    {
        var service = new DbBackupService();
        new ItemRepository().Upsert(new Models.ItemModel { Sku = "SKU-BEFORE", ItemName = "백업전", CostPrice = 100m });
        var backupPath = service.CreateBackup("snapshot");

        new ItemRepository().Upsert(new Models.ItemModel { Sku = "SKU-AFTER", ItemName = "백업후", CostPrice = 200m });
        Assert.IsNotNull(new ItemRepository().GetBySku("SKU-AFTER"));

        service.Restore(backupPath);

        Assert.IsNotNull(new ItemRepository().GetBySku("SKU-BEFORE"));
        Assert.IsNull(new ItemRepository().GetBySku("SKU-AFTER"));
    }
}

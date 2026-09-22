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
    public void CreateOrUpdateDailyBackup_OverwritesSameDayFileInsteadOfAccumulating()
    {
        var service = new DbBackupService();

        var firstPath = service.CreateOrUpdateDailyBackup();
        new ItemRepository().Upsert(new Models.ItemModel { Sku = "SKU-DAILY", ItemName = "일일백업", CostPrice = 100m });
        var secondPath = service.CreateOrUpdateDailyBackup();

        Assert.AreEqual(firstPath, secondPath);
        Assert.HasCount(1, service.GetDailyBackups());
    }

    [TestMethod]
    public void CreateOrUpdateDailyBackup_DoesNotAffectManualBackupRetention()
    {
        var service = new DbBackupService();

        for (int i = 0; i < 5; i++)
        {
            service.CreateBackup($"backup{i}");
            Thread.Sleep(10);
        }
        service.CreateOrUpdateDailyBackup();

        Assert.HasCount(3, service.GetBackups());
        Assert.HasCount(1, service.GetDailyBackups());
    }

    [TestMethod]
    public void CreateOrUpdateDailyBackup_PrunesFilesOlderThanTwoMonths()
    {
        var service = new DbBackupService();
        var backupsFolder = Path.Combine(_testFolder, "backups");
        Directory.CreateDirectory(backupsFolder);

        var oldFile = Path.Combine(backupsFolder, "ERP_DailyBackup_20250101.sqlite");
        File.WriteAllText(oldFile, "old");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddMonths(-3));

        var recentFile = Path.Combine(backupsFolder, $"ERP_DailyBackup_{DateTime.UtcNow.AddDays(-10):yyyyMMdd}.sqlite");
        File.WriteAllText(recentFile, "recent");
        File.SetLastWriteTimeUtc(recentFile, DateTime.UtcNow.AddDays(-10));

        service.CreateOrUpdateDailyBackup();

        var remaining = service.GetDailyBackups().Select(f => f.Name).ToList();
        Assert.DoesNotContain("ERP_DailyBackup_20250101.sqlite", remaining);
        Assert.Contains(Path.GetFileName(recentFile), remaining);
    }

    [TestMethod]
    public void NeedsMonthlyBackup_TrueWhenNoneExistsYet()
    {
        var service = new DbBackupService();

        Assert.IsTrue(service.NeedsMonthlyBackup());
    }

    [TestMethod]
    public void CreateMonthlyBackup_ThenNeedsMonthlyBackupIsFalse()
    {
        var service = new DbBackupService();

        var path = service.CreateMonthlyBackup();

        Assert.IsTrue(File.Exists(path));
        Assert.Contains(DateTime.Now.ToString("yyyyMM"), Path.GetFileName(path));
        Assert.IsFalse(service.NeedsMonthlyBackup());
    }

    [TestMethod]
    public void CreateMonthlyBackup_DoesNotAffectDailyOrManualBackupRetention()
    {
        var service = new DbBackupService();

        for (int i = 0; i < 5; i++)
        {
            service.CreateBackup($"backup{i}");
            Thread.Sleep(10);
        }
        service.CreateOrUpdateDailyBackup();
        service.CreateMonthlyBackup();

        Assert.HasCount(3, service.GetBackups());
        Assert.HasCount(1, service.GetDailyBackups());
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

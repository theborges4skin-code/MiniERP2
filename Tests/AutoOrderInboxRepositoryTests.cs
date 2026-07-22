using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class AutoOrderInboxRepositoryTests
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

    private static AutoOrderInboxItem MakeItem(string id) => new()
    {
        Id = id,
        SubjectSnip = "자동발주처리 - 테스트",
        ReceivedAt = new DateTime(2026, 7, 22, 9, 30, 0),
        XlsxPath = $"pending/{id}.xlsx",
        Sha256 = "abc123",
        RowCount = 3,
        ParseStatus = "ok",
        SeenAt = DateTime.Now,
    };

    [TestMethod]
    public void InsertIfNew_NewId_InsertsAsNewStatus()
    {
        var repository = new AutoOrderInboxRepository();
        repository.InsertIfNew(MakeItem("id-1"));

        var saved = repository.GetById("id-1");

        Assert.IsNotNull(saved);
        Assert.AreEqual("new", saved.Status);
        Assert.AreEqual(3, saved.RowCount);
    }

    [TestMethod]
    public void InsertIfNew_ExistingId_DoesNotOverwrite()
    {
        var repository = new AutoOrderInboxRepository();
        repository.InsertIfNew(MakeItem("id-2"));
        repository.MarkDownloaded("id-2", @"C:\local\id-2.xlsx");

        // 폴링이 같은 id를 다시 감지해도(멱등) 이미 진행된 상태(downloaded)가 new로 되돌아가면 안 된다.
        repository.InsertIfNew(MakeItem("id-2"));

        var saved = repository.GetById("id-2");
        Assert.AreEqual("downloaded", saved!.Status);
    }

    [TestMethod]
    public void Exists_ReturnsTrueOnlyAfterInsert()
    {
        var repository = new AutoOrderInboxRepository();
        Assert.IsFalse(repository.Exists("id-3"));

        repository.InsertIfNew(MakeItem("id-3"));

        Assert.IsTrue(repository.Exists("id-3"));
    }

    [TestMethod]
    public void CountNew_OnlyCountsNewStatus()
    {
        var repository = new AutoOrderInboxRepository();
        repository.InsertIfNew(MakeItem("id-4"));
        repository.InsertIfNew(MakeItem("id-5"));
        repository.MarkDownloaded("id-5", @"C:\local\id-5.xlsx");

        Assert.AreEqual(1, repository.CountNew());
    }

    [TestMethod]
    public void StatusTransitions_DownloadedThenImported()
    {
        var repository = new AutoOrderInboxRepository();
        repository.InsertIfNew(MakeItem("id-6"));

        repository.MarkDownloaded("id-6", @"C:\local\id-6.xlsx");
        var afterDownload = repository.GetById("id-6")!;
        Assert.AreEqual("downloaded", afterDownload.Status);
        Assert.AreEqual(@"C:\local\id-6.xlsx", afterDownload.LocalFilePath);

        repository.MarkImported("id-6");
        var afterImport = repository.GetById("id-6")!;
        Assert.AreEqual("imported", afterImport.Status);
        Assert.IsNotNull(afterImport.ImportedAt);
    }

    [TestMethod]
    public void MarkDismissed_SetsDismissedStatus()
    {
        var repository = new AutoOrderInboxRepository();
        repository.InsertIfNew(MakeItem("id-7"));

        repository.MarkDismissed("id-7");

        Assert.AreEqual("dismissed", repository.GetById("id-7")!.Status);
    }

    [TestMethod]
    public void GetAll_OrdersByReceivedAtDescending()
    {
        var repository = new AutoOrderInboxRepository();
        var older = MakeItem("id-8");
        older.ReceivedAt = new DateTime(2026, 7, 20, 9, 0, 0);
        var newer = MakeItem("id-9");
        newer.ReceivedAt = new DateTime(2026, 7, 22, 9, 0, 0);

        repository.InsertIfNew(older);
        repository.InsertIfNew(newer);

        var all = repository.GetAll();

        Assert.HasCount(2, all);
        Assert.AreEqual("id-9", all[0].Id);
        Assert.AreEqual("id-8", all[1].Id);
    }
}

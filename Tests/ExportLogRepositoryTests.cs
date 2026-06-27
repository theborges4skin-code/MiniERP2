using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class ExportLogRepositoryTests
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
    public void Add_ThenGetRecent_ReturnsNewestFirst()
    {
        var repository = new ExportLogRepository();

        repository.Add(new ExportLogEntry { TableName = "마스터SKU", FilePath = "a.xlsx", RowCount = 10, Headers = "Sku, ItemName" });
        repository.Add(new ExportLogEntry { TableName = "CSKU", FilePath = "b.xlsx", RowCount = 5, Headers = "ChannelCode, CskuCode" });

        var results = repository.GetRecent();

        Assert.HasCount(2, results);
        Assert.AreEqual("CSKU", results[0].TableName);
        Assert.AreEqual("마스터SKU", results[1].TableName);
    }
}

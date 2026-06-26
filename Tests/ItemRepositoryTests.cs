using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class ItemRepositoryTests
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
    public void Upsert_ThenGetBySku_ReturnsSavedItem()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "SKU-001", ItemName = "테스트상품", CostPrice = 1000m });

        var saved = repository.GetBySku("SKU-001");

        Assert.IsNotNull(saved);
        Assert.AreEqual("테스트상품", saved.ItemName);
        Assert.AreEqual(1000m, saved.CostPrice);
    }

    [TestMethod]
    public void Upsert_WithChangedCost_RecordsCostHistory()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "SKU-002", ItemName = "원가변경상품", CostPrice = 1000m });
        repository.Upsert(new ItemModel { Sku = "SKU-002", ItemName = "원가변경상품", CostPrice = 1200m });

        var history = repository.GetCostHistory("SKU-002");
        var saved = repository.GetBySku("SKU-002");

        Assert.HasCount(1, history);
        Assert.AreEqual(1000m, history[0].OldCost);
        Assert.AreEqual(1200m, history[0].NewCost);
        Assert.AreEqual(1200m, saved!.CostPrice);
    }

    [TestMethod]
    public void GetAll_ReturnsAllSavedItems()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "SKU-A", ItemName = "A", CostPrice = 100m });
        repository.Upsert(new ItemModel { Sku = "SKU-B", ItemName = "B", CostPrice = 200m });

        var all = repository.GetAll();

        Assert.HasCount(2, all);
    }
}

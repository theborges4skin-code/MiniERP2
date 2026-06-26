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
        var beforeChange = DateTime.UtcNow;
        repository.Upsert(new ItemModel { Sku = "SKU-002", ItemName = "원가변경상품", CostPrice = 1000m });
        repository.Upsert(new ItemModel { Sku = "SKU-002", ItemName = "원가변경상품", CostPrice = 1200m });
        var afterChange = DateTime.UtcNow;

        var history = repository.GetCostHistory("SKU-002");
        var saved = repository.GetBySku("SKU-002");

        Assert.HasCount(1, history);
        Assert.AreEqual(1000m, history[0].OldCost);
        Assert.AreEqual(1200m, history[0].NewCost);
        Assert.IsTrue(history[0].ChangedAt >= beforeChange && history[0].ChangedAt <= afterChange);
        Assert.AreEqual(1200m, saved!.CostPrice);
    }

    [TestMethod]
    public void Upsert_WithReserveFields_RoundTrips()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "SKU-RES", ItemName = "예비필드상품", CostPrice = 500m, Reserve1 = "규격A", Reserve2 = "제조사B", Reserve3 = null });

        var saved = repository.GetBySku("SKU-RES");

        Assert.IsNotNull(saved);
        Assert.AreEqual("규격A", saved.Reserve1);
        Assert.AreEqual("제조사B", saved.Reserve2);
        Assert.IsNull(saved.Reserve3);
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

    [TestMethod]
    public void Delete_RemovesItemAndHistory()
    {
        var repository = new ItemRepository();
        var skuToDelete = "SKU-DEL-001";

        // 삭제할 데이터와 이력 생성
        repository.Upsert(new ItemModel { Sku = skuToDelete, ItemName = "삭제될 상품", CostPrice = 100m });
        repository.Upsert(new ItemModel { Sku = skuToDelete, ItemName = "삭제될 상품", CostPrice = 110m });
        repository.Upsert(new ItemModel { Sku = "SKU-KEEP-001", ItemName = "유지될 상품", CostPrice = 200m });

        repository.Delete(skuToDelete);

        var deletedItem = repository.GetBySku(skuToDelete);
        var deletedHistory = repository.GetCostHistory(skuToDelete);
        Assert.IsNull(deletedItem, "삭제된 아이템이 조회되면 안 됩니다.");
        Assert.IsEmpty(deletedHistory, "삭제된 아이템의 이력이 남아있으면 안 됩니다.");
        Assert.IsNotNull(repository.GetBySku("SKU-KEEP-001"), "다른 아이템이 삭제되면 안 됩니다.");
    }
}

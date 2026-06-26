using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class ChannelSkuRepositoryTests
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
    public void Upsert_ThenGet_ReturnsSavedItem()
    {
        var repository = new ChannelSkuRepository();
        var csku = new ChannelSkuModel { ChannelCode = "COUPANG", Msku = "CSKU-001", SupplyPrice = 5000m };
        repository.Upsert(csku);

        var saved = repository.GetByChannelAndMsku("COUPANG", "CSKU-001");

        Assert.IsNotNull(saved);
        Assert.AreEqual("COUPANG", saved.ChannelCode);
        Assert.AreEqual("CSKU-001", saved.Msku);
        Assert.AreEqual(5000m, saved.SupplyPrice);
    }

    [TestMethod]
    public void Upsert_WithChangedPrice_RecordsPriceHistory()
    {
        var repository = new ChannelSkuRepository();
        var beforeChange = DateTime.UtcNow;
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", Msku = "CSKU-002", SupplyPrice = 5000m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", Msku = "CSKU-002", SupplyPrice = 5500m });
        var afterChange = DateTime.UtcNow;

        var history = repository.GetPriceHistory("COUPANG", "CSKU-002");
        var saved = repository.GetByChannelAndMsku("COUPANG", "CSKU-002");

        Assert.HasCount(1, history);
        Assert.AreEqual(5000m, history[0].OldPrice);
        Assert.AreEqual(5500m, history[0].NewPrice);
        Assert.IsTrue(history[0].ChangedAt >= beforeChange && history[0].ChangedAt <= afterChange);
        Assert.AreEqual(5500m, saved!.SupplyPrice);
    }

    [TestMethod]
    public void GetAllByMsku_ReturnsCorrectItems()
    {
        var repository = new ChannelSkuRepository();
        var targetMsku = "MSKU-TARGET-01";

        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", Msku = targetMsku, SupplyPrice = 1000m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "11ST", Msku = targetMsku, SupplyPrice = 1100m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "NAVER", Msku = "MSKU-OTHER-02", SupplyPrice = 2000m });

        var results = repository.GetAllByMsku(targetMsku);

        Assert.HasCount(2, results);
        Assert.IsTrue(results.All(c => c.Msku == targetMsku));
        Assert.IsNotNull(results.Find(c => c.ChannelCode == "COUPANG"));
    }

    [TestMethod]
    public void Delete_RemovesChannelSkuAndHistory()
    {
        var repository = new ChannelSkuRepository();
        var msku = "MSKU-DEL-01";
        var channelCode = "COUPANG";

        // Create data to delete and its history
        repository.Upsert(new ChannelSkuModel { ChannelCode = channelCode, Msku = msku, SupplyPrice = 1000m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = channelCode, Msku = msku, SupplyPrice = 1100m });
        // Create data to keep
        repository.Upsert(new ChannelSkuModel { ChannelCode = "11ST", Msku = msku, SupplyPrice = 1200m });

        // Act
        repository.Delete(channelCode, msku);

        // Assert
        var deletedItem = repository.GetByChannelAndMsku(channelCode, msku);
        var deletedHistory = repository.GetPriceHistory(channelCode, msku);
        Assert.IsNull(deletedItem, "Deleted item should not be found.");
        Assert.IsEmpty(deletedHistory, "History of deleted item should be empty.");
        Assert.IsNotNull(repository.GetByChannelAndMsku("11ST", msku), "Other channel's item should not be deleted.");
    }
}
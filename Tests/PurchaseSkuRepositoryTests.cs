using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class PurchaseSkuRepositoryTests
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
        var repository = new PurchaseSkuRepository();
        repository.Upsert(new PurchaseSkuModel { ChannelCode = "VENDOR_A", Msku = "MSKU-001", PurchasePrice = 3000m });

        var saved = repository.GetByChannelAndMsku("VENDOR_A", "MSKU-001");

        Assert.IsNotNull(saved);
        Assert.AreEqual("VENDOR_A", saved.ChannelCode);
        Assert.AreEqual("MSKU-001", saved.Msku);
        Assert.AreEqual(3000m, saved.PurchasePrice);
        Assert.AreEqual("kg", saved.Unit);
    }

    [TestMethod]
    public void Upsert_WithChangedPrice_RecordsPriceHistoryWithReason()
    {
        var repository = new PurchaseSkuRepository();
        repository.Upsert(new PurchaseSkuModel { ChannelCode = "VENDOR_A", Msku = "MSKU-002", PurchasePrice = 3000m });
        repository.Upsert(new PurchaseSkuModel { ChannelCode = "VENDOR_A", Msku = "MSKU-002", PurchasePrice = 3500m }, reason: "원자재 인상");

        var history = repository.GetPriceHistory("VENDOR_A", "MSKU-002");

        Assert.HasCount(1, history);
        Assert.AreEqual(3000m, history[0].OldPrice);
        Assert.AreEqual(3500m, history[0].NewPrice);
        Assert.AreEqual("원자재 인상", history[0].Reason);
    }

    [TestMethod]
    public void GetAllByMsku_ReturnsAllPurchaseChannelsForThatMsku()
    {
        // 같은 마스터SKU를 여러 매입처가 서로 다른 가격에 매입할 수 있다.
        var repository = new PurchaseSkuRepository();
        repository.Upsert(new PurchaseSkuModel { ChannelCode = "VENDOR_A", Msku = "MSKU-T1", PurchasePrice = 3000m });
        repository.Upsert(new PurchaseSkuModel { ChannelCode = "VENDOR_B", Msku = "MSKU-T1", PurchasePrice = 3200m });
        repository.Upsert(new PurchaseSkuModel { ChannelCode = "VENDOR_A", Msku = "MSKU-OTHER", PurchasePrice = 5000m });

        var results = repository.GetAllByMsku("MSKU-T1");

        Assert.HasCount(2, results);
        Assert.IsTrue(results.All(s => s.Msku == "MSKU-T1"));
    }

    [TestMethod]
    public void GetAllByChannel_ReturnsOnlyThatChannelsPurchaseSkus()
    {
        var repository = new PurchaseSkuRepository();
        repository.Upsert(new PurchaseSkuModel { ChannelCode = "VENDOR_A", Msku = "MSKU-A", PurchasePrice = 1000m });
        repository.Upsert(new PurchaseSkuModel { ChannelCode = "VENDOR_A", Msku = "MSKU-B", PurchasePrice = 2000m });
        repository.Upsert(new PurchaseSkuModel { ChannelCode = "VENDOR_B", Msku = "MSKU-A", PurchasePrice = 1500m });

        var results = repository.GetAllByChannel("VENDOR_A");

        Assert.HasCount(2, results);
        Assert.IsTrue(results.All(s => s.ChannelCode == "VENDOR_A"));
    }

    [TestMethod]
    public void Delete_RemovesPurchaseSkuAndHistory()
    {
        var repository = new PurchaseSkuRepository();
        repository.Upsert(new PurchaseSkuModel { ChannelCode = "VENDOR_A", Msku = "MSKU-DEL", PurchasePrice = 1000m });
        repository.Upsert(new PurchaseSkuModel { ChannelCode = "VENDOR_A", Msku = "MSKU-DEL", PurchasePrice = 1100m });

        repository.Delete("VENDOR_A", "MSKU-DEL");

        Assert.IsNull(repository.GetByChannelAndMsku("VENDOR_A", "MSKU-DEL"));
        Assert.IsEmpty(repository.GetPriceHistory("VENDOR_A", "MSKU-DEL"));
    }
}

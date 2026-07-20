using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class SalesChannelRepositoryTests
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
    public void Upsert_ThenGetAll_ReturnsChannels()
    {
        var repository = new SalesChannelRepository();
        repository.Upsert(new SalesChannel { ChannelCode = "COUPANG", ChannelName = "쿠팡", GroupName = "B그룹" });
        repository.Upsert(new SalesChannel { ChannelCode = "11ST", ChannelName = "11번가", GroupName = "A그룹" });

        var channels = repository.GetAll();

        Assert.HasCount(2, channels);
        Assert.AreEqual("A그룹", channels[0].GroupName); // Check for group order
        Assert.AreEqual("11번가", channels[0].ChannelName);
    }

    [TestMethod]
    public void Upsert_DefaultFlags_IsSalesTrueAndIsPurchaseFalse()
    {
        // 기존 채널은 전부 판매 채널이었으므로, 플래그를 지정하지 않으면 IsSales=true/IsPurchase=false여야 한다.
        var repository = new SalesChannelRepository();
        repository.Upsert(new SalesChannel { ChannelCode = "COUPANG", ChannelName = "쿠팡" });

        var saved = repository.GetAll().Single();

        Assert.IsTrue(saved.IsSales);
        Assert.IsFalse(saved.IsPurchase);
    }

    [TestMethod]
    public void Upsert_WithPurchaseFlag_PersistsAndCanBeUpdated()
    {
        var repository = new SalesChannelRepository();
        repository.Upsert(new SalesChannel { ChannelCode = "VENDOR_A", ChannelName = "농산물벤더A", IsPurchase = true, IsSales = false });

        var saved = repository.GetAll().Single();
        Assert.IsTrue(saved.IsPurchase);
        Assert.IsFalse(saved.IsSales);

        repository.Upsert(new SalesChannel { ChannelCode = "VENDOR_A", ChannelName = "농산물벤더A", IsPurchase = true, IsSales = true });
        var updated = repository.GetAll().Single();
        Assert.IsTrue(updated.IsPurchase);
        Assert.IsTrue(updated.IsSales, "한 채널이 매입·매출을 동시에 겸할 수 있어야 한다.");
    }

    [TestMethod]
    public void Delete_RemovesChannel()
    {
        var repository = new SalesChannelRepository();
        repository.Upsert(new SalesChannel { ChannelCode = "COUPANG", ChannelName = "쿠팡" });
        repository.Upsert(new SalesChannel { ChannelCode = "11ST", ChannelName = "11번가" });

        repository.Delete("COUPANG");
        var channels = repository.GetAll();

        Assert.HasCount(1, channels);
        Assert.AreEqual("11ST", channels[0].ChannelCode);
    }
}
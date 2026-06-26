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
using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class OutboundRepositoryTests
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
    public void SaveOutbound_SameOrderAndSkuTwice_UpdatesInsteadOfDuplicating()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.UtcNow.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-1", TrackingNo = "T001", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });

        // 같은 주문/SKU를 다시 저장(예: 같은 발주서를 중복 처리) — 중복 적재가 아니라 갱신되어야 한다.
        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-1", TrackingNo = "T002", MskuCode = "SKU-1", Qty = 2, SupplyPrice = 1500m },
        });

        var to = DateTime.UtcNow.AddMinutes(5);
        var results = repository.GetByChannel(channelCode, from, to);

        Assert.HasCount(1, results);
        Assert.AreEqual("T002", results[0].TrackingNo);
        Assert.AreEqual(2, results[0].Qty);
        Assert.AreEqual(1500m, results[0].SupplyPrice);
    }

    [TestMethod]
    public void SaveOutbound_DifferentSkusSameOrder_SavesBothRows()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.UtcNow.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-2", TrackingNo = "T010", MskuCode = "SKU-A", Qty = 1, SupplyPrice = 1000m },
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-2", TrackingNo = "T010", MskuCode = "SKU-B", Qty = 1, SupplyPrice = 2000m },
        });

        var to = DateTime.UtcNow.AddMinutes(5);
        var results = repository.GetByChannel(channelCode, from, to);

        Assert.HasCount(2, results);
    }
}

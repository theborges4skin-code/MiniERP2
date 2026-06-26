using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class SettlementRepositoryTests
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
    public void Insert_ThenGetByChannel_ReturnsRows()
    {
        var repository = new SettlementRepository();
        repository.Insert(new List<SettlementData>
        {
            new() { ChannelCode = "COUPANG", ProductName = "상품A", Msku = "SKU1", Qty = 2, Settlement = 10000m, Shipping = 500m, Fee = 100m, Profit = 4000m, Status = "매핑(1:1)" },
            new() { ChannelCode = "COUPANG", ProductName = "상품B", Msku = "SKU2", Qty = 1, Settlement = 5000m, Shipping = 0m, Fee = 0m, Profit = 2000m, Status = "매핑(1:1)" },
            new() { ChannelCode = "11ST", ProductName = "상품C", Msku = "SKU3", Qty = 1, Settlement = 3000m, Shipping = 0m, Fee = 0m, Profit = 1000m, Status = "매핑(1:1)" },
        });

        var rows = repository.GetByChannel("COUPANG");

        Assert.HasCount(2, rows);
        Assert.AreEqual("SKU1", rows[0].Msku);
        Assert.AreEqual(4000m, rows[0].Profit);
    }
}

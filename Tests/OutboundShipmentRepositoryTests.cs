using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class OutboundShipmentRepositoryTests
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
    public void Upsert_ThenGetByKey_ReturnsSavedShipment()
    {
        var repository = new OutboundShipmentRepository();
        repository.Upsert(new OutboundShipmentModel { ShipmentGroupKey = "SHIP-001", FreightCost = 15000m });

        var saved = repository.GetByKey("SHIP-001");

        Assert.IsNotNull(saved);
        Assert.AreEqual("SHIP-001", saved.ShipmentGroupKey);
        Assert.AreEqual(15000m, saved.FreightCost);
    }

    [TestMethod]
    public void Upsert_SameKeyTwice_UpdatesInPlace()
    {
        var repository = new OutboundShipmentRepository();
        repository.Upsert(new OutboundShipmentModel { ShipmentGroupKey = "SHIP-002", FreightCost = 10000m });
        repository.Upsert(new OutboundShipmentModel { ShipmentGroupKey = "SHIP-002", FreightCost = 12000m, Note = "재계산" });

        var saved = repository.GetByKey("SHIP-002");

        Assert.AreEqual(12000m, saved!.FreightCost);
        Assert.AreEqual("재계산", saved.Note);
    }

    [TestMethod]
    public void GetByKeys_ReturnsOnlyRequestedShipments()
    {
        var repository = new OutboundShipmentRepository();
        repository.Upsert(new OutboundShipmentModel { ShipmentGroupKey = "SHIP-A", FreightCost = 1000m });
        repository.Upsert(new OutboundShipmentModel { ShipmentGroupKey = "SHIP-B", FreightCost = 2000m });
        repository.Upsert(new OutboundShipmentModel { ShipmentGroupKey = "SHIP-C", FreightCost = 3000m });

        var results = repository.GetByKeys(["SHIP-A", "SHIP-C", "SHIP-NONEXISTENT"]);

        Assert.HasCount(2, results);
        Assert.IsTrue(results.Any(s => s.ShipmentGroupKey == "SHIP-A"));
        Assert.IsTrue(results.Any(s => s.ShipmentGroupKey == "SHIP-C"));
    }

    [TestMethod]
    public void Delete_RemovesShipment()
    {
        var repository = new OutboundShipmentRepository();
        repository.Upsert(new OutboundShipmentModel { ShipmentGroupKey = "SHIP-DEL", FreightCost = 5000m });

        repository.Delete("SHIP-DEL");

        Assert.IsNull(repository.GetByKey("SHIP-DEL"));
    }
}

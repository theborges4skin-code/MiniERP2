using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class FbaOrderRepositoryHistoryTests
{
    private string _testFolder = string.Empty;
    private FbaOrderRepository _repository = new();

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _repository = new FbaOrderRepository();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private void SeedOrder(string fbaNo, DateTime orderDate, string? shipmentId, params (int BoxSeq, string Csku, string ItemName, int Qty)[] rows)
    {
        var order = new FbaOrder { FbaNo = fbaNo, OrderDate = orderDate, ShipmentId = shipmentId, ReceiverName = "R", Phone = "P", Address = "A" };
        var boxSeqs = rows.Select(r => r.BoxSeq).Distinct();
        var boxes = boxSeqs.Select(seq => new FbaBox { FbaNo = fbaNo, BoxSeq = seq }).ToList();
        var items = rows.Select((r, i) => new FbaBoxItem { FbaNo = fbaNo, BoxSeq = r.BoxSeq, ItemSeq = i + 1, Csku = r.Csku, ItemName = r.ItemName, Qty = r.Qty }).ToList();
        _repository.SaveOrder(order, boxes, items);
    }

    [TestMethod]
    public void GetHistory_FiltersByOrderDateRange()
    {
        SeedOrder("FBA-IN", new DateTime(2026, 8, 5), "S1", (1, "CSKU-A", "A", 1));
        SeedOrder("FBA-OUT", new DateTime(2026, 1, 1), "S2", (1, "CSKU-B", "B", 1));

        var rows = _repository.GetHistory(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));

        Assert.HasCount(1, rows);
        Assert.AreEqual("FBA-IN", rows[0].FbaNo);
    }

    [TestMethod]
    public void GetHistory_ReturnsOneRowPerBoxItem_WithBoxAndOrderFieldsJoined()
    {
        SeedOrder("FBA-1", new DateTime(2026, 8, 5), "SHIP1",
            (1, "CSKU-A", "품목A", 2),
            (2, "CSKU-B", "품목B", 3));
        _repository.ApplyTracking("FBA-1", 1, "T001");

        var rows = _repository.GetHistory(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));

        Assert.HasCount(2, rows);
        var boxOneRow = rows.Single(r => r.BoxSeq == 1);
        Assert.AreEqual("SHIP1", boxOneRow.ShipmentId);
        Assert.AreEqual("CSKU-A", boxOneRow.Csku);
        Assert.AreEqual(2, boxOneRow.Qty);
        Assert.AreEqual("T001", boxOneRow.TrackingNo);

        var boxTwoRow = rows.Single(r => r.BoxSeq == 2);
        Assert.IsNull(boxTwoRow.TrackingNo);
    }
}

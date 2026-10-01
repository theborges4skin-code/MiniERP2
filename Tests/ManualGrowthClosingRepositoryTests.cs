using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class ManualGrowthClosingRepositoryTests
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

    private static ManualGrowthClosing Closing(string period, params (string Csku, decimal Qty)[] lines)
    {
        var closing = new ManualGrowthClosing
        {
            Period = period,
            ChannelCode = "GROWTH",
            SourceFileName = "f.xlsx",
            SourceSheetName = "2609 제트",
            Status = ManualGrowthClosing.StatusConfirmed,
            ConfirmedAt = "2026-10-01 10:00:00",
        };
        int rowNo = 0;
        foreach (var (csku, qty) in lines)
        {
            closing.Lines.Add(new ManualGrowthLine
            {
                RowNo = ++rowNo, LineDate = period + "-03", ItemName = csku, CskuCode = csku, MasterSku = "M-" + csku,
                ProductGroup = "G", Qty = qty, UnitPrice = 1000, SupplyAmount = qty * 1000, Tax = qty * 100,
                CostPrice = 500, Profit = qty * 600, PriceMismatch = rowNo == 1,
            });
        }
        closing.TotalQty = closing.Lines.Sum(l => l.Qty);
        closing.TotalSupply = closing.Lines.Sum(l => l.SupplyAmount);
        return closing;
    }

    [TestMethod]
    public void Save_ThenGet_RoundTripsHeaderAndLineSnapshot()
    {
        var repo = new ManualGrowthClosingRepository();
        var id = repo.Save(Closing("2026-09", ("A", 2), ("B", 3)));

        var loaded = repo.Get("2026-09", "GROWTH");

        Assert.IsNotNull(loaded);
        Assert.AreEqual(id, loaded.Id);
        Assert.AreEqual(ManualGrowthClosing.StatusConfirmed, loaded.Status);
        Assert.AreEqual(5m, loaded.TotalQty);
        Assert.AreEqual(2, loaded.Lines.Count);
        Assert.AreEqual("M-A", loaded.Lines[0].MasterSku);
        Assert.IsTrue(loaded.Lines[0].PriceMismatch);
        Assert.AreEqual(1200m, loaded.Lines[0].Profit);
        Assert.AreEqual("2026-09", loaded.Lines[0].Period);
        Assert.IsFalse(loaded.Lines[0].DateMismatch);
    }

    [TestMethod]
    public void Save_SamePeriodAgain_OverwritesHeaderAndReplacesLines_KeepsId()
    {
        var repo = new ManualGrowthClosingRepository();
        var id1 = repo.Save(Closing("2026-09", ("A", 2), ("B", 3)));
        var id2 = repo.Save(Closing("2026-09", ("C", 7)));

        Assert.AreEqual(id1, id2);
        var loaded = repo.Get("2026-09", "GROWTH")!;
        Assert.AreEqual(1, loaded.Lines.Count);
        Assert.AreEqual("C", loaded.Lines[0].CskuCode);
        Assert.AreEqual(1, repo.GetHeaders().Count);
    }

    [TestMethod]
    public void SetStatus_Unconfirm_KeepsLines_ClearsConfirmedAt()
    {
        var repo = new ManualGrowthClosingRepository();
        var id = repo.Save(Closing("2026-09", ("A", 2)));

        repo.SetStatus(id, ManualGrowthClosing.StatusUnconfirmed);

        var loaded = repo.Get("2026-09", "GROWTH")!;
        Assert.AreEqual(ManualGrowthClosing.StatusUnconfirmed, loaded.Status);
        Assert.IsNull(loaded.ConfirmedAt);
        Assert.AreEqual(1, loaded.Lines.Count);
    }

    [TestMethod]
    public void GetHeaders_FiltersByChannel_LatestPeriodFirst()
    {
        var repo = new ManualGrowthClosingRepository();
        repo.Save(Closing("2026-08", ("A", 1)));
        repo.Save(Closing("2026-09", ("A", 1)));
        var other = Closing("2026-09", ("A", 1));
        other.ChannelCode = "OTHER";
        repo.Save(other);

        var headers = repo.GetHeaders("GROWTH");

        CollectionAssert.AreEqual(new[] { "2026-09", "2026-08" }, headers.Select(h => h.Period).ToArray());
        Assert.AreEqual(3, repo.GetHeaders().Count);
    }

    [TestMethod]
    public void Delete_RemovesHeaderAndLines()
    {
        var repo = new ManualGrowthClosingRepository();
        var id = repo.Save(Closing("2026-09", ("A", 1)));

        repo.Delete(id);

        Assert.IsNull(repo.Get("2026-09", "GROWTH"));
    }
}

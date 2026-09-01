using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class CskuStatRepositoryTests
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

    private static CskuStatBatch Batch(string period = "2026-08", decimal rate = 0) =>
        new() { Period = period, Memo = "메모", ExchangeRate = rate };

    private static CskuStatLine Line(string channel, string csku, int qty, decimal revenue, decimal profit) => new()
    {
        FileKind = CskuFileKind.General,
        ChannelCode = channel,
        ChannelName = channel,
        CskuCode = csku,
        ProductGroup = "그룹",
        ProductName = "상품",
        RowCount = 1,
        Qty = qty,
        Revenue = revenue,
        Settlement = revenue,
        Profit = profit,
    };

    private static CskuStatFile File(string fileName, int rowCount, int sumQty, decimal sumRevenue, decimal sumProfit) => new()
    {
        FileName = fileName,
        FileKind = CskuFileKind.General,
        RowCount = rowCount,
        SumQty = sumQty,
        SumRevenue = sumRevenue,
        SumProfit = sumProfit,
    };

    [TestMethod]
    public void SaveBatch_ThenGetLines_RoundTripsValues()
    {
        var repo = new CskuStatRepository();
        var batchId = repo.SaveBatch(Batch(), [Line("COUPANG", "CSKU1", 2, 1000m, 200m)], [File("a.xlsx", 2, 2, 1000m, 200m)]);

        var lines = repo.GetLines(batchId);

        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual("COUPANG", lines[0].ChannelCode);
        Assert.AreEqual("CSKU1", lines[0].CskuCode);
        Assert.AreEqual(2, lines[0].Qty);
        Assert.AreEqual(1000m, lines[0].Revenue);
        Assert.AreEqual(200m, lines[0].Profit);
    }

    [TestMethod]
    public void SaveBatch_MultipleBatches_AreIndependentSnapshots()
    {
        var repo = new CskuStatRepository();
        var id1 = repo.SaveBatch(Batch("2026-07"), [Line("COUPANG", "CSKU1", 1, 100m, 10m)], [File("a.xlsx", 1, 1, 100m, 10m)]);
        var id2 = repo.SaveBatch(Batch("2026-08"), [Line("COUPANG", "CSKU1", 2, 200m, 20m)], [File("b.xlsx", 2, 2, 200m, 20m)]);

        Assert.AreNotEqual(id1, id2);
        Assert.AreEqual(2, repo.GetBatches().Count);
        Assert.AreEqual(1, repo.GetLines(id1).Single().Qty);
        Assert.AreEqual(2, repo.GetLines(id2).Single().Qty);
    }

    [TestMethod]
    public void FindDuplicateFile_AllFiveValuesMatch_ReturnsMatch()
    {
        var repo = new CskuStatRepository();
        repo.SaveBatch(Batch(), [Line("COUPANG", "CSKU1", 2, 1000m, 200m)], [File("a.xlsx", 2, 2, 1000m, 200m)]);

        var match = repo.FindDuplicateFile("a.xlsx", 2, 2, 1000m, 200m);

        Assert.IsNotNull(match);
        Assert.AreEqual("2026-08", match.Value.Batch.Period);
    }

    [TestMethod]
    public void FindDuplicateFile_OneValueDiffers_ReturnsNull()
    {
        var repo = new CskuStatRepository();
        repo.SaveBatch(Batch(), [Line("COUPANG", "CSKU1", 2, 1000m, 200m)], [File("a.xlsx", 2, 2, 1000m, 200m)]);

        // 매출합만 다름 → 다른 파일로 취급(§7).
        var match = repo.FindDuplicateFile("a.xlsx", 2, 2, 1000.01m, 200m);

        Assert.IsNull(match);
    }

    [TestMethod]
    public void DeleteBatch_RemovesLinesAndFiles()
    {
        var repo = new CskuStatRepository();
        var batchId = repo.SaveBatch(Batch(), [Line("COUPANG", "CSKU1", 1, 100m, 10m)], [File("a.xlsx", 1, 1, 100m, 10m)]);

        repo.DeleteBatch(batchId);

        Assert.IsNull(repo.GetBatch(batchId));
        Assert.AreEqual(0, repo.GetLines(batchId).Count);
        Assert.AreEqual(0, repo.GetFiles(batchId).Count);
    }
}

using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

/// <summary>
/// 매핑시스템 통합개편 기획서 §5 — ClosingUnmapped에 추가한 Quantity/SampleRevenue(가격 매핑
/// 채널의 4필드 집계용 대표 샘플)가 저장/조회 왕복에서 유실되지 않는지 검증한다.
/// </summary>
[TestClass]
public class ClosingRunRepositoryTests
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
    public void ReplaceUnmapped_WithQuantityAndSampleRevenue_RoundTripsThroughGetUnmapped()
    {
        var repository = new ClosingRunRepository();
        var runId = repository.CreateRun("C:\\test", "2026-08");

        repository.ReplaceUnmapped(runId, "CH1", new List<ClosingUnmappedItem>
        {
            new()
            {
                ChannelCode = "CH1",
                SourceKey = "상품A|옵션1|2|10000",
                OccurrenceCount = 3,
                SampleAmount = 9500m,
                Quantity = 2,
                SampleRevenue = 10000m,
            },
        });

        var result = repository.GetUnmapped(runId);

        Assert.HasCount(1, result);
        Assert.AreEqual(2, result[0].Quantity);
        Assert.AreEqual(10000m, result[0].SampleRevenue);
        Assert.AreEqual("상품A", result[0].ProductName);
        Assert.AreEqual("옵션1", result[0].OptionName);
    }

    [TestMethod]
    public void ReplaceUnmapped_WithoutQuantityAndSampleRevenue_LeavesThemNull()
    {
        // 가격 매핑 없는 채널은 기존처럼 2필드 키만 쓰고, Quantity/SampleRevenue는 null이어야 한다.
        var repository = new ClosingRunRepository();
        var runId = repository.CreateRun("C:\\test", "2026-08");

        repository.ReplaceUnmapped(runId, "CH1", new List<ClosingUnmappedItem>
        {
            new() { ChannelCode = "CH1", SourceKey = "상품A|옵션1", OccurrenceCount = 1, SampleAmount = 5000m },
        });

        var result = repository.GetUnmapped(runId);

        Assert.HasCount(1, result);
        Assert.IsNull(result[0].Quantity);
        Assert.IsNull(result[0].SampleRevenue);
    }
}

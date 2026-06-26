using MiniERP2.Config;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class ChannelConfigServiceTests
{
    private string _testFile = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFile = Path.Combine(Path.GetTempPath(), $"MiniERP2Tests_channels_{Guid.NewGuid()}.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_testFile))
        {
            File.Delete(_testFile);
        }
    }

    [TestMethod]
    public void Save_ThenLoad_RoundTripsChannelConfig()
    {
        var service = new ChannelConfigService(_testFile);
        var configs = new List<ChannelConfig>
        {
            new()
            {
                ChannelCode = "COUPANG_GROWTH",
                ChannelName = "쿠팡그로스",
                ChannelType = ChannelType.CoupangGrowth,
                FieldMappings = new Dictionary<StdField, FieldMapping>
                {
                    [StdField.SettlementAmount] = new FieldMapping { SheetName = "정산", HeaderRow = 2, Column = "C" },
                },
                GrowthAuxSources = new List<GrowthAuxSource>
                {
                    new()
                    {
                        Enabled = true,
                        TargetStdField = StdField.HandlingFee,
                        SheetName = "입출고비",
                        HeaderRow = 1,
                        KeyHeader = "옵션ID",
                        ValueHeader = "금액",
                        OutCol = "HandlingFee",
                    },
                },
            },
        };

        service.Save(configs);
        var loaded = service.Load();

        Assert.HasCount(1, loaded);
        Assert.AreEqual("COUPANG_GROWTH", loaded[0].ChannelCode);
        Assert.AreEqual(ChannelType.CoupangGrowth, loaded[0].ChannelType);
        Assert.AreEqual("정산", loaded[0].FieldMappings[StdField.SettlementAmount].SheetName);
        Assert.HasCount(1, loaded[0].GrowthAuxSources);
        Assert.AreEqual("입출고비", loaded[0].GrowthAuxSources[0].SheetName);
    }

    [TestMethod]
    public void Load_WhenFileMissing_ReturnsEmptyList()
    {
        var service = new ChannelConfigService(_testFile);

        var loaded = service.Load();

        Assert.IsEmpty(loaded);
    }
}

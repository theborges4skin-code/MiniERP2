using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class AutoOrderChannelResolverTests
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
    public void FindStandardPreset_ReturnsChannelFlaggedAsPreset()
    {
        var configs = new List<ChannelConfig>
        {
            new() { ChannelCode = "COUPANG", ChannelName = "쿠팡" },
            new() { ChannelCode = "AUTOORDER", ChannelName = "자동발주(표준)", IsAutoOrderStandardPreset = true },
        };

        var preset = AutoOrderChannelResolver.FindStandardPreset(configs);

        Assert.IsNotNull(preset);
        Assert.AreEqual("AUTOORDER", preset.ChannelCode);
    }

    [TestMethod]
    public void FindStandardPreset_NoneFlagged_ReturnsNull()
    {
        var configs = new List<ChannelConfig>
        {
            new() { ChannelCode = "COUPANG", ChannelName = "쿠팡" },
        };

        Assert.IsNull(AutoOrderChannelResolver.FindStandardPreset(configs));
    }

    [TestMethod]
    public void ResolveChannelCode_MatchingHint_ReturnsRealChannelCode()
    {
        var configs = new List<ChannelConfig>
        {
            new() { ChannelCode = "COUPANG", ChannelName = "쿠팡", AutoOrderHints = "쿠팡,COUPANG" },
        };

        Assert.AreEqual("COUPANG", AutoOrderChannelResolver.ResolveChannelCode(configs, "쿠팡"));
        Assert.AreEqual("COUPANG", AutoOrderChannelResolver.ResolveChannelCode(configs, "coupang"));
    }

    [TestMethod]
    public void ResolveChannelCode_NoMatch_ReturnsNull()
    {
        var configs = new List<ChannelConfig>
        {
            new() { ChannelCode = "COUPANG", ChannelName = "쿠팡", AutoOrderHints = "쿠팡,COUPANG" },
        };

        Assert.IsNull(AutoOrderChannelResolver.ResolveChannelCode(configs, "스마트스토어"));
    }

    [TestMethod]
    public void ResolveChannelCode_EmptyHint_ReturnsNull()
    {
        var configs = new List<ChannelConfig>
        {
            new() { ChannelCode = "COUPANG", ChannelName = "쿠팡", AutoOrderHints = "쿠팡" },
        };

        Assert.IsNull(AutoOrderChannelResolver.ResolveChannelCode(configs, ""));
        Assert.IsNull(AutoOrderChannelResolver.ResolveChannelCode(configs, null));
    }

    [TestMethod]
    public void ResolveChannelCode_ChannelWithoutHints_IsIgnored()
    {
        var configs = new List<ChannelConfig>
        {
            new() { ChannelCode = "AUTOORDER", ChannelName = "자동발주(표준)", IsAutoOrderStandardPreset = true },
            new() { ChannelCode = "COUPANG", ChannelName = "쿠팡", AutoOrderHints = "쿠팡" },
        };

        Assert.AreEqual("COUPANG", AutoOrderChannelResolver.ResolveChannelCode(configs, "쿠팡"));
    }

    [TestMethod]
    public void ApplyChannelOverrides_MatchingHint_ReassignsChannelAndAppliesExistingMappingRules()
    {
        var mappingRepository = new MappingRepository();
        mappingRepository.SaveRules(MappingRuleType.Exact, "COUPANG", [new MappingRule { Key = "상품A옵션1", TargetSku = "SKU-001" }]);

        var configs = new List<ChannelConfig>
        {
            new() { ChannelCode = "AUTOORDER", ChannelName = "자동발주(표준)", IsAutoOrderStandardPreset = true },
            new() { ChannelCode = "COUPANG", ChannelName = "쿠팡", AutoOrderHints = "쿠팡,COUPANG" },
        };

        var items = new List<OfsOrderItem>
        {
            new() { ChannelCode = "AUTOORDER", ChannelHint = "쿠팡", ProductName = "상품A", OptionName = "옵션1" },
        };

        var resolvedCount = AutoOrderChannelResolver.ApplyChannelOverrides(items, configs, mappingRepository);

        Assert.AreEqual(1, resolvedCount);
        Assert.AreEqual("COUPANG", items[0].ChannelCode);
        Assert.AreEqual("SKU-001", items[0].MappedSku);
    }

    [TestMethod]
    public void ApplyChannelOverrides_UnresolvedHint_LeavesPresetChannelCodeAndDoesNotCountIt()
    {
        var mappingRepository = new MappingRepository();
        var configs = new List<ChannelConfig>
        {
            new() { ChannelCode = "AUTOORDER", ChannelName = "자동발주(표준)", IsAutoOrderStandardPreset = true },
            new() { ChannelCode = "COUPANG", ChannelName = "쿠팡", AutoOrderHints = "쿠팡" },
        };

        var items = new List<OfsOrderItem>
        {
            new() { ChannelCode = "AUTOORDER", ChannelHint = "알수없는채널", ProductName = "상품A", OptionName = "옵션1" },
        };

        var resolvedCount = AutoOrderChannelResolver.ApplyChannelOverrides(items, configs, mappingRepository);

        Assert.AreEqual(0, resolvedCount);
        Assert.AreEqual("AUTOORDER", items[0].ChannelCode);
    }
}

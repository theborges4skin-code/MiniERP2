using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class AutoOrderChannelResolverTests
{
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
}

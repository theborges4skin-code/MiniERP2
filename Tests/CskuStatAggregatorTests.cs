using MiniERP2.Models;
using MiniERP2.Services;

namespace MiniERP2.Tests;

[TestClass]
public class CskuStatAggregatorTests
{
    private static CskuStatSourceRow Row(
        CskuFileKind kind, string channel, string csku, int qty, decimal revenue, decimal profit,
        string productGroup = "그룹", string productName = "상품", CskuStatRowClass rowClass = CskuStatRowClass.Normal) =>
        new()
        {
            FileKind = kind,
            ChannelCode = channel,
            CskuCode = csku,
            Qty = qty,
            Revenue = revenue,
            Settlement = revenue,
            Profit = profit,
            ProductGroup = productGroup,
            ProductName = productName,
            Status = "매핑(1:1)",
            RowClass = rowClass,
        };

    [TestMethod]
    public void Aggregate_SumsAcrossMultipleRowsOfSameKey()
    {
        var rows = new[]
        {
            Row(CskuFileKind.General, "COUPANG", "CSKU1", 2, 1000m, 200m),
            Row(CskuFileKind.General, "COUPANG", "CSKU1", 3, 1500m, 300m),
        };

        var lines = CskuStatAggregator.Aggregate(rows, code => code);

        var line = lines.Single();
        Assert.AreEqual(2, line.RowCount);
        Assert.AreEqual(5, line.Qty);
        Assert.AreEqual(2500m, line.Revenue);
        Assert.AreEqual(500m, line.Profit);
    }

    [TestMethod]
    public void Aggregate_DifferentChannel_SameCsku_ProducesSeparateLines()
    {
        var rows = new[]
        {
            Row(CskuFileKind.General, "COUPANG", "CSKU1", 1, 1000m, 100m),
            Row(CskuFileKind.General, "NAVER", "CSKU1", 1, 1000m, 100m),
        };

        var lines = CskuStatAggregator.Aggregate(rows, code => code);

        Assert.AreEqual(2, lines.Count);
    }

    [TestMethod]
    public void Aggregate_DifferentFileKind_SameChannelAndCsku_ProducesSeparateLines()
    {
        var rows = new[]
        {
            Row(CskuFileKind.General, "AMZUS", "CSKU1", 1, 1000m, 100m),
            Row(CskuFileKind.Amazon, "AMZUS", "CSKU1", 1, 1000m, 100m),
        };

        var lines = CskuStatAggregator.Aggregate(rows, code => code);

        Assert.AreEqual(2, lines.Count);
    }

    [TestMethod]
    public void Aggregate_ExcludesNonNormalRows()
    {
        var rows = new[]
        {
            Row(CskuFileKind.General, "COUPANG", "CSKU1", 1, 1000m, 100m),
            Row(CskuFileKind.General, "COUPANG", "CSKU1", 1, 1000m, 100m, rowClass: CskuStatRowClass.Excluded),
            Row(CskuFileKind.General, "COUPANG", "CSKU2", 1, 1000m, 100m, rowClass: CskuStatRowClass.Unmapped),
        };

        var lines = CskuStatAggregator.Aggregate(rows, code => code);

        var line = lines.Single();
        Assert.AreEqual("CSKU1", line.CskuCode);
        Assert.AreEqual(1, line.RowCount);
    }

    [TestMethod]
    public void Aggregate_PicksProductGroupAndNameFromHighestRevenueRow()
    {
        var rows = new[]
        {
            Row(CskuFileKind.General, "COUPANG", "CSKU1", 1, 500m, 50m, "그룹A", "상품A"),
            Row(CskuFileKind.General, "COUPANG", "CSKU1", 1, 2000m, 50m, "그룹B", "상품B"),
        };

        var line = CskuStatAggregator.Aggregate(rows, code => code).Single();

        Assert.AreEqual("그룹B", line.ProductGroup);
        Assert.AreEqual("상품B", line.ProductName);
    }

    [TestMethod]
    public void Aggregate_ZeroRevenue_MarginRateIsNull()
    {
        var rows = new[] { Row(CskuFileKind.General, "COUPANG", "CSKU1", 1, 0m, 0m) };

        var line = CskuStatAggregator.Aggregate(rows, code => code).Single();

        Assert.IsNull(line.MarginRate);
    }

    [TestMethod]
    public void Aggregate_UsesChannelNameResolver()
    {
        var rows = new[] { Row(CskuFileKind.General, "COUPANG", "CSKU1", 1, 1000m, 100m) };

        var line = CskuStatAggregator.Aggregate(rows, code => code == "COUPANG" ? "쿠팡" : code).Single();

        Assert.AreEqual("쿠팡", line.ChannelName);
    }
}

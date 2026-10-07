using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Services;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerCskuStatAggregatorTests
{
    private static PartnerCskuStatInput Input(string party, string csku, string msku, decimal qty, decimal unit, decimal cost, string name = "품목") =>
        new(party, new PartnerClosingLine
        {
            CskuCode = csku,
            MasterSku = msku,
            ItemName = name,
            Qty = qty,
            UnitPrice = unit,
            CostPrice = cost,
            Profit = (unit - cost) * qty,
        }, false);

    [TestMethod]
    public void Aggregate_ByCsku_SumsAcrossParties()
    {
        var rows = PartnerCskuStatAggregator.Aggregate(
        [
            Input("푸디", "HW300", "M1", 2, 2500, 1000),
            Input("한결", "HW300", "M1", 3, 2400, 1000),
            Input("한결", "HW4L", "M2", 1, 6100, 3000),
        ], PartnerCskuGroupBy.Csku);

        Assert.AreEqual(2, rows.Count);
        var hw = rows.Single(r => r.Key == "HW300");
        Assert.AreEqual(5, hw.Qty);
        Assert.AreEqual(12200, hw.Supply);
        Assert.AreEqual(5000, hw.Cost);
        Assert.AreEqual(7200, hw.Profit);
        Assert.AreEqual(2, hw.PartyCount);
        Assert.AreEqual("HW300", rows[0].Key); // 이익 큰 순
    }

    [TestMethod]
    public void Aggregate_ByMasterSku_MergesDifferentCskus()
    {
        var rows = PartnerCskuStatAggregator.Aggregate(
        [
            Input("A", "A_HW", "M1", 1, 100, 50),
            Input("B", "B_HW", "M1", 1, 100, 50),
        ], PartnerCskuGroupBy.MasterSku);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("M1", rows[0].Key);
        Assert.AreEqual("A_HW, B_HW", rows[0].CskuCode);
    }

    [TestMethod]
    public void Aggregate_NonItemLines_SortedLastAndFlagged()
    {
        var rows = PartnerCskuStatAggregator.Aggregate(
        [
            Input("A", PartnerClosingRepository.AdjustmentLineCode, "", 1, -5000, 0),
            Input("A", "택배비", "", 3, 3000, 3000),
            Input("A", "X", "M1", 1, 100, 90),
        ], PartnerCskuGroupBy.Csku);

        Assert.AreEqual("X", rows[0].Key);
        Assert.IsTrue(rows.Skip(1).All(r => r.IsNonItem));
    }

    [TestMethod]
    public void ToSourceRows_ManualParty_UsesPartyNameAsChannelAndExcludesShipping()
    {
        var summary = new PartnerClosingSummary
        {
            PartyKey = "MANUAL:1",
            PartyName = "푸디",
            Lines =
            [
                Input("푸디", "GIFT1", "GIFT1", 2, 10000, 6000).Line,
                Input("푸디", "택배비", "", 1, 3000, 3000).Line,
            ],
        };

        var rows = PartnerCskuStatSource.ToSourceRows(summary, "f");

        Assert.AreEqual(2, rows.Count);
        Assert.IsTrue(rows.All(r => r.ChannelCode == "푸디" && r.FileKind == CskuFileKind.Partner));
        Assert.AreEqual(CskuStatRowClass.Normal, rows[0].RowClass);
        Assert.AreEqual(20000, rows[0].Revenue);
        Assert.AreEqual(8000, rows[0].Profit);
        Assert.AreEqual("GIFT1", rows[0].Msku);
        Assert.AreEqual(CskuStatRowClass.Excluded, rows[1].RowClass);
    }

    [TestMethod]
    public void MskuSummarizer_MergesOnlineAndPartnerByMsku_ConvertsAmazon()
    {
        var lines = new[]
        {
            new CskuStatLine { FileKind = CskuFileKind.General, ChannelName = "스마트스토어", CskuCode = "SS_GIFT", Msku = "GIFT1", Qty = 3, Revenue = 30000, Profit = 9000 },
            new CskuStatLine { FileKind = CskuFileKind.Amazon, ChannelName = "아마존", CskuCode = "AMZ_GIFT", Msku = "GIFT1", Qty = 1, Revenue = 10, Profit = 2 },
            new CskuStatLine { FileKind = CskuFileKind.Partner, ChannelName = "푸디", CskuCode = "GIFT1", Msku = "GIFT1", Qty = 2, Revenue = 20000, Profit = 8000 },
            new CskuStatLine { FileKind = CskuFileKind.General, ChannelName = "쿠팡", CskuCode = "NOMSKU", Msku = "", Qty = 1, Revenue = 100, Profit = 10 },
        };

        var rows = CskuStatMskuSummarizer.Summarize(lines, 1400m);

        Assert.AreEqual(2, rows.Count);
        var gift = rows.Single(r => r.Msku == "GIFT1");
        Assert.AreEqual(4, gift.OnlineQty);
        Assert.AreEqual(2, gift.PartnerQty);
        Assert.AreEqual(30000 + 14000, gift.OnlineRevenue);
        Assert.AreEqual(9000 + 2800 + 8000, gift.Profit);
        Assert.AreEqual(3, gift.ChannelCount);
        Assert.IsTrue(rows.Any(r => r.Msku == "(MSKU없음) NOMSKU"));
    }
}

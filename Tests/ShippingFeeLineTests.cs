using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Services;

namespace MiniERP2.Tests;

/// <summary>
/// OFS 배송비 청구(송장별 배송비 라인) — 배송비 라인이 같은 묶음의 상품 라인을 따라 출고확정되는지,
/// 청구 해제 시 배송비 라인만 지워지는지.
/// </summary>
[TestClass]
public class ShippingFeeLineTests
{
    private string _testFolder = string.Empty;
    private const string Channel = "TESTCH-SHIP";

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

    private static (OutboundRepository Repo, ChannelSkuModel ShippingCsku) SeedShipment(string groupKey)
    {
        var shipping = new ShippingFeeLineService().EnsureShippingCsku(Channel, "테스트", 3000m, "test");
        var repo = new OutboundRepository();
        repo.SaveOutbound(
        [
            new OutboundDetail { ChannelCode = Channel, OrderNo = "O1", ShipmentGroupKey = groupKey, MskuCode = "CSKU-A", Qty = 1, SupplyPrice = 10000m, Recipient = "홍길동" },
            new OutboundDetail { ChannelCode = Channel, OrderNo = "O1", ShipmentGroupKey = groupKey, MskuCode = "CSKU-B", Qty = 2, SupplyPrice = 5000m, Recipient = "홍길동" },
            new OutboundDetail { ChannelCode = Channel, OrderNo = "O1", ShipmentGroupKey = groupKey, MskuCode = shipping.CskuCode, Qty = 1, SupplyPrice = 4500m, PurchasePrice = 4500m },
        ]);
        return (repo, shipping);
    }

    private static List<OutboundDetail> Lines(OutboundRepository repo, string groupKey) =>
        repo.GetByShipmentGroupKeys([groupKey], Utils.LineKindScope.All);

    [TestMethod]
    public void EnsureShippingCsku_CreatesOnceThenReuses()
    {
        var service = new ShippingFeeLineService();
        var first = service.EnsureShippingCsku(Channel, "투유", 3000m, "test");
        var second = service.EnsureShippingCsku(Channel, "다른이름", 4500m, "test");

        Assert.AreEqual("투유_ship", first.CskuCode);
        Assert.AreEqual(first.CskuCode, second.CskuCode);
        CollectionAssert.AreEquivalent(new[] { "투유_ship" }, service.GetShippingCskuCodes(Channel).ToArray());
    }

    [TestMethod]
    public void ApplyTrackingNo_OnProductLine_ConfirmsShippingLineOnlyInSameGroup()
    {
        var (repo, shipping) = SeedShipment("G1");
        var lines = Lines(repo, "G1");
        var productA = lines.Single(l => l.MskuCode == "CSKU-A");

        repo.ApplyTrackingNo(productA.Id, "T100");

        var after = Lines(repo, "G1");
        var shippingLine = after.Single(l => l.MskuCode == shipping.CskuCode);
        Assert.AreEqual("T100", shippingLine.TrackingNo);
        Assert.AreEqual("출고확정", shippingLine.Status);
        Assert.IsNotNull(shippingLine.ConfirmedAt);
        // 다른 상품 라인은 건드리지 않는다(운송장 매칭은 상품 라인마다 따로 한다).
        var productB = after.Single(l => l.MskuCode == "CSKU-B");
        Assert.AreEqual("", productB.TrackingNo);
        Assert.IsNull(productB.ConfirmedAt);
    }

    [TestMethod]
    public void MarkAsShippedOn_PropagatesDateToShippingLine()
    {
        var (repo, shipping) = SeedShipment("G2");
        var productA = Lines(repo, "G2").Single(l => l.MskuCode == "CSKU-A");

        repo.MarkAsShippedOn([productA.Id], new DateTime(2026, 9, 15));

        var shippingLine = Lines(repo, "G2").Single(l => l.MskuCode == shipping.CskuCode);
        Assert.AreEqual(new DateTime(2026, 9, 15), shippingLine.ConfirmedAt!.Value.Date);
        // 마감보드 집계에 들어가고 원가=청구액(이익 0)이 유지된다.
        var closing = repo.GetForClosingPeriod(Channel, "2026-09");
        var inClosing = closing.Single(l => l.MskuCode == shipping.CskuCode);
        Assert.AreEqual(4500m, inClosing.SupplyPrice);
        Assert.AreEqual(4500m, inClosing.PurchasePrice);
    }

    [TestMethod]
    public void DeleteShippingFeeLines_RemovesOnlyShippingLine()
    {
        var (repo, shipping) = SeedShipment("G3");

        var deleted = repo.DeleteShippingFeeLines(Channel, ["G3"], [shipping.CskuCode]);

        Assert.AreEqual(1, deleted);
        var remaining = Lines(repo, "G3");
        Assert.HasCount(2, remaining);
        Assert.IsFalse(remaining.Any(l => l.MskuCode == shipping.CskuCode));
    }

    [TestMethod]
    public void IsShippingLine_UsesChannelAndCsku()
    {
        var (_, shipping) = SeedShipment("G4");
        var keys = new ShippingFeeLineService().GetAllShippingKeys();

        Assert.IsTrue(ShippingFeeLineService.IsShippingLine(new OutboundDetail { ChannelCode = Channel, MskuCode = shipping.CskuCode }, keys));
        Assert.IsFalse(ShippingFeeLineService.IsShippingLine(new OutboundDetail { ChannelCode = "OTHER", MskuCode = shipping.CskuCode }, keys));
        Assert.IsFalse(ShippingFeeLineService.IsShippingLine(new OutboundDetail { ChannelCode = Channel, MskuCode = "CSKU-A" }, keys));
    }

    [TestMethod]
    public void UpsertShippingFeeLine_KeepsShipmentMonthAndUpdatesAmountOnRepeat()
    {
        var shipping = new ShippingFeeLineService().EnsureShippingCsku(Channel, "푸디", 3000m, "test");
        var repo = new OutboundRepository();
        repo.AddManualEntries([new OutboundDetail { ChannelCode = Channel, OrderNo = "P1", TrackingNo = "T900", MskuCode = "CSKU-A", Qty = 1, SupplyPrice = 9000m, Recipient = "개인", ConfirmedAt = new DateTime(2026, 8, 30) }]);
        var anchor = repo.GetByTrackingNos(["T900"]).Single();

        repo.UpsertShippingFeeLine(anchor, shipping.CskuCode, "배송비", 3000m, "test");
        repo.UpsertShippingFeeLine(anchor, shipping.CskuCode, "배송비", 4500m, "test");

        // 지금(10월)이 아니라 송장이 나간 8월 마감에 1줄만 잡힌다.
        var august = repo.GetForClosingPeriod(Channel, "2026-08").Where(l => l.MskuCode == shipping.CskuCode).ToList();
        Assert.HasCount(1, august);
        Assert.AreEqual(4500m, august[0].SupplyPrice);
        Assert.AreEqual(4500m, august[0].PurchasePrice);
        Assert.AreEqual("T900", august[0].TrackingNo);
    }

    [TestMethod]
    public void ReconcileEngine_CountsPerChannelAndDetectsCharges()
    {
        var (repo, shipping) = SeedShipment("G5");
        var productA = Lines(repo, "G5").Single(l => l.MskuCode == "CSKU-A");
        repo.ApplyTrackingNo(productA.Id, "T500"); // 배송비 라인도 T500을 받는다
        repo.AddManualEntries([new OutboundDetail { ChannelCode = Channel, OrderNo = "O2", TrackingNo = "T501", MskuCode = "CSKU-A", Qty = 1, SupplyPrice = 10000m, ConfirmedAt = new DateTime(2026, 9, 3) }]);

        var fileRows = new List<TrackingBackfillRow>
        {
            new() { TrackingNo = "T500", Recipient = "홍길동", FreightCost = 3100m },
            new() { TrackingNo = "T500", Recipient = "홍길동", FreightCost = 3100m }, // 합포장 — 같은 송장 두 줄
            new() { TrackingNo = "T501", Recipient = "김철수", FreightCost = 3100m },
            new() { TrackingNo = "T999", Recipient = "모름", FreightCost = 4000m, Label = "기타" },
        };
        var history = repo.GetByTrackingNos(["T500", "T501", "T999"]);
        var shipments = ChannelShipmentReconcileEngine.BuildShipments(fileRows, history,
            new ShippingFeeLineService().GetAllShippingKeys(), new Dictionary<string, string> { [Channel] = "테스트채널" });

        Assert.HasCount(3, shipments);
        var charged = shipments.Single(s => s.TrackingNo == "T500");
        Assert.IsTrue(charged.IsCharged);
        Assert.AreEqual(4500m, charged.ChargedAmount);
        Assert.AreEqual("CSKU-A", charged.Anchor!.MskuCode); // 기준 라인은 배송비가 아닌 상품 라인
        Assert.IsFalse(shipments.Single(s => s.TrackingNo == "T501").IsCharged);
        Assert.IsFalse(shipments.Single(s => s.TrackingNo == "T999").IsRegistered);

        var summary = ChannelShipmentReconcileEngine.Summarize(shipments);
        var channelRow = summary[0];
        Assert.AreEqual("테스트채널", channelRow.GroupName);
        Assert.AreEqual(2, channelRow.ShipmentCount);
        Assert.AreEqual(1, channelRow.ChargedCount);
        Assert.AreEqual(1, channelRow.UnchargedCount);
        Assert.AreEqual(6200m, channelRow.FreightTotal);
        Assert.AreEqual("(미등록) 기타", summary[1].GroupName);
    }

    [TestMethod]
    public void ParsePresets_ReadsCommaSeparatedAmounts()
    {
        CollectionAssert.AreEqual(new[] { 3000m, 4500m }, ShippingFeeLineService.ParsePresets("3000, 4500원, abc, 0, 3000").ToArray());
        Assert.HasCount(0, ShippingFeeLineService.ParsePresets(null));
    }
}

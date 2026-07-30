using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerClosingRepositoryTests
{
    private string _testFolder = string.Empty;
    private PartnerClosingRepository _closingRepo = new();
    private PartnerMasterRepository _masterRepo = new();
    private OutboundRepository _outboundRepo = new();
    private OutboundShipmentRepository _shipmentRepo = new();

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _closingRepo = new PartnerClosingRepository();
        _masterRepo = new PartnerMasterRepository();
        _outboundRepo = new OutboundRepository();
        _shipmentRepo = new OutboundShipmentRepository();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private static OutboundDetail Shipped(string orderNo, string channelCode = "CH01", string msku = "CSKU-1",
        int qty = 2, decimal supplyPrice = 10000m, decimal? purchasePrice = 4000m, decimal? weightKg = null,
        string shipmentGroupKey = "") => new()
    {
        ChannelCode = channelCode,
        OrderNo = orderNo,
        ShipmentGroupKey = shipmentGroupKey,
        TrackingNo = "T-" + orderNo, // 출고확정(ConfirmedAt 채워짐) 유도
        MskuCode = msku,
        Qty = qty,
        SupplyPrice = supplyPrice,
        PurchasePrice = purchasePrice,
        WeightKg = weightKg,
        Recipient = "홍길동",
        ProductName = "테스트품목",
    };

    [TestMethod]
    public void GetSummary_NoClosingYet_ComputesLiveTotalsFromOutboundDetail()
    {
        _outboundRepo.SaveOutbound([Shipped("ORD-1", qty: 2, supplyPrice: 10000m, purchasePrice: 4000m)]);

        var period = DateTime.Now.ToString("yyyy-MM");
        var summary = _closingRepo.GetSummary(period, "CH:CH01");

        Assert.IsNull(summary.ClosingId);
        Assert.AreEqual("미확인", summary.Status);
        Assert.AreEqual(2m, summary.TotalQty);
        Assert.AreEqual(20000m, summary.TotalSupply);
        Assert.AreEqual(8000m, summary.TotalCost);
        Assert.AreEqual(12000m, summary.TotalProfit);
        Assert.HasCount(1, summary.Lines);
    }

    [TestMethod]
    public void Confirm_SnapshotsLinesAndPinsClosingPeriod_SurvivesSourceLineDeletion()
    {
        var saved = _outboundRepo.SaveOutbound([Shipped("ORD-2", qty: 3, supplyPrice: 9000m, purchasePrice: 3000m)]);
        Assert.IsEmpty(saved);
        var period = DateTime.Now.ToString("yyyy-MM");

        var header = _closingRepo.Confirm(period, "CH:CH01", "테스트채널");
        Assert.AreEqual("확정", header.Status);
        Assert.AreEqual(3m, header.TotalQty);
        Assert.AreEqual(27000m, header.TotalSupply);
        Assert.AreEqual(18000m, header.TotalProfit); // (9000-3000)*3

        // 확정 시 원본 라인의 ClosingPeriod가 고정되어야 한다.
        var afterConfirm = _outboundRepo.GetHistory("CH01", DateTime.Now.AddDays(-1), DateTime.Now.AddDays(1)).Single(o => o.OrderNo == "ORD-2");
        Assert.AreEqual(period, afterConfirm.ClosingPeriod);

        // 확정 후 원본 라인이 삭제돼도 스냅샷은 그대로 유지되어야 한다(§5.3 설계 이유).
        _outboundRepo.DeleteByIds([afterConfirm.Id]);
        var summaryAfterDelete = _closingRepo.GetSummary(period, "CH:CH01");
        Assert.AreEqual(3m, summaryAfterDelete.TotalQty);
        Assert.AreEqual(18000m, summaryAfterDelete.TotalProfit);
        Assert.HasCount(1, summaryAfterDelete.Lines);
    }

    [TestMethod]
    public void Cancel_DeletesSnapshotAndRevertsToDraft_ThenLiveAggregateAgain()
    {
        _outboundRepo.SaveOutbound([Shipped("ORD-3", qty: 1, supplyPrice: 5000m, purchasePrice: 1000m)]);
        var period = DateTime.Now.ToString("yyyy-MM");
        var header = _closingRepo.Confirm(period, "CH:CH01", "테스트채널");

        _closingRepo.Cancel(header.Id);

        var reverted = _closingRepo.GetHeader(period, "CH:CH01")!;
        Assert.AreEqual("대조중", reverted.Status);
        Assert.IsNull(reverted.ConfirmedAt);
        Assert.IsEmpty(_closingRepo.GetLinesByClosingId(header.Id));

        // 취소 후에는 다시 라이브 집계로 돌아가야 한다(원본 라인이 아직 살아있으므로 값 그대로).
        var summary = _closingRepo.GetSummary(period, "CH:CH01");
        Assert.AreEqual(1m, summary.TotalQty);
        Assert.AreEqual(4000m, summary.TotalProfit);
    }

    [TestMethod]
    public void Confirm_AllocatesFreightAcrossGroupByWeight_AndExcludesItFromLineProfit()
    {
        // 같은 ShipmentGroupKey를 공유하는 두 라인(2kg, 1kg) — 운임 3000원이 2:1로 배부되어야 한다.
        _outboundRepo.SaveOutbound([
            Shipped("ORD-4A", msku: "CSKU-1", qty: 1, supplyPrice: 10000m, purchasePrice: 4000m, weightKg: 2m, shipmentGroupKey: "GRP-1"),
            Shipped("ORD-4B", msku: "CSKU-2", qty: 1, supplyPrice: 8000m, purchasePrice: 3000m, weightKg: 1m, shipmentGroupKey: "GRP-1"),
        ]);
        _shipmentRepo.Upsert(new OutboundShipmentModel { ShipmentGroupKey = "GRP-1", FreightCost = 3000m });

        var period = DateTime.Now.ToString("yyyy-MM");
        var header = _closingRepo.Confirm(period, "CH:CH01", "테스트채널");

        Assert.AreEqual(3000m, header.FreightAllocated);
        // 라인 이익 자체에는 운임이 반영되지 않아야 한다.
        Assert.AreEqual(6000m + 5000m, header.TotalProfit + header.FreightAllocated);
        Assert.AreEqual(11000m - 3000m, header.TotalProfit);

        var lines = _closingRepo.GetLinesByClosingId(header.Id);
        Assert.AreEqual(6000m, lines.Single(l => l.CskuCode == "CSKU-1").Profit); // (10000-4000)*1, 운임 미반영
        Assert.AreEqual(5000m, lines.Single(l => l.CskuCode == "CSKU-2").Profit); // (8000-3000)*1, 운임 미반영
    }

    [TestMethod]
    public void GetVisiblePartyKeys_IncludesFavoriteAndRecentChannels_ExcludesStaleUnlessIncludeAll()
    {
        var period = DateTime.Now.ToString("yyyy-MM");

        // CH02는 즐겨찾기로 고정(최근 활동 없어도 노출되어야 함).
        _masterRepo.SetFavorite("CH:CH02", true);

        // CH03은 이번 달 실제 활동이 있음(자동 노출).
        _outboundRepo.SaveOutbound([Shipped("ORD-5", channelCode: "CH03")]);

        // CH04는 아주 오래 전에만 활동(3개월 필터에서는 제외되어야 함).
        var old = Shipped("ORD-6", channelCode: "CH04");
        _outboundRepo.SaveOutbound([old]);
        var oldRow = _outboundRepo.GetHistory("CH04", DateTime.Now.AddYears(-2), DateTime.Now).Single();
        _outboundRepo.SetClosingPeriod([oldRow.Id], "2020-01");

        var visible = _closingRepo.GetVisiblePartyKeys(period, includeAll: false);
        CollectionAssert.Contains(visible, "CH:CH02");
        CollectionAssert.Contains(visible, "CH:CH03");
        CollectionAssert.DoesNotContain(visible, "CH:CH04");

        var all = _closingRepo.GetVisiblePartyKeys(period, includeAll: true);
        CollectionAssert.Contains(all, "CH:CH04");
    }

    [TestMethod]
    public void ManualPartner_AddThenConfirm_PersistsTypedTotalsAsSnapshot()
    {
        var partyKey = _masterRepo.AddManualPartner("수기거래처A");
        Assert.IsTrue(partyKey.StartsWith("MANUAL:", StringComparison.Ordinal));

        var period = DateTime.Now.ToString("yyyy-MM");
        _closingRepo.SaveManualDraft(period, partyKey, "수기거래처A", 5m, 50000m, 12000m, "대조중", "발주서 기준");
        var confirmed = _closingRepo.ConfirmManual(period, partyKey, "수기거래처A", 5m, 50000m, 12000m, "발주서 기준");

        Assert.AreEqual("확정", confirmed.Status);
        Assert.IsNotNull(confirmed.ConfirmedAt);

        var summary = _closingRepo.GetSummary(period, partyKey);
        Assert.AreEqual(50000m, summary.TotalSupply);
        Assert.AreEqual(12000m, summary.TotalProfit);
        Assert.IsTrue(summary.IsManual);

        // §8: 활성 수동 거래처는 다음 달에도 자동 노출되어야 한다(승계 배치 없이).
        var nextPeriod = DateTime.ParseExact(period, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture).AddMonths(1).ToString("yyyy-MM");
        CollectionAssert.Contains(_closingRepo.GetVisiblePartyKeys(nextPeriod, includeAll: false), partyKey);
    }

    [TestMethod]
    public void ManualPartner_Deactivated_NoLongerVisible()
    {
        var partyKey = _masterRepo.AddManualPartner("수기거래처B");
        _masterRepo.SetActive(partyKey, false);

        var period = DateTime.Now.ToString("yyyy-MM");
        CollectionAssert.DoesNotContain(_closingRepo.GetVisiblePartyKeys(period, includeAll: false), partyKey);
    }
}

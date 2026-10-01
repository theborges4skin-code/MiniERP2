using MiniERP2.Models;
using MiniERP2.Services;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerShipmentBackfillEngineTests
{
    private static readonly (string, string)[] Known =
    [
        ("아이스버블 면도기세정액 500ml (1+1 묶음) (BR타입)", "투유_rz2set_BR"),
        ("아이스버블 면도기세정액 (500ml x4개 본품만) (BR타입)", "투유_rz500x4_BR"),
        ("아이스버블 면도기클리너 (500ml) (BR타입)", "투유_rz500_BR"),
        ("아이스버블 면도기세정액 BR타입 500ml 1개", "투유_rz500_BR"),
    ];

    private static TrackingBackfillRow Courier(string trk, string name, string date, string product, string memo = "") => new()
    {
        TrackingNo = trk, Recipient = name, ReceivedAt = DateTime.Parse(date), ProductName = product, OrderNoMemo = memo, SourceFileName = "cj.xlsx",
    };

    private static BackfillAnalysis Run(List<TrackingBackfillRow> courier, List<BackfillOrderLine> orders,
        List<OutboundDetail>? registered = null, List<OutboundDetail>? history = null) =>
        PartnerShipmentBackfillEngine.Analyze("2026-09", "CH001", "투유", courier, orders,
            registered ?? [], history ?? [], Known, _ => 1000m);

    [TestMethod]
    [DataRow("아이스버블 면도기세정액 500ml _1_1 묶음_컵_ _BR타입___1개_", "투유_rz2set_BR")]
    [DataRow("아이스버블 면도기세정액 _500ml x4개 본품만_ _BR타입___1개_", "투유_rz500x4_BR")]
    [DataRow("아이스버블 면도기클리너 _500ml_ _BR타입___1개_", "투유_rz500_BR")]
    [DataRow("브라운_BR호환_  3개 _컵    __x 1개_", null)]
    public void GuessCsku_MatchesInvoiceNameDespiteUnderscores(string courierName, string? expected) =>
        Assert.AreEqual(expected, PartnerShipmentBackfillEngine.GuessCsku(courierName, Known));

    [TestMethod]
    public void ParseCourierQty_ReadsTrailingCount()
    {
        Assert.AreEqual(2, PartnerShipmentBackfillEngine.ParseCourierQty("이공이공 핸드워시 _4L__ _머스캣향___2개_"));
        Assert.AreEqual(1, PartnerShipmentBackfillEngine.ParseCourierQty("세척용 유리컵"));
    }

    [TestMethod]
    public void Analyze_UnregisteredWithOrder_UsesOrderLineAndNearestOrderDay()
    {
        var orders = new List<BackfillOrderLine>
        {
            new(new DateTime(2026, 9, 18), "김동욱", "면도기 2개+컵", "", 1, "투유_rz2set_BR", "", "", "0918.xlsx"),
            new(new DateTime(2026, 8, 1), "김동욱", "옛 주문", "", 1, "투유_rz500_BR", "", "", "0801.xlsx"),
        };
        var a = Run([Courier("T1", "김 동욱", "2026-09-18", "아무 이름")], orders);

        var c = a.Candidates.Single();
        Assert.AreEqual("발주서 일치", c.MatchKind);
        Assert.AreEqual("투유_rz2set_BR", c.CskuCode);
        Assert.AreEqual(new DateTime(2026, 9, 18), c.OrderDate);
        Assert.IsTrue(c.Selected);
    }

    [TestMethod]
    public void Analyze_NoOrderFile_OnlyMemoWithChannelNameCounts()
    {
        var a = Run(
        [
            Courier("T1", "김진석", "2026-09-01", "아이스버블 면도기세정액 500ml _1_1 묶음_컵_ _BR타입___1개_", "(주)투유"),
            Courier("T2", "남남", "2026-09-01", "아이스버블 면도기세정액 500ml _1_1 묶음_컵_ _BR타입___1개_", "다른거래처"),
        ], []);

        var c = a.Candidates.Single();
        Assert.AreEqual("T1", c.TrackingNo);
        Assert.AreEqual("운송장만(품목명 추정)", c.MatchKind);
        Assert.AreEqual("투유_rz2set_BR", c.CskuCode);
    }

    [TestMethod]
    public void Analyze_RegisteredButUnshipped_BecomesConfirmCandidate_ShippedIsSkipped()
    {
        var registered = new List<OutboundDetail>
        {
            new() { Id = 7, ChannelCode = "CH001", TrackingNo = "T1", Status = "발주확정", MskuCode = "투유_rz500_BR", Qty = 1, ProductName = "x" },
            new() { Id = 8, ChannelCode = "CH001", TrackingNo = "T2", Status = "출고확정", MskuCode = "투유_rz500_BR", Qty = 1 },
        };
        var a = Run([Courier("T1", "강봉석", "2026-09-22", "p"), Courier("T2", "누구", "2026-09-22", "p")], [], registered);

        var c = a.Candidates.Single();
        Assert.IsTrue(c.IsConfirmExisting);
        CollectionAssert.AreEqual(new List<long> { 7 }, c.ExistingDetailIds);
        Assert.AreEqual(new DateTime(2026, 9, 22), c.ReceivedAt);
    }

    [TestMethod]
    public void Analyze_OrderAlreadyShipped_IsFlaggedAndUnchecked()
    {
        var orders = new List<BackfillOrderLine> { new(new DateTime(2026, 9, 14), "김동현", "면도기", "", 1, "투유_rz2set_BR", "", "", "0914.xlsx") };
        var history = new List<OutboundDetail>
        {
            new() { ChannelCode = "CH001", Recipient = "김동현", Status = "출고확정", ConfirmedAt = new DateTime(2026, 9, 14), TrackingNo = "OLD" },
        };
        var a = Run([Courier("T9", "김동현", "2026-09-28", "브라운 3개")], orders, history: history);

        var c = a.Candidates.Single();
        Assert.IsFalse(c.Selected);
        StringAssert.StartsWith(c.MatchKind, "주문 이미 출고됨");
    }

    [TestMethod]
    public void Analyze_OrderWithoutShipmentAnywhere_IsUnshippedOrder()
    {
        var orders = new List<BackfillOrderLine> { new(new DateTime(2026, 9, 10), "홍길동", "면도기", "", 1, null, "", "", "0910.xlsx") };
        var a = Run([], orders);

        Assert.AreEqual("홍길동", a.UnshippedOrders.Single().Recipient);
    }
}

using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

/// <summary>
/// 발주 파일을 불러왔을 때 기존 발주/출고 이력과 겹치는 줄을 "중복" 열에 어떻게 표시하는지 검증한다.
/// 이 판정은 처리를 막지 않고 사람이 눈으로 판단할 재료만 주는 것이라, "겹치면 반드시 표시된다"와
/// "안 겹치는 줄은 깨끗하게 남는다" 양쪽이 모두 중요하다.
/// </summary>
[TestClass]
public class OrderDuplicateCheckerTests
{
    private static OfsOrderItem NewOrder(string orderNo, string sku, int qty = 1) => new()
    {
        ChannelCode = "CH001",
        OrderNo = orderNo,
        MappedSku = sku,
        Quantity = qty,
        ProductName = "그린필콜 샴푸 1000g",
    };

    private static OutboundDetail NewHistory(string orderNo, string sku, int qty = 1, string status = "발주확정") => new()
    {
        ChannelCode = "CH001",
        OrderNo = orderNo,
        MskuCode = sku,
        Qty = qty,
        Status = status,
        Recipient = "최명규",
        CreatedAt = new DateTime(2026, 9, 20, 14, 0, 0),
    };

    [TestMethod]
    public void 주문번호_SKU_수량까지_같으면_동일_중복으로_표시한다()
    {
        var index = OrderDuplicateChecker.HistoryIndex.Build([NewHistory("342914258", "온_오_ob_gfc1000", 2)]);
        var item = NewOrder("342914258", "온_오_ob_gfc1000", 2);

        Assert.IsTrue(index.Annotate(item));
        Assert.AreEqual(OrderDuplicateChecker.ExactLabel, item.DuplicateNote);
        Assert.IsTrue(OrderDuplicateChecker.IsStrongDuplicate(item.DuplicateNote));
        StringAssert.Contains(item.DuplicateDetail, "342914258");
        StringAssert.Contains(item.DuplicateDetail, "발주확정");
    }

    [TestMethod]
    public void SKU는_같고_수량만_다르면_수량다름으로_표시한다()
    {
        var index = OrderDuplicateChecker.HistoryIndex.Build([NewHistory("342914258", "온_오_ob_gfc1000", 1)]);
        var item = NewOrder("342914258", "온_오_ob_gfc1000", 3);

        index.Annotate(item);

        Assert.AreEqual(OrderDuplicateChecker.QtyDifferentLabel, item.DuplicateNote);
    }

    [TestMethod]
    public void 주문번호만_같으면_약한_중복으로_표시한다()
    {
        var index = OrderDuplicateChecker.HistoryIndex.Build([NewHistory("342914258", "온_오_ob_dcs1000")]);
        var item = NewOrder("342914258", "온_오_ob_gfc1000");

        index.Annotate(item);

        Assert.AreEqual(OrderDuplicateChecker.OrderNoOnlyLabel, item.DuplicateNote);
        Assert.IsFalse(OrderDuplicateChecker.IsStrongDuplicate(item.DuplicateNote));
    }

    [TestMethod]
    public void 미매핑이라_SKU가_비어도_주문번호가_같으면_표시한다()
    {
        var index = OrderDuplicateChecker.HistoryIndex.Build([NewHistory("342914258", "온_오_ob_gfc1000")]);
        var item = NewOrder("342914258", string.Empty);

        index.Annotate(item);

        Assert.AreEqual(OrderDuplicateChecker.OrderNoOnlyLabel, item.DuplicateNote);
    }

    [TestMethod]
    public void 이력에_없는_주문은_표시하지_않는다()
    {
        var index = OrderDuplicateChecker.HistoryIndex.Build([NewHistory("342914258", "온_오_ob_gfc1000")]);
        var item = NewOrder("999999999", "온_오_ob_gfc1000");

        Assert.IsFalse(index.Annotate(item));
        Assert.IsNull(item.DuplicateNote);
        Assert.IsNull(item.DuplicateDetail);
    }

    [TestMethod]
    public void 주문번호가_없는_줄은_표시하지_않는다()
    {
        // 메모행/수동행처럼 주문번호가 비어있는 줄이 전부 "중복"으로 뭉뚱그려지면 안 된다.
        var index = OrderDuplicateChecker.HistoryIndex.Build([NewHistory(string.Empty, "온_오_ob_gfc1000")]);
        var item = NewOrder("  ", "온_오_ob_gfc1000");

        Assert.IsFalse(index.Annotate(item));
        Assert.IsNull(item.DuplicateNote);
    }

    [TestMethod]
    public void 주문번호_앞뒤_공백과_대소문자는_같은_건으로_본다()
    {
        var index = OrderDuplicateChecker.HistoryIndex.Build([NewHistory("A-342914258", "온_오_OB_gfc1000", 2)]);
        var item = NewOrder(" a-342914258 ", " 온_오_ob_gfc1000 ", 2);

        index.Annotate(item);

        Assert.AreEqual(OrderDuplicateChecker.ExactLabel, item.DuplicateNote);
    }

    [TestMethod]
    public void 다시_판정하면_이전_표시를_지운다()
    {
        // 그리드에서 주문번호/SKU를 고친 뒤 다시 판정하는 경로 — 옛 표시가 남아있으면 오판을 부른다.
        var index = OrderDuplicateChecker.HistoryIndex.Build([NewHistory("342914258", "온_오_ob_gfc1000")]);
        var item = NewOrder("342914258", "온_오_ob_gfc1000");
        index.Annotate(item);

        item.OrderNo = "111111111";
        Assert.IsFalse(index.Annotate(item));
        Assert.IsNull(item.DuplicateNote);
        Assert.IsNull(item.DuplicateDetail);
    }

    [TestMethod]
    public void 여러_줄을_한꺼번에_판정하면_표시된_줄_수를_돌려준다()
    {
        var index = OrderDuplicateChecker.HistoryIndex.Build(
        [
            NewHistory("342914258", "온_오_ob_gfc1000"),
            NewHistory("342907924", "온_오_ob_dcs1000", status: "출고확정"),
        ]);

        var items = new List<OfsOrderItem>
        {
            NewOrder("342914258", "온_오_ob_gfc1000"),
            NewOrder("342907924", "온_오_ob_gfc1000"),
            NewOrder("342902439", "온_오_ob_gfc1000"),
        };

        Assert.AreEqual(2, index.Annotate(items));
        Assert.AreEqual(OrderDuplicateChecker.ExactLabel, items[0].DuplicateNote);
        Assert.AreEqual(OrderDuplicateChecker.OrderNoOnlyLabel, items[1].DuplicateNote);
        Assert.IsNull(items[2].DuplicateNote);
    }

    [TestMethod]
    public void 같은_주문번호_이력이_많으면_상세를_잘라_보여준다()
    {
        var history = Enumerable.Range(0, 8)
            .Select(i => NewHistory("342914258", $"온_오_ob_sku{i}"))
            .ToList();
        var index = OrderDuplicateChecker.HistoryIndex.Build(history);
        var item = NewOrder("342914258", "온_오_ob_gfc1000");

        index.Annotate(item);

        StringAssert.Contains(item.DuplicateDetail, "이력 8건");
        StringAssert.Contains(item.DuplicateDetail, "외 3건");
    }

    [TestMethod]
    public void 이력이_없으면_빈_인덱스로_표시한다()
    {
        var index = OrderDuplicateChecker.HistoryIndex.Build([]);

        Assert.IsTrue(index.IsEmpty);
        Assert.IsFalse(index.Annotate(NewOrder("342914258", "온_오_ob_gfc1000")));
    }
}

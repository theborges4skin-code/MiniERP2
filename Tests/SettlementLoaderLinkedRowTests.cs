using MiniERP2.DataLoaders;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class SettlementLoaderLinkedRowTests
{
    private static ChannelConfig Config(string keyHeader = "주문옵션번호") => new()
    {
        ChannelCode = "CH066",
        ChannelName = "온_오늘의집",
        ChannelType = ChannelType.General,
        LinkedRowKeyHeader = keyHeader,
    };

    private static SettlementData Product(string optionNo, string csku, string group = "23.옵시디앙") => new()
    {
        ChannelCode = "CH066",
        ProductName = "데미지케어 샴푸",
        Msku = csku,
        ProductGroup = group,
        Qty = 1,
        Settlement = 15990m,
        Profit = 8583m,
        Status = "매핑(1:1)",
        RawValues = new() { ["주문옵션번호"] = optionNo },
    };

    private static SettlementData Coupon(string optionNo, decimal amount) => new()
    {
        ChannelCode = "CH066",
        ProductName = "옵시디앙 샴푸 10% 쿠폰",
        Qty = 0,
        Settlement = amount,
        Status = "매핑 실패",
        RawValues = new() { ["주문옵션번호"] = optionNo },
    };

    [TestMethod]
    public void UnmappedRow_TakesCskuOfRowWithSameKey_WithZeroQtyAndAmountAsProfit()
    {
        var product = Product("676448818", "온_오_ob_dcs1000");
        var coupon = Coupon("676448818", -1950m);

        SettlementLoader.ApplyLinkedRowMapping([product, coupon], Config());

        Assert.AreEqual("온_오_ob_dcs1000", coupon.Msku);
        Assert.AreEqual("23.옵시디앙", coupon.ProductGroup);
        Assert.AreEqual(SettlementLoader.LinkedRowStatus, coupon.Status);
        Assert.AreEqual(0, coupon.Qty);
        Assert.AreEqual(-1950m, coupon.Profit);
        Assert.AreEqual(8583m, product.Profit, "연결 대상 행은 바뀌지 않아야 한다.");
    }

    [TestMethod]
    public void NoMatchingKey_StaysUnmapped()
    {
        var coupon = Coupon("999", -1950m);

        SettlementLoader.ApplyLinkedRowMapping([Product("676448818", "온_오_ob_dcs1000"), coupon], Config());

        Assert.IsNull(coupon.Msku);
        Assert.AreEqual("매핑 실패", coupon.Status);
    }

    [TestMethod]
    public void EmptyKeyHeader_DoesNothing()
    {
        var coupon = Coupon("676448818", -1950m);

        SettlementLoader.ApplyLinkedRowMapping([Product("676448818", "온_오_ob_dcs1000"), coupon], Config(""));

        Assert.IsNull(coupon.Msku);
    }

    [TestMethod]
    public void PreviouslyLinkedRow_FollowsSourceChange_AndRevertsWhenSourceGone()
    {
        var product = Product("1", "온_오_ob_dcs1000");
        var coupon = Coupon("1", -1950m);
        SettlementLoader.ApplyLinkedRowMapping([product, coupon], Config());

        product.Msku = "온_오_ob_dcs1000x2";
        SettlementLoader.ApplyLinkedRowMapping([product, coupon], Config());
        Assert.AreEqual("온_오_ob_dcs1000x2", coupon.Msku);

        product.Msku = null;
        product.Status = "매핑 실패";
        SettlementLoader.ApplyLinkedRowMapping([product, coupon], Config());
        Assert.IsNull(coupon.Msku);
        Assert.AreEqual("매핑 실패", coupon.Status);
        Assert.AreEqual(0m, coupon.Profit);
    }
}

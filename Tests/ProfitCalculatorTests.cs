using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class ProfitCalculatorTests
{
    [TestMethod]
    public void Calculate_General_SubtractsCostTimesQty()
    {
        // 정산액 10000, 원가 3000, 수량 2 -> 10000 - 3000*2 = 4000
        var profit = ProfitCalculator.Calculate(ChannelType.General, settlement: 10000m, costPrice: 3000m, qty: 2, shipping: 500m, fee: 100m);

        Assert.AreEqual(4000m, profit);
    }

    [TestMethod]
    public void Calculate_CoupangGrowth_AppliesVatToShippingAndFee()
    {
        // 10000 - 3000*2 - (500*1.1) - (100*1.1) = 10000 - 6000 - 550 - 110 = 3340
        var profit = ProfitCalculator.Calculate(ChannelType.CoupangGrowth, settlement: 10000m, costPrice: 3000m, qty: 2, shipping: 500m, fee: 100m);

        Assert.AreEqual(3340m, profit);
    }

    [TestMethod]
    public void Calculate_AmazonUs_ConvertsCostToSupplyPriceAndAppliesExchangeRate()
    {
        // (100 - (11/1.1*2)) * 1300 = (100 - 20) * 1300 = 104000
        var profit = ProfitCalculator.Calculate(ChannelType.AmazonUs, settlement: 100m, costPrice: 11m, qty: 2, shipping: 0m, fee: 0m, exchangeRate: 1300m);

        Assert.AreEqual(104000m, profit);
    }

    [TestMethod]
    public void ApplyCoupangGeneralShippingAggregation_WithoutOrderNo_UsesShippingOnlyRowRevenueAndRemovesThoseRows()
    {
        // OrderNo 미매핑 상황: 배송비 전용 행(Qty=0)의 매출액(Revenue)이 실제 배송비 금액이다.
        // 상품 행(Qty!=0)에 이미 Shipping이 직접 매핑되어 있으면 그 값도 함께 합산한다.
        var rows = new List<SettlementData>
        {
            new() { Qty = 0, Revenue = 100m, Shipping = 0m },
            new() { Qty = 0, Revenue = 200m, Shipping = 0m },
            new() { Qty = 1, Revenue = 0m, Shipping = 50m },
            new() { Qty = 2, Revenue = 0m, Shipping = 0m },
        };

        ProfitCalculator.ApplyCoupangGeneralShippingAggregation(ChannelType.CoupangGeneral, rows);

        Assert.AreEqual(2, rows.Count);
        Assert.IsTrue(rows.All(r => r.Qty != 0));
        Assert.AreEqual(350m, rows[0].Shipping);
        Assert.AreEqual(0m, rows[1].Shipping);
    }

    [TestMethod]
    public void ApplyCoupangGeneralShippingAggregation_WithOrderNo_DistributesShippingOnlyRowRevenuePerOrder()
    {
        // 주문 A: 배송비 전용 행(매출액 3000원) + 상품 행 1개 -> 상품 행이 3000원 전부 수령
        // 주문 B: 배송비 전용 행(매출액 1000원) + 상품 행 2개 -> 상품 행마다 500원씩 분배
        var rows = new List<SettlementData>
        {
            new() { OrderNo = "A", Qty = 0, Revenue = 3000m },
            new() { OrderNo = "A", Qty = 1 },
            new() { OrderNo = "B", Qty = 0, Revenue = 1000m },
            new() { OrderNo = "B", Qty = 1 },
            new() { OrderNo = "B", Qty = 2 },
        };

        ProfitCalculator.ApplyCoupangGeneralShippingAggregation(ChannelType.CoupangGeneral, rows);

        Assert.AreEqual(3, rows.Count);
        Assert.IsTrue(rows.All(r => r.Qty != 0));
        Assert.AreEqual(3000m, rows.Single(r => r.OrderNo == "A").Shipping);
        Assert.IsTrue(rows.Where(r => r.OrderNo == "B").All(r => r.Shipping == 500m));
    }

    [TestMethod]
    public void ApplyCoupangGeneralShippingAggregation_WithOrderNoButNoShippingOnlyRows_FallsBackToFullSumOnFirstRow()
    {
        // 실제 데이터에서 관찰된 버그 재현: OrderNo는 매핑돼 있지만 배송비 전용 행(Qty=0)이 파일에
        // 아예 없는 경우. 예전 코드는 재배분할 대상이 없어 아무 것도 안 하고 리턴해버려 배송비가
        // 전부 0으로 표시됐다. 이제는 매핑된 Shipping 필드를 그대로 합산해 첫 행에 몰아줘야 한다.
        var rows = new List<SettlementData>
        {
            new() { OrderNo = "A", Qty = 1, Shipping = 300m },
            new() { OrderNo = "B", Qty = 2, Shipping = 400m },
        };

        ProfitCalculator.ApplyCoupangGeneralShippingAggregation(ChannelType.CoupangGeneral, rows);

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(700m, rows[0].Shipping);
        Assert.AreEqual(0m, rows[1].Shipping);
    }

    [TestMethod]
    public void ApplyCoupangGeneralShippingAggregation_DoesNothingForOtherChannelTypes()
    {
        var rows = new List<SettlementData>
        {
            new() { Shipping = 100m },
            new() { Shipping = 200m },
        };

        ProfitCalculator.ApplyCoupangGeneralShippingAggregation(ChannelType.General, rows);

        Assert.AreEqual(100m, rows[0].Shipping);
        Assert.AreEqual(200m, rows[1].Shipping);
    }

    [TestMethod]
    public void ApplyElevenStreetFilter_WithOrderNo_MovesShippingToProductRowsAndRemovesShippingOnlyRows()
    {
        // 주문 A: 배송비 전용 행(3000원) + 상품 행 1개 -> 상품 행이 3000원 전부 수령
        // 주문 B: 배송비 전용 행(1000원) + 상품 행 2개 -> 상품 행마다 500원씩 분배
        var rows = new List<SettlementData>
        {
            new() { OrderNo = "A", Qty = 0, Shipping = 3000m },
            new() { OrderNo = "A", Qty = 1, Shipping = 0m },
            new() { OrderNo = "B", Qty = 0, Shipping = 1000m },
            new() { OrderNo = "B", Qty = 1, Shipping = 0m },
            new() { OrderNo = "B", Qty = 2, Shipping = 0m },
        };

        ProfitCalculator.ApplyElevenStreetFilter(ChannelType.ElevenStreet, rows);

        Assert.AreEqual(3, rows.Count);
        Assert.IsTrue(rows.All(r => r.Qty != 0));
        Assert.AreEqual(3000m, rows.Single(r => r.OrderNo == "A").Shipping);
        Assert.IsTrue(rows.Where(r => r.OrderNo == "B").All(r => r.Shipping == 500m));
    }

    [TestMethod]
    public void ApplyElevenStreetFilter_WithoutOrderNo_SumsShippingOntoFirstProductRowAndRemovesShippingOnlyRows()
    {
        var rows = new List<SettlementData>
        {
            new() { Qty = 0, Shipping = 3000m },
            new() { Qty = 1, Shipping = 0m },
            new() { Qty = 1, Shipping = 0m },
        };

        ProfitCalculator.ApplyElevenStreetFilter(ChannelType.ElevenStreet, rows);

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(3000m, rows[0].Shipping);
        Assert.AreEqual(0m, rows[1].Shipping);
    }

    [TestMethod]
    public void ApplyElevenStreetFilter_DoesNothingForOtherChannelTypes()
    {
        var rows = new List<SettlementData>
        {
            new() { Qty = 0, Shipping = 3000m },
            new() { Qty = 1, Shipping = 0m },
        };

        ProfitCalculator.ApplyElevenStreetFilter(ChannelType.General, rows);

        Assert.AreEqual(2, rows.Count);
    }
}

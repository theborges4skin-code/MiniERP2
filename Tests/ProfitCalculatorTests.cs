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
    public void ApplyCoupangGeneralShippingAggregation_MovesTotalShippingToFirstRow()
    {
        var rows = new List<SettlementData>
        {
            new() { Shipping = 100m },
            new() { Shipping = 200m },
            new() { Shipping = 300m },
        };

        ProfitCalculator.ApplyCoupangGeneralShippingAggregation(ChannelType.CoupangGeneral, rows);

        Assert.AreEqual(600m, rows[0].Shipping);
        Assert.AreEqual(0m, rows[1].Shipping);
        Assert.AreEqual(0m, rows[2].Shipping);
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
}

using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class SimpleMarginCalculatorTests
{
    [TestMethod]
    public void SettlementOnly_UsesSettlementMinusCost()
    {
        var result = SimpleMarginCalculator.Calculate(new SimpleMarginCalcInput
        {
            CostPrice = 1000m,
            Quantity = 10m,
            SettlementAmount = 15000m,
        });

        Assert.IsTrue(result.IsComputable);
        Assert.AreEqual(5000m, result.ProfitAmount);   // 15000 - 1000*10
        Assert.AreEqual(500m, result.ProfitPerUnit);   // 5000/10
        Assert.IsNull(result.SalePerUnit);
        Assert.AreEqual(15000m, result.RevenueBasis);  // 정산금액 기준
        Assert.AreEqual(1m / 3m, result.MarginRate);   // 5000/15000
    }

    [TestMethod]
    public void FeeRateOnly_UsesSaleAmountMinusFeeMinusCost()
    {
        var result = SimpleMarginCalculator.Calculate(new SimpleMarginCalcInput
        {
            CostPrice = 1000m,
            Quantity = 10m,
            SaleAmount = 20000m,
            FeeRate = 0.1m,
        });

        Assert.IsTrue(result.IsComputable);
        // 20000 - 20000*0.1 - 1000*10 = 20000 - 2000 - 10000 = 8000
        Assert.AreEqual(8000m, result.ProfitAmount);
        Assert.AreEqual(800m, result.ProfitPerUnit);
        Assert.AreEqual(2000m, result.SalePerUnit);
        Assert.AreEqual(20000m, result.RevenueBasis); // 판매금액 기준
        Assert.AreEqual(0.4m, result.MarginRate);     // 8000/20000
    }

    [TestMethod]
    public void NeitherFeeNorSettlement_UsesSaleAmountMinusCost()
    {
        var result = SimpleMarginCalculator.Calculate(new SimpleMarginCalcInput
        {
            CostPrice = 1000m,
            Quantity = 10m,
            SaleAmount = 20000m,
        });

        Assert.IsTrue(result.IsComputable);
        Assert.AreEqual(10000m, result.ProfitAmount); // 20000 - 10000
        Assert.AreEqual(1000m, result.ProfitPerUnit);
        Assert.AreEqual(2000m, result.SalePerUnit);
    }

    [TestMethod]
    public void BothFeeAndSettlement_SettlementTakesPriority()
    {
        var result = SimpleMarginCalculator.Calculate(new SimpleMarginCalcInput
        {
            CostPrice = 1000m,
            Quantity = 10m,
            SaleAmount = 20000m,
            SettlementAmount = 15000m,
            FeeRate = 0.1m,
        });

        Assert.IsTrue(result.IsComputable);
        Assert.AreEqual(5000m, result.ProfitAmount); // 15000 - 10000 (수수료 무시)
    }

    [TestMethod]
    public void MissingQuantity_NotComputable()
    {
        var result = SimpleMarginCalculator.Calculate(new SimpleMarginCalcInput
        {
            CostPrice = 1000m,
            SaleAmount = 20000m,
        });

        Assert.IsFalse(result.IsComputable);
        Assert.AreEqual("수량 필요", result.Reason);
    }

    [TestMethod]
    public void MissingSaleAndSettlement_NotComputable()
    {
        var result = SimpleMarginCalculator.Calculate(new SimpleMarginCalcInput
        {
            CostPrice = 1000m,
            Quantity = 10m,
        });

        Assert.IsFalse(result.IsComputable);
        Assert.AreEqual("판매금액 또는 정산금액 필요", result.Reason);
    }

    [TestMethod]
    public void ZeroRevenueBasis_MarginRateIsNullNotDivideByZero()
    {
        var result = SimpleMarginCalculator.Calculate(new SimpleMarginCalcInput
        {
            CostPrice = 1000m,
            Quantity = 10m,
            SaleAmount = 0m,
        });

        Assert.IsTrue(result.IsComputable);
        Assert.AreEqual(0m, result.RevenueBasis);
        Assert.IsNull(result.MarginRate);
    }
}

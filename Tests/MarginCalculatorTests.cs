using MiniERP2.Utils;

namespace MiniERP2.Tests;

/// <summary>
/// 간이마진계산기_개발기획서.md §4(계산 모델) 검증. M1 완료 기준 — 정방향/역산/납품가/정액 총액 환산/경계조건.
/// </summary>
[TestClass]
public class MarginCalculatorTests
{
    private static MarginCostItemInput Rate(string name, decimal value) => new() { Name = name, IsRate = true, Value = value };
    private static MarginCostItemInput Flat(string name, decimal value, CostApplyUnit unit) => new() { Name = name, IsRate = false, Value = value, Unit = unit };

    [TestMethod]
    public void Margin_ForwardCalc_MatchesSpecFormula()
    {
        // C=1000, Σr=36%(광고20+물류3+기타3+수수료10 가정 단순화), P=2000
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.Margin,
            CostPrice = 1000m,
            CostItems = new[] { Rate("수수료", 0.36m) },
            SalePrice = 2000m,
        };

        var result = MarginCalculator.Calculate(input);

        Assert.IsTrue(result.IsComputable);
        // 판매원가 = 1000 + 2000*0.36 = 1720
        Assert.AreEqual(1720m, result.CostOfGoods);
        // 이익액 = 2000 - 1720 = 280
        Assert.AreEqual(280m, result.ProfitAmount);
        // 마진율 = 280/2000 = 0.14
        Assert.AreEqual(0.14m, result.MarginRate);
    }

    [TestMethod]
    public void Margin_WithQuantity_ComputesTotals()
    {
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.Margin,
            CostPrice = 1000m,
            CostItems = Array.Empty<MarginCostItemInput>(),
            SalePrice = 2000m,
            Quantity = 5m,
        };

        var result = MarginCalculator.Calculate(input);

        Assert.AreEqual(10000m, result.SaleAmountTotal);   // 5*2000
        Assert.AreEqual(5000m, result.ProfitAmountTotal);  // 5*(2000-1000)
        Assert.AreEqual(5000m, result.CostOfGoodsTotal);   // 5*1000
    }

    [TestMethod]
    public void Margin_SalePriceZero_BlanksDownstreamValues()
    {
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.Margin,
            CostPrice = 1000m,
            SalePrice = 0m,
            Quantity = 5m,
        };

        var result = MarginCalculator.Calculate(input);

        Assert.IsTrue(result.IsComputable);
        Assert.AreEqual(0m, result.SalePrice);
        Assert.IsNull(result.MarginRate);
        Assert.IsNull(result.ProfitAmount);
        Assert.IsNull(result.SaleAmountTotal);
    }

    [TestMethod]
    public void Margin_SalePriceNull_EverythingBlank()
    {
        var input = new MarginCalcInput { Mode = MarginCalcMode.Margin, CostPrice = 1000m, SalePrice = null };

        var result = MarginCalculator.Calculate(input);

        Assert.IsTrue(result.IsComputable);
        Assert.IsNull(result.SalePrice);
        Assert.IsNull(result.MarginRate);
    }

    [TestMethod]
    public void SalePrice_ReverseCalc_MatchesForwardFormula()
    {
        // P = (C + Σa) / (1 - Σr - m). C=1000, Σr=0.36, m=0.14 → P = 1000/0.5 = 2000
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.SalePrice,
            CostPrice = 1000m,
            CostItems = new[] { Rate("수수료", 0.36m) },
            TargetMarginRate = 0.14m,
            RoundingUnit = 1, // 반올림 노이즈 배제하고 순수 역산 검증
        };

        var result = MarginCalculator.Calculate(input);

        Assert.IsTrue(result.IsComputable);
        Assert.AreEqual(2000m, result.SalePrice);
        Assert.AreEqual(0.14m, result.MarginRate);
    }

    [TestMethod]
    public void SalePrice_RoundsUpToConfiguredUnit()
    {
        // C=1000, Σr=0, m=0.5 → 라운딩 전 P = 2000 (나누어떨어지지 않는 값으로 재확인)
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.SalePrice,
            CostPrice = 1001m,
            TargetMarginRate = 0.5m,
            RoundingUnit = 10,
        };
        // 라운딩 전 P = 1001/0.5 = 2002 → 10원 올림 = 2010
        var result = MarginCalculator.Calculate(input);

        Assert.IsTrue(result.IsComputable);
        Assert.AreEqual(2010m, result.SalePrice);
        // §4.7: 반올림된 판매가 기준으로 마진율 재계산 → 목표마진(0.5)과 미세하게 달라지는 게 정상
        Assert.AreNotEqual(0.5m, result.MarginRate);
        Assert.IsTrue(result.MarginRate > 0.5m);
    }

    [TestMethod]
    public void SalePrice_BoundaryCondition_RatesPlusMarginAtOrOverOneHundredPercent_NotComputable()
    {
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.SalePrice,
            CostPrice = 1000m,
            CostItems = new[] { Rate("비용", 0.39m) },
            TargetMarginRate = 0.61m, // 0.39+0.61 = 1.00 → denom=0
        };

        var result = MarginCalculator.Calculate(input);

        Assert.IsFalse(result.IsComputable);
        Assert.IsNotNull(result.Reason);
    }

    [TestMethod]
    public void SalePrice_MissingTargetMargin_NotComputable()
    {
        var input = new MarginCalcInput { Mode = MarginCalcMode.SalePrice, CostPrice = 1000m, TargetMarginRate = null };

        var result = MarginCalculator.Calculate(input);

        Assert.IsFalse(result.IsComputable);
    }

    [TestMethod]
    public void FlatItem_PerUnit_UsedAsIs()
    {
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.Margin,
            CostPrice = 1000m,
            CostItems = new[] { Flat("포장비", 200m, CostApplyUnit.PerUnit) },
            SalePrice = 2000m,
        };

        var result = MarginCalculator.Calculate(input);

        // 판매원가 = 1000 + 200(건당 그대로) = 1200
        Assert.AreEqual(1200m, result.CostOfGoods);
    }

    [TestMethod]
    public void FlatItem_Total_DividedByQuantity()
    {
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.Margin,
            CostPrice = 1000m,
            CostItems = new[] { Flat("배송비", 1000m, CostApplyUnit.Total) },
            SalePrice = 2000m,
            Quantity = 4m,
        };

        var result = MarginCalculator.Calculate(input);

        // a_i = 1000/4 = 250 → 판매원가 = 1000+250 = 1250
        Assert.IsTrue(result.IsComputable);
        Assert.AreEqual(1250m, result.CostOfGoods);
    }

    [TestMethod]
    public void FlatItem_Total_WithoutQuantity_NotComputable_QuantityRequired()
    {
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.Margin,
            CostPrice = 1000m,
            CostItems = new[] { Flat("배송비", 1000m, CostApplyUnit.Total) },
            SalePrice = 2000m,
            Quantity = null,
        };

        var result = MarginCalculator.Calculate(input);

        Assert.IsFalse(result.IsComputable);
        Assert.AreEqual("수량 필요", result.Reason);
    }

    [TestMethod]
    public void FlatItem_Total_WithZeroQuantity_NotComputable()
    {
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.Margin,
            CostPrice = 1000m,
            CostItems = new[] { Flat("배송비", 1000m, CostApplyUnit.Total) },
            SalePrice = 2000m,
            Quantity = 0m,
        };

        var result = MarginCalculator.Calculate(input);

        Assert.IsFalse(result.IsComputable);
    }

    [TestMethod]
    public void Supply_DiscountRateGiven_ComputesSupplyPriceFromRetail()
    {
        // §4.5: 납품가 = L*(1-d)
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.Supply,
            CostPrice = 1000m,
            RetailPrice = 10000m,
            DiscountRate = 0.3m,
        };

        var result = MarginCalculator.Calculate(input);

        Assert.IsTrue(result.IsComputable);
        Assert.AreEqual(7000m, result.SalePrice);
        Assert.AreEqual(0.3m, result.DiscountRate);
        // 마진율 분모는 납품가(P) 기준 — §4.5
        Assert.AreEqual((7000m - 1000m) / 7000m, result.MarginRate);
    }

    [TestMethod]
    public void Supply_SupplyPriceGiven_ComputesDiscountRateFromRetail()
    {
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.Supply,
            CostPrice = 1000m,
            RetailPrice = 10000m,
            SupplyPriceInput = 7000m,
        };

        var result = MarginCalculator.Calculate(input);

        Assert.IsTrue(result.IsComputable);
        Assert.AreEqual(7000m, result.SalePrice);
        Assert.AreEqual(0.3m, result.DiscountRate);
    }

    [TestMethod]
    public void Supply_RetailPriceMissing_SupplyPriceGiven_DiscountBlankButMarginStillComputed()
    {
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.Supply,
            CostPrice = 1000m,
            RetailPrice = null,
            SupplyPriceInput = 7000m,
        };

        var result = MarginCalculator.Calculate(input);

        Assert.IsTrue(result.IsComputable);
        Assert.IsNull(result.DiscountRate);
        Assert.AreEqual((7000m - 1000m) / 7000m, result.MarginRate);
    }

    [TestMethod]
    public void Supply_RetailPriceMissing_DiscountRateGiven_NotComputable()
    {
        // L이 없으면 할인율만으로는 납품가 금액을 낼 수 없다.
        var input = new MarginCalcInput
        {
            Mode = MarginCalcMode.Supply,
            CostPrice = 1000m,
            RetailPrice = null,
            DiscountRate = 0.3m,
        };

        var result = MarginCalculator.Calculate(input);

        Assert.IsFalse(result.IsComputable);
    }

    [TestMethod]
    public void Supply_NeitherDiscountNorPriceGiven_NotComputable()
    {
        var input = new MarginCalcInput { Mode = MarginCalcMode.Supply, CostPrice = 1000m, RetailPrice = 10000m };

        var result = MarginCalculator.Calculate(input);

        Assert.IsFalse(result.IsComputable);
    }
}

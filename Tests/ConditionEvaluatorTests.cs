using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class ConditionEvaluatorTests
{
    [TestMethod]
    public void Matches_AllAndConditionsTrue_ReturnsTrue()
    {
        var details = new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.OptionName, Operator = ConditionOperator.Contains, TargetValue = "500ml 3개", Logic = ConditionLogic.And },
            new() { HeaderField = StdField.OptionName, Operator = ConditionOperator.NotContains, TargetValue = "면도", Logic = ConditionLogic.And },
        };
        var item = new OfsOrderItem { OptionName = "500ml 3개, 사은품:거치대" };

        Assert.IsTrue(ConditionEvaluator.Matches(details, item));
    }

    [TestMethod]
    public void Matches_OneAndConditionFalse_ReturnsFalse()
    {
        var details = new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.OptionName, Operator = ConditionOperator.Contains, TargetValue = "500ml 3개", Logic = ConditionLogic.And },
            new() { HeaderField = StdField.OptionName, Operator = ConditionOperator.NotContains, TargetValue = "거치대", Logic = ConditionLogic.And },
        };
        var item = new OfsOrderItem { OptionName = "500ml 3개, 사은품:거치대" };

        Assert.IsFalse(ConditionEvaluator.Matches(details, item));
    }

    [TestMethod]
    public void Matches_OrConditions_ReturnsTrueIfAnyMatches()
    {
        var details = new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "상품A", Logic = ConditionLogic.Or },
            new() { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "상품B", Logic = ConditionLogic.Or },
        };
        var item = new OfsOrderItem { ProductName = "상품B 한정판" };

        Assert.IsTrue(ConditionEvaluator.Matches(details, item));
    }

    [TestMethod]
    public void Matches_EqualsOperator_RequiresExactMatch()
    {
        var details = new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.Quantity, Operator = ConditionOperator.Equals, TargetValue = "0", Logic = ConditionLogic.And },
        };

        Assert.IsTrue(ConditionEvaluator.Matches(details, new OfsOrderItem { Quantity = 0 }));
        Assert.IsFalse(ConditionEvaluator.Matches(details, new OfsOrderItem { Quantity = 1 }));
    }

    [TestMethod]
    public void Matches_EmptyDetails_ReturnsFalse()
    {
        Assert.IsFalse(ConditionEvaluator.Matches([], new OfsOrderItem()));
    }

    [TestMethod]
    public void Matches_SettlementData_EvaluatesSameAsOfsOrderItem()
    {
        var details = new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "면도기", Logic = ConditionLogic.And },
            new() { HeaderField = StdField.OptionName, Operator = ConditionOperator.NotContains, TargetValue = "세트", Logic = ConditionLogic.And },
        };

        Assert.IsTrue(ConditionEvaluator.Matches(details, new SettlementData { ProductName = "면도기 3개입", OptionName = "단품" }));
        Assert.IsFalse(ConditionEvaluator.Matches(details, new SettlementData { ProductName = "면도기 3개입", OptionName = "세트구성" }));
        Assert.IsFalse(ConditionEvaluator.Matches(details, new SettlementData { ProductName = "샴푸", OptionName = "단품" }));
    }
}

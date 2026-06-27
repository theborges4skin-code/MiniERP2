using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class AdConditionEvaluatorTests
{
    [TestMethod]
    public void Matches_SingleContainsCondition_ReturnsTrueWhenSubstringPresent()
    {
        var details = new List<AdConditionDetail>
        {
            new() { HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = "면도", Logic = ConditionLogic.And },
        };
        var item = new AdSpendItem { ProductName = "전기면도기 세트" };

        Assert.IsTrue(AdConditionEvaluator.Matches(details, item));
    }

    [TestMethod]
    public void Matches_AllAndConditions_RequiresEveryConditionToMatch()
    {
        var details = new List<AdConditionDetail>
        {
            new() { HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = "커피", Logic = ConditionLogic.And },
            new() { HeaderField = AdStdField.OptionName, Operator = AdConditionOperator.Contains, TargetValue = "원두", Logic = ConditionLogic.And },
        };

        Assert.IsTrue(AdConditionEvaluator.Matches(details, new AdSpendItem { ProductName = "커피머신", OptionName = "원두포함" }));
        Assert.IsFalse(AdConditionEvaluator.Matches(details, new AdSpendItem { ProductName = "커피머신", OptionName = "기본형" }));
    }

    [TestMethod]
    public void Matches_AllOrConditions_MatchesIfAnyConditionMatches()
    {
        var details = new List<AdConditionDetail>
        {
            new() { HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = "면도", Logic = ConditionLogic.Or },
            new() { HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = "윤활", Logic = ConditionLogic.Or },
        };

        Assert.IsTrue(AdConditionEvaluator.Matches(details, new AdSpendItem { ProductName = "윤활젤" }));
        Assert.IsFalse(AdConditionEvaluator.Matches(details, new AdSpendItem { ProductName = "샴푸" }));
    }

    [TestMethod]
    public void Matches_NumericGreaterThan_ComparesAsNumbers()
    {
        var details = new List<AdConditionDetail>
        {
            new() { HeaderField = AdStdField.Cost, Operator = AdConditionOperator.GreaterThan, TargetValue = "1000", Logic = ConditionLogic.And },
        };

        Assert.IsTrue(AdConditionEvaluator.Matches(details, new AdSpendItem { Cost = 5000m }));
        Assert.IsFalse(AdConditionEvaluator.Matches(details, new AdSpendItem { Cost = 500m }));
    }

    [TestMethod]
    public void Matches_IsZero_TrueOnlyWhenCostIsZero()
    {
        var details = new List<AdConditionDetail>
        {
            new() { HeaderField = AdStdField.Cost, Operator = AdConditionOperator.IsZero, TargetValue = "", Logic = ConditionLogic.And },
        };

        Assert.IsTrue(AdConditionEvaluator.Matches(details, new AdSpendItem { Cost = 0m }));
        Assert.IsFalse(AdConditionEvaluator.Matches(details, new AdSpendItem { Cost = 1m }));
    }

    [TestMethod]
    public void MatchesException_ContainsOperator_FlagsMatchingRow()
    {
        var rule = new AdExceptionRule { HeaderField = AdStdField.ProductId, Operator = AdConditionOperator.Contains, TargetValue = "합계" };

        Assert.IsTrue(AdConditionEvaluator.MatchesException(rule, new AdSpendItem { ProductId = "전체 합계" }));
        Assert.IsFalse(AdConditionEvaluator.MatchesException(rule, new AdSpendItem { ProductId = "CAMPAIGN-1" }));
    }
}

using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class ConditionRuleSignatureTests
{
    [TestMethod]
    public void Build_SameConditionsInDifferentOrder_ProducesSameSignature()
    {
        var detailsA = new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "면도기", Logic = ConditionLogic.And },
            new() { HeaderField = StdField.OptionName, Operator = ConditionOperator.NotContains, TargetValue = "세트", Logic = ConditionLogic.And },
        };
        var detailsB = new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.OptionName, Operator = ConditionOperator.NotContains, TargetValue = "세트", Logic = ConditionLogic.And },
            new() { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "면도기", Logic = ConditionLogic.And },
        };

        Assert.AreEqual(ConditionRuleSignature.Build("SKU1", detailsA), ConditionRuleSignature.Build("SKU1", detailsB));
    }

    [TestMethod]
    public void Build_DifferentTargetSku_ProducesDifferentSignature()
    {
        var details = new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "면도기", Logic = ConditionLogic.And },
        };

        Assert.AreNotEqual(ConditionRuleSignature.Build("SKU1", details), ConditionRuleSignature.Build("SKU2", details));
    }

    [TestMethod]
    public void Build_DifferentLogic_ProducesDifferentSignature()
    {
        var detailsAnd = new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "면도기", Logic = ConditionLogic.And },
        };
        var detailsOr = new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "면도기", Logic = ConditionLogic.Or },
        };

        Assert.AreNotEqual(ConditionRuleSignature.Build("SKU1", detailsAnd), ConditionRuleSignature.Build("SKU1", detailsOr));
    }

    [TestMethod]
    public void Build_CaseInsensitiveTargetValueAndSku_ProducesSameSignature()
    {
        var detailsLower = new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "razor", Logic = ConditionLogic.And },
        };
        var detailsUpper = new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "RAZOR", Logic = ConditionLogic.And },
        };

        Assert.AreEqual(ConditionRuleSignature.Build("sku1", detailsLower), ConditionRuleSignature.Build("SKU1", detailsUpper));
    }
}

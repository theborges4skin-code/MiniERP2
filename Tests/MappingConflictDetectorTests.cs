using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class MappingConflictDetectorTests
{
    [TestMethod]
    public void Detect_Exact_SameKeyDifferentSku_IsConflict()
    {
        var rules = new List<MappingRule>
        {
            new() { Key = "상품A", TargetSku = "SKU1" },
            new() { Key = "상품A", TargetSku = "SKU2" },
        };

        var conflicts = MappingConflictDetector.Detect(MappingRuleType.Exact, rules);

        Assert.HasCount(1, conflicts);
    }

    [TestMethod]
    public void Detect_Exact_SameKeySameSku_IsNotConflict()
    {
        var rules = new List<MappingRule>
        {
            new() { Key = "상품A", TargetSku = "SKU1" },
            new() { Key = "상품A", TargetSku = "SKU1" },
        };

        var conflicts = MappingConflictDetector.Detect(MappingRuleType.Exact, rules);

        Assert.HasCount(0, conflicts);
    }

    [TestMethod]
    public void Detect_Exact_DifferentKeys_IsNotConflict()
    {
        var rules = new List<MappingRule>
        {
            new() { Key = "상품A", TargetSku = "SKU1" },
            new() { Key = "상품B", TargetSku = "SKU2" },
        };

        var conflicts = MappingConflictDetector.Detect(MappingRuleType.Exact, rules);

        Assert.HasCount(0, conflicts);
    }

    [TestMethod]
    public void Detect_Condition_SubstringOverlapDifferentSku_IsConflict()
    {
        var rules = new List<MappingRule>
        {
            new() { Key = "굿즈", TargetSku = "SKU1" },
            new() { Key = "굿즈-한정판", TargetSku = "SKU2" },
        };

        var conflicts = MappingConflictDetector.Detect(MappingRuleType.Condition, rules);

        Assert.HasCount(1, conflicts);
    }

    [TestMethod]
    public void Detect_Condition_NoOverlap_IsNotConflict()
    {
        var rules = new List<MappingRule>
        {
            new() { Key = "굿즈", TargetSku = "SKU1" },
            new() { Key = "전자기기", TargetSku = "SKU2" },
        };

        var conflicts = MappingConflictDetector.Detect(MappingRuleType.Condition, rules);

        Assert.HasCount(0, conflicts);
    }

    [TestMethod]
    public void GetConflictingKeys_ReturnsAllKeysInvolved()
    {
        var conflicts = new List<MappingConflict>
        {
            new(MappingRuleType.Exact, "A", "SKU1", "B", "SKU2"),
        };

        var keys = MappingConflictDetector.GetConflictingKeys(conflicts);

        Assert.HasCount(2, keys);
        Assert.Contains("A", keys);
        Assert.Contains("B", keys);
    }
}

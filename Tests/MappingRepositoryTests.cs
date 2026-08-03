using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class MappingRepositoryTests
{
    private string _testFolder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    [TestMethod]
    public void UpsertExactRule_NewKey_InsertsRule()
    {
        var repository = new MappingRepository();
        repository.UpsertExactRule("CH1", "상품A옵션1", "SKU-1");

        var rules = repository.GetRules(MappingRuleType.Exact, "CH1");
        Assert.IsTrue(rules.Any(r => r.Key == "상품A옵션1" && r.TargetSku == "SKU-1"));
    }

    [TestMethod]
    public void UpsertExactRule_ExistingKey_UpdatesTargetSkuWithoutDuplicating()
    {
        var repository = new MappingRepository();
        repository.UpsertExactRule("CH1", "상품A옵션1", "SKU-1");
        repository.UpsertExactRule("CH1", "상품A옵션1", "SKU-2");

        var rules = repository.GetRules(MappingRuleType.Exact, "CH1");
        Assert.HasCount(1, rules.Where(r => r.Key == "상품A옵션1"));
        Assert.AreEqual("SKU-2", rules.Single(r => r.Key == "상품A옵션1").TargetSku);
    }

    [TestMethod]
    public void UpsertExactRuleWithQuantityPrice_NewCombo_InsertsFourFieldRule()
    {
        var repository = new MappingRepository();
        repository.UpsertExactRuleWithQuantityPrice("CH1", "상품A옵션1", "SKU-4FIELD", quantity: 2, price: 10000m);

        var rules = repository.GetRules(MappingRuleType.Exact, "CH1");
        var rule = rules.Single(r => r.Key == "상품A옵션1");
        Assert.AreEqual("SKU-4FIELD", rule.TargetSku);
        Assert.AreEqual(2, rule.Quantity);
        Assert.AreEqual(10000m, rule.Price);
    }

    [TestMethod]
    public void UpsertExactRuleWithQuantityPrice_CoexistsWithLegacyRuleOfSameKey()
    {
        var repository = new MappingRepository();
        repository.UpsertExactRule("CH1", "상품A옵션1", "SKU-LEGACY");
        repository.UpsertExactRuleWithQuantityPrice("CH1", "상품A옵션1", "SKU-4FIELD", quantity: 2, price: 10000m);

        var rules = repository.GetRules(MappingRuleType.Exact, "CH1").Where(r => r.Key == "상품A옵션1").ToList();
        Assert.HasCount(2, rules);
        Assert.IsTrue(rules.Any(r => r.TargetSku == "SKU-LEGACY" && r.Quantity == null && r.Price == null));
        Assert.IsTrue(rules.Any(r => r.TargetSku == "SKU-4FIELD" && r.Quantity == 2 && r.Price == 10000m));
    }

    [TestMethod]
    public void UpsertExactRuleWithQuantityPrice_SameChannelKeyQuantityPrice_UpdatesInPlaceWithoutDuplicating()
    {
        var repository = new MappingRepository();
        repository.UpsertExactRuleWithQuantityPrice("CH1", "상품A옵션1", "SKU-OLD", quantity: 2, price: 10000m);
        repository.UpsertExactRuleWithQuantityPrice("CH1", "상품A옵션1", "SKU-NEW", quantity: 2, price: 10000m);

        var rules = repository.GetRules(MappingRuleType.Exact, "CH1").Where(r => r.Key == "상품A옵션1").ToList();
        Assert.HasCount(1, rules);
        Assert.AreEqual("SKU-NEW", rules.Single().TargetSku);
    }

    [TestMethod]
    public void UpsertRule_ExceptionType_InsertsIntoExceptionTableNotExact()
    {
        var repository = new MappingRepository();
        repository.UpsertRule(MappingRuleType.Exception, "CH1", "<기본배송료>", MiniERP2.Mapping.SkuMapper.ExcludedTargetSku);

        var exceptionRules = repository.GetRules(MappingRuleType.Exception, "CH1");
        var exactRules = repository.GetRules(MappingRuleType.Exact, "CH1");

        Assert.IsTrue(exceptionRules.Any(r => r.Key == "<기본배송료>" && r.TargetSku == MiniERP2.Mapping.SkuMapper.ExcludedTargetSku));
        Assert.IsEmpty(exactRules);
    }
}

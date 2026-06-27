using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class AdMappingRepositoryTests
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
    public void UpsertTempRule_SameChannelAndKeyTwice_UpdatesInsteadOfDuplicating()
    {
        var repository = new AdMappingRepository();

        repository.UpsertTempRule("CH-A", "상품A_옵션1_PID1", "01.그룹A");
        repository.UpsertTempRule("CH-A", "상품A_옵션1_PID1", "02.그룹B");

        var rules = repository.GetTempRules("CH-A");
        Assert.HasCount(1, rules);
        Assert.AreEqual("02.그룹B", rules[0].TargetGroup);
    }

    [TestMethod]
    public void DeleteTempRule_RemovesOnlyThatRule()
    {
        var repository = new AdMappingRepository();
        repository.UpsertTempRule("CH-A", "key1", "그룹1");
        repository.UpsertTempRule("CH-A", "key2", "그룹2");
        var toDelete = repository.GetTempRules("CH-A").First(r => r.Key == "key1");

        repository.DeleteTempRule(toDelete.Id);

        var remaining = repository.GetTempRules("CH-A");
        Assert.HasCount(1, remaining);
        Assert.AreEqual("key2", remaining[0].Key);
    }

    [TestMethod]
    public void AddConditionRuleWithDetails_ThenGetAll_RoundTripsConditions()
    {
        var repository = new AdMappingRepository();

        var ruleId = repository.AddConditionRuleWithDetails("CH-A", "면도+윤활", "14.면도",
        [
            new AdConditionDetail { HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = "면도", Logic = ConditionLogic.Or },
            new AdConditionDetail { HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = "윤활", Logic = ConditionLogic.Or },
        ]);

        var all = repository.GetAllConditionRulesWithDetails();
        Assert.HasCount(1, all);
        Assert.AreEqual(ruleId, all[0].Rule.Id);
        Assert.AreEqual("14.면도", all[0].Rule.TargetGroup);
        Assert.HasCount(2, all[0].Details);
    }

    [TestMethod]
    public void ReplaceConditionDetails_OverwritesPreviousDetails()
    {
        var repository = new AdMappingRepository();
        var ruleId = repository.AddConditionRuleWithDetails("CH-A", "key", "그룹A",
            [new AdConditionDetail { HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = "old", Logic = ConditionLogic.And }]);

        repository.ReplaceConditionDetails(ruleId,
            [new AdConditionDetail { HeaderField = AdStdField.OptionName, Operator = AdConditionOperator.Equals, TargetValue = "new", Logic = ConditionLogic.And }]);

        var details = repository.GetConditionDetails(ruleId);
        Assert.HasCount(1, details);
        Assert.AreEqual("new", details[0].TargetValue);
        Assert.AreEqual(AdStdField.OptionName, details[0].HeaderField);
    }

    [TestMethod]
    public void DeleteConditionRule_RemovesRuleAndItsDetails()
    {
        var repository = new AdMappingRepository();
        var ruleId = repository.AddConditionRuleWithDetails("CH-A", "key", "그룹A",
            [new AdConditionDetail { HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = "x", Logic = ConditionLogic.And }]);

        repository.DeleteConditionRule(ruleId);

        Assert.HasCount(0, repository.GetConditionRules("CH-A"));
        Assert.HasCount(0, repository.GetConditionDetails(ruleId));
    }

    [TestMethod]
    public void ExceptionRules_AddAndDelete_RoundTrip()
    {
        var repository = new AdMappingRepository();
        repository.AddExceptionRule(new AdExceptionRule { ChannelCode = "CH-A", HeaderField = AdStdField.ProductId, Operator = AdConditionOperator.Contains, TargetValue = "합계" });

        var rules = repository.GetExceptionRules("CH-A");
        Assert.HasCount(1, rules);

        repository.DeleteExceptionRule(rules[0].Id);
        Assert.HasCount(0, repository.GetExceptionRules("CH-A"));
    }
}

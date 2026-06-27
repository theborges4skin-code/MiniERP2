using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

/// <summary>
/// 광고매핑 우선순위(예외 > 임시 > 조건부)가 레거시 SalesManagerV2(ad_engine.py)와 동일하게
/// 동작하는지 검증한다.
/// </summary>
[TestClass]
public class AdMappingEngineTests
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
    public void ApplyMapping_ExceptionRuleMatches_MarksAsExcludedRegardlessOfOtherRules()
    {
        var repository = new AdMappingRepository();
        repository.AddExceptionRule(new AdExceptionRule { ChannelCode = "CH-A", HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = "합계" });
        repository.UpsertTempRule("CH-A", "합계_총_PID", "01.그룹");

        var engine = new AdMappingEngine(repository, "CH-A");
        var item = new AdSpendItem { ProductName = "합계", OptionName = "총", ProductId = "PID" };

        engine.ApplyMapping(item);

        Assert.AreEqual("예외처리", item.MatchType);
        Assert.IsNull(item.MappedGroup);
    }

    [TestMethod]
    public void ApplyMapping_TempRuleTakesPriorityOverCondition()
    {
        var repository = new AdMappingRepository();
        repository.UpsertTempRule("CH-A", "면도기_옵션1_PID1", "99.임시그룹");
        repository.AddConditionRuleWithDetails("CH-A", "조건규칙", "14.면도",
            [new AdConditionDetail { HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = "면도", Logic = ConditionLogic.And }]);

        var engine = new AdMappingEngine(repository, "CH-A");
        var item = new AdSpendItem { ProductName = "면도기", OptionName = "옵션1", ProductId = "PID1" };

        engine.ApplyMapping(item);

        Assert.AreEqual("임시", item.MatchType);
        Assert.AreEqual("99.임시그룹", item.MappedGroup);
    }

    [TestMethod]
    public void ApplyMapping_NoTempMatch_FallsBackToConditionRule()
    {
        var repository = new AdMappingRepository();
        repository.AddConditionRuleWithDetails("CH-A", "조건규칙", "14.면도",
            [new AdConditionDetail { HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = "면도", Logic = ConditionLogic.And }]);

        var engine = new AdMappingEngine(repository, "CH-A");
        var item = new AdSpendItem { ProductName = "전기면도기", OptionName = "옵션1", ProductId = "PID9" };

        engine.ApplyMapping(item);

        Assert.AreEqual("조건부", item.MatchType);
        Assert.AreEqual("14.면도", item.MappedGroup);
    }

    [TestMethod]
    public void ApplyMapping_NoRuleMatches_MarksAsFailed()
    {
        var repository = new AdMappingRepository();
        var engine = new AdMappingEngine(repository, "CH-A");
        var item = new AdSpendItem { ProductName = "알수없는상품" };

        engine.ApplyMapping(item);

        Assert.AreEqual("매핑 실패", item.Status);
        Assert.IsNull(item.MappedGroup);
    }
}

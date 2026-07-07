using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class AdLegacyMigrationServiceTests
{
    private string _testFolder = string.Empty;
    private string _configFolder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;

        _configFolder = Path.Combine(_testFolder, "legacy_config");
        Directory.CreateDirectory(_configFolder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    [TestMethod]
    public void Migrate_ConditionRulesFile_ImportsAsConditionRulesWithTranslatedHeaders()
    {
        File.WriteAllText(Path.Combine(_configFolder, "ad_channels_config.json"), """
            [
              { "channel_name": "쿠팡일반", "header_row": 1, "mapping": {
                  "AD_PRODUCT_NAME": { "col": "광고집행 상품명" },
                  "AD_COST": { "col": "집행 광고비" }
              } }
            ]
            """);
        File.WriteAllText(Path.Combine(_configFolder, "ad_condition_rules.json"), """
            [
              { "target_group": "14.면도", "conditions": [
                  { "header": "광고집행 상품명", "op": "contains", "val": "면도", "logic": "AND" }
              ], "count": 0 }
            ]
            """);

        new SalesChannelRepository().Upsert(new SalesChannel { ChannelCode = "CH-COUPANG", ChannelName = "쿠팡일반" });

        var service = new AdLegacyMigrationService();
        var result = service.Migrate(_configFolder, "CH-COUPANG");

        Assert.AreEqual(1, result.ConditionRulesImported);
        Assert.HasCount(0, result.UntranslatedHeaders);

        var imported = new AdMappingRepository().GetConditionRules("CH-COUPANG").Single();
        Assert.AreEqual("14.면도", imported.TargetGroup);
        var details = new AdMappingRepository().GetConditionDetails(imported.Id);
        Assert.HasCount(1, details);
        Assert.AreEqual(AdStdField.ProductName, details[0].HeaderField);
        Assert.AreEqual("면도", details[0].TargetValue);
    }

    [TestMethod]
    public void Migrate_UnknownHeader_FallsBackToProductNameAndReportsUntranslated()
    {
        File.WriteAllText(Path.Combine(_configFolder, "ad_condition_rules.json"), """
            [
              { "target_group": "20.기타", "conditions": [
                  { "header": "알수없는헤더", "op": "contains", "val": "x", "logic": "AND" }
              ], "count": 0 }
            ]
            """);

        var service = new AdLegacyMigrationService();
        var result = service.Migrate(_configFolder, "CH-A");

        Assert.AreEqual(1, result.ConditionRulesImported);
        Assert.Contains("알수없는헤더", result.UntranslatedHeaders);

        var details = new AdMappingRepository().GetConditionRules("CH-A")
            .SelectMany(r => new AdMappingRepository().GetConditionDetails(r.Id))
            .ToList();
        Assert.AreEqual(AdStdField.ProductName, details[0].HeaderField);
    }

    [TestMethod]
    public void Migrate_ExceptionRulesFile_TranslatesStandardFieldNameDirectly()
    {
        File.WriteAllText(Path.Combine(_configFolder, "ad_exception_rules.json"), """
            [
              { "enabled": true, "header": "AD_PRODUCT_ID", "op": "contains", "val": "합계", "created_at": "2026-03-03 09:27:34" }
            ]
            """);

        var service = new AdLegacyMigrationService();
        var result = service.Migrate(_configFolder, "CH-A");

        Assert.AreEqual(1, result.ExceptionRulesImported);
        var rule = new AdMappingRepository().GetExceptionRules("CH-A").Single();
        Assert.AreEqual(AdStdField.ProductId, rule.HeaderField);
        Assert.AreEqual(AdConditionOperator.Contains, rule.Operator);
        Assert.AreEqual("합계", rule.TargetValue);
    }

    [TestMethod]
    public void Migrate_ChannelConfigFile_FillsFieldMappingsOnlyForMatchingChannelNames()
    {
        File.WriteAllText(Path.Combine(_configFolder, "ad_channels_config.json"), """
            [
              { "channel_name": "쿠팡일반", "header_row": 1, "mapping": { "AD_PRODUCT_NAME": { "col": "상품명" } } },
              { "channel_name": "존재안하는채널", "header_row": 1, "mapping": { "AD_PRODUCT_NAME": { "col": "상품명" } } }
            ]
            """);
        new SalesChannelRepository().Upsert(new SalesChannel { ChannelCode = "CH-COUPANG", ChannelName = "쿠팡일반" });

        var service = new AdLegacyMigrationService();
        var result = service.Migrate(_configFolder, "CH-COUPANG");

        Assert.AreEqual(1, result.ChannelFieldMappingsImported);
        Assert.Contains("존재안하는채널", result.UnmatchedChannelNames);

        var config = new ChannelConfigService().Load().Single(c => c.ChannelCode == "CH-COUPANG");
        Assert.IsTrue(config.AdFileLayouts.Count > 0, "레이아웃이 생성되어야 합니다.");
        Assert.AreEqual("상품명", config.AdFileLayouts[0].FieldMappings[AdStdField.ProductName].Column);
    }

    [TestMethod]
    public void Migrate_MissingFiles_ReturnsZeroCountsWithoutThrowing()
    {
        var service = new AdLegacyMigrationService();

        var result = service.Migrate(_configFolder, "CH-A");

        Assert.AreEqual(0, result.ConditionRulesImported);
        Assert.AreEqual(0, result.ExceptionRulesImported);
        Assert.AreEqual(0, result.ChannelFieldMappingsImported);
    }
}

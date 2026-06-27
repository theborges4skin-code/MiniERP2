using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class SalesChannelLegacyMigrationServiceTests
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
    public void Migrate_NewChannelName_CreatesChannelWithGeneratedCode()
    {
        File.WriteAllText(Path.Combine(_configFolder, "channels_config.json"), """
            [
              {
                "channel_name": "쿠팡일반",
                "channel_code": "443F05E4",
                "channel_type": "COUPANG_NORMAL",
                "exchange_rate": 1.0,
                "global_header_row": 1,
                "mapping": {
                  "STD_PRODUCT_NAME": { "col": "상품명", "conditions": [] },
                  "STD_OPTION": { "col": "옵션명", "conditions": [] },
                  "STD_QTY": { "col": "수량", "conditions": [] },
                  "STD_SETTLEMENT": { "col": "정산금액", "conditions": [] },
                  "STD_SHIPPING": { "col": "배송비", "conditions": [] },
                  "STD_FEE": { "col": "기타비용", "conditions": [] }
                }
              }
            ]
            """);

        var service = new SalesChannelLegacyMigrationService();
        var result = service.Migrate(_configFolder);

        Assert.Contains("쿠팡일반", result.CreatedChannels);
        var channel = new SalesChannelRepository().GetAll().Single(c => c.ChannelName == "쿠팡일반");
        Assert.AreEqual("CH001", channel.ChannelCode);

        var config = new ChannelConfigService().Load().Single(c => c.ChannelCode == "CH001");
        Assert.AreEqual(ChannelType.CoupangGeneral, config.ChannelType);
        Assert.AreEqual("상품명", config.SettlementFieldMappings[StdField.ProductName].Column);
        Assert.AreEqual("정산금액", config.SettlementFieldMappings[StdField.SettlementAmount].Column);
        Assert.AreEqual("기타비용", config.SettlementFieldMappings[StdField.HandlingFee].Column);
    }

    [TestMethod]
    public void Migrate_ExistingChannelName_UpdatesItsConfigInsteadOfCreatingDuplicate()
    {
        var repository = new SalesChannelRepository();
        repository.Upsert(new SalesChannel { ChannelCode = "CH050", ChannelName = "스마트" });

        File.WriteAllText(Path.Combine(_configFolder, "channels_config.json"), """
            [
              { "channel_name": "스마트", "channel_type": "GENERAL", "exchange_rate": 1.0, "global_header_row": 1,
                "mapping": { "STD_PRODUCT_NAME": { "col": "상품명", "conditions": [] } } }
            ]
            """);

        var service = new SalesChannelLegacyMigrationService();
        var result = service.Migrate(_configFolder);

        Assert.Contains("스마트", result.UpdatedChannels);
        Assert.HasCount(1, repository.GetAll());

        var config = new ChannelConfigService().Load().Single(c => c.ChannelCode == "CH050");
        Assert.AreEqual("상품명", config.SettlementFieldMappings[StdField.ProductName].Column);
    }

    [TestMethod]
    public void Migrate_ConditionalFieldEntry_SkipsAndReportsInsteadOfMismapping()
    {
        File.WriteAllText(Path.Combine(_configFolder, "channels_config.json"), """
            [
              { "channel_name": "쿠팡그로스", "channel_type": "COUPANG_GROWTH", "exchange_rate": 1.0, "global_header_row": 2,
                "mapping": {
                  "STD_SHIPPING": { "col": "", "conditions": [
                      { "result_type": "column", "result_value": "판매액", "criteria": [ { "header": "옵션 ID", "op": "contains", "val": "배송", "logic": "AND" } ] }
                  ] }
                },
                "growth_aux_sources": [
                  { "enabled": "Y", "target_std_field": "STD_FEE", "sheet_name": "입출고비", "header_row": 8, "key_header": "옵션ID", "value_header": "할인적용가(A-B)", "out_col": "할인적용가(A-B)" }
                ]
              }
            ]
            """);

        var service = new SalesChannelLegacyMigrationService();
        var result = service.Migrate(_configFolder);

        Assert.Contains("쿠팡그로스/STD_SHIPPING", result.UnsupportedConditionalFields);

        var config = new ChannelConfigService().Load().Single(c => c.ChannelName == "쿠팡그로스");
        Assert.IsFalse(config.SettlementFieldMappings.ContainsKey(StdField.ShippingFee));
        Assert.AreEqual(ChannelType.CoupangGrowth, config.ChannelType);
        Assert.HasCount(1, config.GrowthAuxSources);
        Assert.IsTrue(config.GrowthAuxSources[0].Enabled);
        Assert.AreEqual(StdField.HandlingFee, config.GrowthAuxSources[0].TargetStdField);
        Assert.AreEqual("입출고비", config.GrowthAuxSources[0].SheetName);
    }

    [TestMethod]
    public void Migrate_MissingFile_ReturnsWarningWithoutThrowing()
    {
        var service = new SalesChannelLegacyMigrationService();

        var result = service.Migrate(_configFolder);

        Assert.HasCount(0, result.CreatedChannels);
        Assert.HasCount(1, result.Warnings);
    }
}

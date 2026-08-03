using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class SkuMapperTests
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
    public void ApplyMapping_ExceptionWithExcludedMarker_ExcludesWithoutSku()
    {
        var repository = new MappingRepository();
        repository.SaveRules(MappingRuleType.Exception, "CH1", [new MappingRule { Key = "기본배송료", TargetSku = SkuMapper.ExcludedTargetSku }]);

        var mapper = new SkuMapper(repository, "CH1");
        var item = new OfsOrderItem { ProductName = "<기본배송료>", OptionName = "" };

        mapper.ApplyMapping(item);

        Assert.IsNull(item.MappedSku);
        Assert.AreEqual("제외(배송비 등)", item.Status);
    }

    [TestMethod]
    public void ApplyMapping_ExactRule_TakesPriorityOverCondition()
    {
        var repository = new MappingRepository();
        repository.SaveRules(MappingRuleType.Exact, "CH1", [new MappingRule { Key = "상품A옵션1", TargetSku = "SKU-EXACT" }]);
        repository.SaveRules(MappingRuleType.Condition, "CH1", [new MappingRule { Key = "상품A", TargetSku = "SKU-COND" }]);

        var mapper = new SkuMapper(repository, "CH1");
        var item = new OfsOrderItem { ProductName = "상품A", OptionName = "옵션1" };

        mapper.ApplyMapping(item);

        Assert.AreEqual("SKU-EXACT", item.MappedSku);
        Assert.AreEqual("매핑(1:1)", item.Status);
    }

    [TestMethod]
    public void ApplyMapping_ConditionRuleWithMultipleAndDetails_MapsWhenAllMatch()
    {
        var repository = new MappingRepository();
        repository.AddConditionRuleWithDetails("CH1", "요약: 500ml 3세트", "SKU-COND", new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.OptionName, Operator = ConditionOperator.Contains, TargetValue = "500ml 3개", Logic = ConditionLogic.And },
            new() { HeaderField = StdField.OptionName, Operator = ConditionOperator.NotContains, TargetValue = "면도", Logic = ConditionLogic.And },
        });

        var mapper = new SkuMapper(repository, "CH1");
        var item = new OfsOrderItem { ProductName = "샴푸", OptionName = "500ml 3개, 사은품 포함" };

        mapper.ApplyMapping(item);

        Assert.AreEqual("SKU-COND", item.MappedSku);
        Assert.AreEqual("매핑(조건)", item.Status);
    }

    [TestMethod]
    public void ApplyMapping_ConditionRuleWithDetails_DoesNotMapWhenConditionFails()
    {
        var repository = new MappingRepository();
        repository.AddConditionRuleWithDetails("CH1", "요약", "SKU-COND", new List<MappingConditionDetail>
        {
            new() { HeaderField = StdField.OptionName, Operator = ConditionOperator.Contains, TargetValue = "500ml 3개", Logic = ConditionLogic.And },
            new() { HeaderField = StdField.OptionName, Operator = ConditionOperator.NotContains, TargetValue = "사은품", Logic = ConditionLogic.And },
        });

        var mapper = new SkuMapper(repository, "CH1");
        var item = new OfsOrderItem { ProductName = "샴푸", OptionName = "500ml 3개, 사은품 포함" };

        mapper.ApplyMapping(item);

        Assert.IsNull(item.MappedSku);
        Assert.AreEqual("매핑 실패", item.Status);
    }

    [TestMethod]
    public void ApplyMapping_NoMatchingRule_SetsMappingFailed()
    {
        var mapper = new SkuMapper(new MappingRepository(), "CH1");
        var item = new OfsOrderItem { ProductName = "알수없는상품", OptionName = "" };

        mapper.ApplyMapping(item);

        Assert.IsNull(item.MappedSku);
        Assert.AreEqual("매핑 실패", item.Status);
    }

    [TestMethod]
    public void ApplyMapping_MappedSkuHasInvoiceDisplayName_FillsInvoiceDisplayNameWithoutQuantity()
    {
        // 수량은 더 이상 여기서 붙지 않는다 — 택배사별 수량 표기 형식이 내보내기 시점에 따로 붙는다
        // (Utils/ShipmentGrouping.cs 참고).
        var mappingRepository = new MappingRepository();
        mappingRepository.SaveRules(MappingRuleType.Exact, "CH1", [new MappingRule { Key = "상품A옵션1", TargetSku = "SKU-1" }]);

        var channelSkuRepository = new ChannelSkuRepository();
        channelSkuRepository.Upsert(new ChannelSkuModel { ChannelCode = "CH1", CskuCode = "SKU-1", Msku = "SKU-1", SupplyPrice = 1000m, InvoiceDisplayName = "샴푸 500ml" });

        var mapper = new SkuMapper(mappingRepository, "CH1", channelSkuRepository);
        var item = new OfsOrderItem { ProductName = "상품A", OptionName = "옵션1", Quantity = 3 };

        mapper.ApplyMapping(item);

        Assert.AreEqual("SKU-1", item.MappedSku);
        Assert.AreEqual("샴푸 500ml", item.InvoiceDisplayName);
        Assert.IsNull(item.InvoiceLabel);
    }

    [TestMethod]
    public void ApplyMapping_FourFieldExactRule_TakesPriorityOverLegacyRuleWithSameKey()
    {
        // 상품명+옵션명은 같지만 가격으로 구분되는 두 옵션 — 매핑시스템 통합개편 기획서 §4.1.
        var repository = new MappingRepository();
        repository.SaveRules(MappingRuleType.Exact, "CH1",
        [
            new MappingRule { Key = "상품A옵션1", TargetSku = "SKU-LEGACY" },
            new MappingRule { Key = "상품A옵션1", TargetSku = "SKU-4FIELD", Quantity = 2, Price = 10000m },
        ]);

        var mapper = new SkuMapper(repository, "CH1");
        var item = new OfsOrderItem { ProductName = "상품A", OptionName = "옵션1", Quantity = 2, Revenue = 10000m };

        mapper.ApplyMapping(item);

        Assert.AreEqual("SKU-4FIELD", item.MappedSku);
        Assert.AreEqual("매핑(1:1)", item.Status);
    }

    [TestMethod]
    public void ApplyMapping_FourFieldExactRule_QuantityOrPriceMismatch_FallsBackToLegacyRule()
    {
        var repository = new MappingRepository();
        repository.SaveRules(MappingRuleType.Exact, "CH1",
        [
            new MappingRule { Key = "상품A옵션1", TargetSku = "SKU-LEGACY" },
            new MappingRule { Key = "상품A옵션1", TargetSku = "SKU-4FIELD", Quantity = 2, Price = 10000m },
        ]);

        var mapper = new SkuMapper(repository, "CH1");
        // 수량은 규칙과 같지만(2) 매출액이 다름(9000 != 10000) → 4필드 규칙 불일치, 레거시로 폴백.
        var item = new OfsOrderItem { ProductName = "상품A", OptionName = "옵션1", Quantity = 2, Revenue = 9000m };

        mapper.ApplyMapping(item);

        Assert.AreEqual("SKU-LEGACY", item.MappedSku);
        Assert.AreEqual("매핑(1:1)", item.Status);
    }

    [TestMethod]
    public void ApplyMapping_ItemWithNoRevenue_NeverMatchesFourFieldRule_UsesLegacyOnly()
    {
        // 발주서에 매출액 열이 매핑 안 된 채널(item.Revenue == null)은 애초에 4필드 후보가 될 수
        // 없으므로 자동으로 레거시 경로로만 처리되어야 한다(기획서 §4.1).
        var repository = new MappingRepository();
        repository.SaveRules(MappingRuleType.Exact, "CH1",
        [
            new MappingRule { Key = "상품A옵션1", TargetSku = "SKU-LEGACY" },
            new MappingRule { Key = "상품A옵션1", TargetSku = "SKU-4FIELD", Quantity = 2, Price = 10000m },
        ]);

        var mapper = new SkuMapper(repository, "CH1");
        var item = new OfsOrderItem { ProductName = "상품A", OptionName = "옵션1", Quantity = 2, Revenue = null };

        mapper.ApplyMapping(item);

        Assert.AreEqual("SKU-LEGACY", item.MappedSku);
    }

    [TestMethod]
    public void ApplyMapping_MappedSkuHasNoInvoiceDisplayName_LeavesInvoiceDisplayNameNull()
    {
        var mappingRepository = new MappingRepository();
        mappingRepository.SaveRules(MappingRuleType.Exact, "CH1", [new MappingRule { Key = "상품A옵션1", TargetSku = "SKU-1" }]);

        var mapper = new SkuMapper(mappingRepository, "CH1", new ChannelSkuRepository());
        var item = new OfsOrderItem { ProductName = "상품A", OptionName = "옵션1", Quantity = 1 };

        mapper.ApplyMapping(item);

        Assert.AreEqual("SKU-1", item.MappedSku);
        Assert.IsNull(item.InvoiceDisplayName);
    }
}

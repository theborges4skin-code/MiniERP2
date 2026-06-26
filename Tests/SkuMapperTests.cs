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
    public void ApplyMapping_NoMatchingRule_SetsMappingFailed()
    {
        var mapper = new SkuMapper(new MappingRepository(), "CH1");
        var item = new OfsOrderItem { ProductName = "알수없는상품", OptionName = "" };

        mapper.ApplyMapping(item);

        Assert.IsNull(item.MappedSku);
        Assert.AreEqual("매핑 실패", item.Status);
    }
}

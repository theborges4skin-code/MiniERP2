using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.DataLoaders;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

/// <summary>
/// 1회성 대량구매 묶음 코드 "{마스터SKU}x{수량}" — 마스터SKU 등록 없이 마감/이익분석에서
/// 기준 마스터SKU 원가 × 수량으로 이익이 계산되는지 검증한다.
/// </summary>
[TestClass]
public class BundleSkuCodeTests
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
    public void TryParse_BundleCode_SplitsBaseAndQuantity()
    {
        Assert.IsTrue(BundleSkuCode.TryParse("26c_lgmjnb72x47", out var baseSku, out var qty));
        Assert.AreEqual("26c_lgmjnb72", baseSku);
        Assert.AreEqual(47, qty);
    }

    [TestMethod]
    public void TryParse_NotBundleCode_ReturnsFalse()
    {
        Assert.IsFalse(BundleSkuCode.TryParse("26c_lgmjnb72", out _, out _));
        Assert.IsFalse(BundleSkuCode.TryParse("x5", out _, out _));
        Assert.IsFalse(BundleSkuCode.TryParse("ABCx0", out _, out _));
        Assert.IsFalse(BundleSkuCode.TryParse(null, out _, out _));
    }

    [TestMethod]
    public void ApplyMappingAndProfit_UnregisteredBundleCode_UsesBaseCostTimesQuantity()
    {
        new ItemRepository().Upsert(new ItemModel { Sku = "BASE", ItemName = "기준상품", CostPrice = 1000m, ProductGroup = "21.선물" });
        var mappingRepository = new MappingRepository();
        mappingRepository.UpsertRule(MappingRuleType.Temp, "CH01", "대량구매", "BASEx47");

        var data = new SettlementData { ProductName = "대량구매", OptionName = "", Qty = 1, Settlement = 100000m };
        var channelConfig = new ChannelConfig { ChannelCode = "CH01", ChannelName = "테스트채널", ChannelType = ChannelType.General };
        var channelSkuRepository = new ChannelSkuRepository();

        SettlementLoader.ApplyMappingAndProfit(data, new SkuMapper(mappingRepository, "CH01", channelSkuRepository),
            new ItemRepository(), channelConfig, channelSkuRepository);

        Assert.AreEqual("BASEx47", data.Msku);
        Assert.AreEqual("매핑(임시)", data.Status);
        Assert.AreEqual("21.선물", data.ProductGroup);
        // 100000 - 1000 * 47 = 53000
        Assert.AreEqual(53000m, data.Profit);
    }

    [TestMethod]
    public void ApplyMappingAndProfit_RegisteredCodeLookingLikeBundle_UsesRegisteredCost()
    {
        var itemRepository = new ItemRepository();
        itemRepository.Upsert(new ItemModel { Sku = "BASE", ItemName = "기준상품", CostPrice = 1000m });
        itemRepository.Upsert(new ItemModel { Sku = "BASEx5", ItemName = "5개입 정식상품", CostPrice = 4500m });
        var mappingRepository = new MappingRepository();
        mappingRepository.UpsertRule(MappingRuleType.Temp, "CH01", "5개입", "BASEx5");

        var data = new SettlementData { ProductName = "5개입", OptionName = "", Qty = 1, Settlement = 10000m };
        var channelConfig = new ChannelConfig { ChannelCode = "CH01", ChannelName = "테스트채널", ChannelType = ChannelType.General };
        var channelSkuRepository = new ChannelSkuRepository();

        SettlementLoader.ApplyMappingAndProfit(data, new SkuMapper(mappingRepository, "CH01", channelSkuRepository),
            itemRepository, channelConfig, channelSkuRepository);

        Assert.AreEqual(5500m, data.Profit);
    }
}

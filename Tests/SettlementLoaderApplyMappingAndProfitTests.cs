using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.DataLoaders;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

/// <summary>
/// SettlementForm의 "매핑 SKU" 즉석 1:1 매핑(인라인 자동완성 입력)이 의존하는 재사용 경로를
/// 검증한다: 행 하나에 새 1:1 규칙을 추가한 뒤 ApplyMappingAndProfit을 다시 호출하면, 정산파일을
/// 다시 불러오지 않고도 그 행의 Msku/Status/Profit이 즉시 올바르게 갱신되어야 한다.
/// </summary>
[TestClass]
public class SettlementLoaderApplyMappingAndProfitTests
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
    public void ApplyMappingAndProfit_AfterAddingExactRule_RecomputesUnmappedRowInPlace()
    {
        var itemRepository = new ItemRepository();
        itemRepository.Upsert(new ItemModel { Sku = "SKU1", ItemName = "상품A", CostPrice = 1000m });

        var mappingRepository = new MappingRepository();
        var channelConfig = new ChannelConfig { ChannelCode = "CH1", ChannelName = "테스트채널", ChannelType = ChannelType.General };

        // 처음엔 매핑 규칙이 없어 미매핑 상태로 시작한다(정산파일 로드 직후의 상황과 동일).
        var data = new SettlementData { ChannelCode = "CH1", ProductName = "상품A", OptionName = "옵션1", Qty = 2, Settlement = 10000m };
        var skuMapperBefore = new SkuMapper(mappingRepository, "CH1");
        SettlementLoader.ApplyMappingAndProfit(data, skuMapperBefore, itemRepository, channelConfig, new ChannelSkuRepository());

        Assert.AreEqual("매핑 실패", data.Status);
        Assert.IsNull(data.Msku);

        // 사용자가 그리드 셀에 "SKU1"을 입력해 1:1 규칙을 만든 상황을 흉내낸다.
        mappingRepository.UpsertExactRule("CH1", "상품A옵션1", "SKU1");

        // SkuMapper는 생성 시점에 규칙을 로드하므로, 규칙이 바뀌면 새로 만들어야 한다(SettlementForm의
        // ReapplyMappingAndProfit이 하는 일).
        var skuMapperAfter = new SkuMapper(mappingRepository, "CH1");
        SettlementLoader.ApplyMappingAndProfit(data, skuMapperAfter, itemRepository, channelConfig, new ChannelSkuRepository());

        Assert.AreEqual("SKU1", data.Msku);
        Assert.AreEqual("매핑(1:1)", data.Status);
        Assert.AreEqual(8000m, data.Profit); // 10000 - 1000*2
    }

    /// <summary>
    /// SettlementForm.ReapplyMappingForAllRows가 행마다 새 SQLite 연결을 여는 비용을 피하려고
    /// 쓰는 itemCache/cskuCache 인자가 DB를 직접 조회하는 기본 경로와 똑같은 결과를 내는지
    /// 검증한다(캐시 조회가 실수로 다른 키 비교 방식을 쓰면 결과가 달라질 수 있음).
    /// </summary>
    [TestMethod]
    public void ApplyMappingAndProfit_WithCaches_ProducesSameResultAsWithoutCaches()
    {
        var itemRepository = new ItemRepository();
        itemRepository.Upsert(new ItemModel { Sku = "MSKU1", ItemName = "상품A", CostPrice = 1500m });

        var channelSkuRepository = new ChannelSkuRepository();
        channelSkuRepository.Upsert(new ChannelSkuModel { ChannelCode = "CH1", CskuCode = "CSKU1", Msku = "MSKU1", SupplyPrice = 0m });

        var mappingRepository = new MappingRepository();
        mappingRepository.UpsertExactRule("CH1", "상품A옵션1", "CSKU1");

        var channelConfig = new ChannelConfig { ChannelCode = "CH1", ChannelName = "테스트채널", ChannelType = ChannelType.General };

        var dataWithoutCache = new SettlementData { ChannelCode = "CH1", ProductName = "상품A", OptionName = "옵션1", Qty = 1, Settlement = 5000m };
        SettlementLoader.ApplyMappingAndProfit(dataWithoutCache, new SkuMapper(mappingRepository, "CH1"), itemRepository, channelConfig, channelSkuRepository);

        var itemCache = itemRepository.GetAll().ToDictionary(i => i.Sku);
        var cskuCache = channelSkuRepository.GetAllByChannel("CH1").ToDictionary(c => c.CskuCode, StringComparer.OrdinalIgnoreCase);
        var dataWithCache = new SettlementData { ChannelCode = "CH1", ProductName = "상품A", OptionName = "옵션1", Qty = 1, Settlement = 5000m };
        SettlementLoader.ApplyMappingAndProfit(dataWithCache, new SkuMapper(mappingRepository, "CH1"), itemRepository, channelConfig, channelSkuRepository, itemCache, cskuCache);

        Assert.AreEqual(dataWithoutCache.Msku, dataWithCache.Msku);
        Assert.AreEqual(dataWithoutCache.Status, dataWithCache.Status);
        Assert.AreEqual(dataWithoutCache.Profit, dataWithCache.Profit);
        Assert.AreEqual("CSKU1", dataWithCache.Msku); // 매핑 규칙의 TargetSku(CSKU코드) 그대로 저장됨
        Assert.AreEqual(3500m, dataWithCache.Profit); // 5000 - 1500*1 (원가는 MSKU1로 변환해서 조회)
    }
}

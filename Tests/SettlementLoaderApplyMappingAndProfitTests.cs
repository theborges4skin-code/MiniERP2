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
}

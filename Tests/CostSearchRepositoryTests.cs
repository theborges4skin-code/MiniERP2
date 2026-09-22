using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class CostSearchRepositoryTests
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

    private static void AddChannel(string code, string name)
        => new SalesChannelRepository().Upsert(new SalesChannel { ChannelCode = code, ChannelName = name });

    private static void AddCsku(string channelCode, string cskuCode, string msku, decimal? costOverride = null)
        => new ChannelSkuRepository().Upsert(new ChannelSkuModel
        {
            ChannelCode = channelCode,
            CskuCode = cskuCode,
            Msku = msku,
            SupplyPrice = 5000m,
            CostPriceOverride = costOverride,
        });

    [TestMethod]
    public void Search_MatchesMasterSkuByCodeAndName()
    {
        new ItemRepository().Upsert(new ItemModel { Sku = "SKU-001", ItemName = "콜라겐분말", CostPrice = 1000m });

        var byCode = new CostSearchRepository().Search("SKU-0");
        var byName = new CostSearchRepository().Search("콜라겐");

        Assert.HasCount(1, byCode);
        Assert.AreEqual("SKU-001", byCode[0].Code);
        Assert.IsFalse(byCode[0].IsCsku);
        Assert.AreEqual(1000m, byCode[0].CostPrice);
        Assert.HasCount(1, byName);
        Assert.AreEqual("SKU-001", byName[0].Code);
    }

    [TestMethod]
    public void Search_MasterSku_ReportsLastCostChangeDate()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "SKU-002", ItemName = "이력상품", CostPrice = 1000m });

        var noHistory = new CostSearchRepository().Search("SKU-002").Single();
        Assert.IsNull(noHistory.CostChangedAt, "원가 변경이 없었으면 최종 수정일도 없어야 한다.");

        var before = DateTime.Now.AddSeconds(-1);
        repository.Upsert(new ItemModel { Sku = "SKU-002", ItemName = "이력상품", CostPrice = 1200m });

        var changed = new CostSearchRepository().Search("SKU-002").Single();
        Assert.AreEqual(1200m, changed.CostPrice);
        Assert.IsNotNull(changed.CostChangedAt);
        Assert.IsTrue(changed.CostChangedAt >= before);
    }

    [TestMethod]
    public void Search_Csku_WithoutOverride_FollowsMasterCostAndMasterChangeDate()
    {
        var itemRepository = new ItemRepository();
        itemRepository.Upsert(new ItemModel { Sku = "SKU-003", ItemName = "연동상품", CostPrice = 1000m });
        AddChannel("CH1", "쿠팡");
        AddCsku("CH1", "쿠팡_SKU-003", "SKU-003");
        itemRepository.Upsert(new ItemModel { Sku = "SKU-003", ItemName = "연동상품", CostPrice = 1500m });

        var csku = new CostSearchRepository().Search("쿠팡_SKU-003").Single();

        Assert.IsTrue(csku.IsCsku);
        Assert.IsFalse(csku.IsCostOverride);
        Assert.AreEqual(1500m, csku.CostPrice, "개별원가가 없으면 마스터 원가를 따라야 한다.");
        Assert.AreEqual("연동상품", csku.Name);
        Assert.AreEqual("쿠팡", csku.ChannelName);
        Assert.IsNotNull(csku.CostChangedAt, "마스터 원가가 바뀐 시각이 곧 이 CSKU 원가가 바뀐 시각이다.");
    }

    [TestMethod]
    public void Search_Csku_WithOverride_UsesOverrideCostAndItsOwnChangeDate()
    {
        var itemRepository = new ItemRepository();
        itemRepository.Upsert(new ItemModel { Sku = "SKU-004", ItemName = "개별원가상품", CostPrice = 1000m });
        AddChannel("CH1", "쿠팡");
        AddCsku("CH1", "쿠팡_SKU-004", "SKU-004");

        var before = DateTime.Now.AddSeconds(-1);
        AddCsku("CH1", "쿠팡_SKU-004", "SKU-004", costOverride: 2200m);

        var csku = new CostSearchRepository().Search("쿠팡_SKU-004").Single();

        Assert.IsTrue(csku.IsCostOverride);
        Assert.AreEqual(2200m, csku.CostPrice);
        Assert.IsNotNull(csku.CostChangedAt);
        Assert.IsTrue(csku.CostChangedAt >= before);
    }

    [TestMethod]
    public void Search_Csku_WithoutMatchingMasterSku_ReturnsNullCost()
    {
        AddChannel("CH1", "쿠팡");
        AddCsku("CH1", "쿠팡_없는SKU", "SKU-MISSING");

        var csku = new CostSearchRepository().Search("쿠팡_없는SKU").Single();

        Assert.IsNull(csku.CostPrice, "마스터SKU가 없으면 0원이 아니라 '알 수 없음'이어야 한다.");
        Assert.AreEqual("SKU-MISSING", csku.Msku);
    }

    [TestMethod]
    public void Search_ByMasterSku_FindsBothMasterAndItsCskus_MasterFirst()
    {
        new ItemRepository().Upsert(new ItemModel { Sku = "SKU-005", ItemName = "공용상품", CostPrice = 1000m });
        AddChannel("CH1", "쿠팡");
        AddChannel("CH2", "네이버");
        AddCsku("CH1", "쿠팡_SKU-005", "SKU-005");
        AddCsku("CH2", "네이버_SKU-005", "SKU-005", costOverride: 1800m);

        var results = new CostSearchRepository().Search("SKU-005");

        Assert.HasCount(3, results);
        Assert.IsFalse(results[0].IsCsku, "코드가 정확히 일치하는 마스터SKU가 맨 위에 와야 한다.");
        Assert.AreEqual("SKU-005", results[0].Code);
        Assert.HasCount(2, results.Where(r => r.IsCsku).ToList());
    }

    [TestMethod]
    public void Search_TreatsWildcardCharactersAsLiterals()
    {
        new ItemRepository().Upsert(new ItemModel { Sku = "SKU-100", ItemName = "일반상품", CostPrice = 1000m });
        new ItemRepository().Upsert(new ItemModel { Sku = "SKU%100", ItemName = "퍼센트상품", CostPrice = 2000m });

        var results = new CostSearchRepository().Search("SKU%1");

        Assert.HasCount(1, results);
        Assert.AreEqual("SKU%100", results[0].Code);
    }

    [TestMethod]
    public void Search_EmptyQuery_ReturnsNothing()
    {
        new ItemRepository().Upsert(new ItemModel { Sku = "SKU-006", ItemName = "상품", CostPrice = 1000m });

        Assert.IsEmpty(new CostSearchRepository().Search("   "));
    }
}

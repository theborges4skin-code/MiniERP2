using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerConsolidationAggregatorTests
{
    private string _testFolder = string.Empty;
    private ChannelSkuRepository _channelSkuRepository = new();
    private DocPartyRepository _docPartyRepository = new();
    private ItemRepository _itemRepository = new();
    private PartnerConsolidationAggregator _aggregator = null!;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _channelSkuRepository = new ChannelSkuRepository();
        _docPartyRepository = new DocPartyRepository();
        _itemRepository = new ItemRepository();
        var resolver = new PartnerSupplyPriceResolver(_channelSkuRepository, _docPartyRepository);
        _aggregator = new PartnerConsolidationAggregator(resolver, _itemRepository);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private void SavePartner(string channelCode, string companyName, bool isPriceMaster = false) =>
        _docPartyRepository.Save(new DocParty { ChannelCode = channelCode, CompanyName = companyName, ProfileName = channelCode, IsPriceMaster = isPriceMaster });

    private void SaveCsku(string channel, string csku, string msku, decimal price = 0) =>
        _channelSkuRepository.Upsert(new ChannelSkuModel { ChannelCode = channel, CskuCode = csku, Msku = msku, SupplyPrice = price });

    private static PartnerConsolidationRow MappedRow(string company, string channel, string csku, string msku, int qty, string productName = "상품") => new()
    {
        CompanyName = company,
        ChannelCode = channel,
        ProductName = productName,
        Quantity = qty,
        RawMappedSku = csku,
        Kind = PartnerConsolidationRowKind.Mapped,
        ResolvedCskuCode = csku,
        ResolvedMsku = msku,
    };

    [TestMethod]
    public void Aggregate_SumsQuantityAcrossChannels_ForSameMsku()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true);
        SavePartner("CH_SUB", "펩투나");
        SaveCsku("CH_MASTER", "M-SKU1", "MSKU1", 5000m);
        SaveCsku("CH_SUB", "S-SKU1", "MSKU1", 0);
        _itemRepository.Upsert(new ItemModel { Sku = "MSKU1", ItemName = "상품A", CostPrice = 3000m });

        var rows = new[]
        {
            MappedRow("펩투나", "CH_MASTER", "M-SKU1", "MSKU1", 10),
            MappedRow("펩투나", "CH_SUB", "S-SKU1", "MSKU1", 5),
        };

        var result = _aggregator.Aggregate(rows);

        Assert.HasCount(1, result.CskuDetails);
        var detail = result.CskuDetails[0];
        Assert.AreEqual(15, detail.Quantity);
        Assert.AreEqual(5000m, detail.SupplyPrice);
        Assert.AreEqual(75000m, detail.SupplyRevenue);
        Assert.AreEqual(3000m, detail.CostPrice);
        Assert.AreEqual(75000m - 15 * 3000m, detail.SupplyProfit);

        var summary = result.CompanySummaries.Single();
        Assert.AreEqual("펩투나", summary.CompanyName);
        Assert.AreEqual(2, summary.ChannelCount);
        Assert.AreEqual(15, summary.TotalQuantity);
        Assert.AreEqual(75000m, summary.TotalSupplyRevenue);
        Assert.AreEqual(0, summary.UnassignedPriceCount);
    }

    [TestMethod]
    public void Aggregate_NonMasterChannelOwnOverride_TakesPrecedenceOverMaster()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true);
        SavePartner("CH_SUB", "펩투나");
        SaveCsku("CH_MASTER", "M-SKU1", "MSKU1", 5000m);
        SaveCsku("CH_SUB", "S-SKU1", "MSKU1", 9999m); // 자체 오버라이드

        var rows = new[] { MappedRow("펩투나", "CH_SUB", "S-SKU1", "MSKU1", 1) };

        var result = _aggregator.Aggregate(rows);

        Assert.AreEqual(9999m, result.CskuDetails[0].SupplyPrice);
        Assert.AreEqual(SupplyPriceSource.Own, result.CskuDetails[0].PriceSource);
    }

    [TestMethod]
    public void Aggregate_NoMasterChannel_MarksUnassigned_ZeroRevenue()
    {
        SavePartner("CH_SUB", "펩투나");
        SaveCsku("CH_SUB", "S-SKU1", "MSKU1", 0);

        var rows = new[] { MappedRow("펩투나", "CH_SUB", "S-SKU1", "MSKU1", 10) };

        var result = _aggregator.Aggregate(rows);

        var detail = result.CskuDetails[0];
        Assert.AreEqual(SupplyPriceSource.Unassigned, detail.PriceSource);
        Assert.AreEqual(0m, detail.SupplyPrice);
        Assert.AreEqual(0m, detail.SupplyRevenue);
        Assert.AreEqual(1, result.CompanySummaries.Single().UnassignedPriceCount);
    }

    [TestMethod]
    public void Aggregate_ItemNotRegistered_CostPriceAndProfitAreNull_W7()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true);
        SaveCsku("CH_MASTER", "M-SKU1", "UNKNOWN-MSKU", 5000m);

        var rows = new[] { MappedRow("펩투나", "CH_MASTER", "M-SKU1", "UNKNOWN-MSKU", 2) };

        var result = _aggregator.Aggregate(rows);

        var detail = result.CskuDetails[0];
        Assert.IsNull(detail.CostPrice);
        Assert.IsNull(detail.SupplyProfit);
        Assert.IsTrue(detail.IsCostMissing);
        // 매출액은 원가와 무관하게 계산된다.
        Assert.AreEqual(10000m, detail.SupplyRevenue);
    }

    [TestMethod]
    public void Aggregate_IgnoresNonMappedRows()
    {
        var rows = new[]
        {
            new PartnerConsolidationRow { CompanyName = "펩투나", ChannelCode = "CH1", Kind = PartnerConsolidationRowKind.Unmapped, Quantity = 5 },
            new PartnerConsolidationRow { CompanyName = "펩투나", ChannelCode = "CH1", Kind = PartnerConsolidationRowKind.Excluded, Quantity = 5 },
            new PartnerConsolidationRow { CompanyName = "펩투나", ChannelCode = "CH1", Kind = PartnerConsolidationRowKind.CskuUnresolved, Quantity = 5 },
        };

        var result = _aggregator.Aggregate(rows);

        Assert.IsEmpty(result.CskuDetails);
        Assert.IsEmpty(result.CompanySummaries);
    }

    [TestMethod]
    public void Aggregate_MultipleCompanies_AreIndependent()
    {
        SavePartner("CH_A", "펩투나", isPriceMaster: true);
        SavePartner("CH_B", "한결", isPriceMaster: true);
        SaveCsku("CH_A", "A-SKU1", "MSKU1", 1000m);
        SaveCsku("CH_B", "B-SKU1", "MSKU2", 2000m);

        var rows = new[]
        {
            MappedRow("펩투나", "CH_A", "A-SKU1", "MSKU1", 3),
            MappedRow("한결", "CH_B", "B-SKU1", "MSKU2", 4),
        };

        var result = _aggregator.Aggregate(rows);

        Assert.HasCount(2, result.CompanySummaries);
        var pep = result.CompanySummaries.Single(c => c.CompanyName == "펩투나");
        var han = result.CompanySummaries.Single(c => c.CompanyName == "한결");
        Assert.AreEqual(3000m, pep.TotalSupplyRevenue);
        Assert.AreEqual(8000m, han.TotalSupplyRevenue);
    }
}

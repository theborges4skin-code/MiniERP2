using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerSupplyPriceResolverTests
{
    private string _testFolder = string.Empty;
    private ChannelSkuRepository _channelSkuRepository = new();
    private DocPartyRepository _docPartyRepository = new();
    private PartnerSupplyPriceResolver _resolver = null!;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _channelSkuRepository = new ChannelSkuRepository();
        _docPartyRepository = new DocPartyRepository();
        _resolver = new PartnerSupplyPriceResolver(_channelSkuRepository, _docPartyRepository);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private static ChannelSkuModel Csku(string channel, string csku, string msku, decimal price) => new()
    {
        ChannelCode = channel,
        CskuCode = csku,
        Msku = msku,
        SupplyPrice = price,
    };

    private void SavePartner(string channelCode, string companyName, bool isPriceMaster) =>
        _docPartyRepository.Save(new DocParty
        {
            ChannelCode = channelCode,
            CompanyName = companyName,
            ProfileName = channelCode,
            IsPriceMaster = isPriceMaster,
        });

    [TestMethod]
    public void Resolve_OwnPriceSet_ReturnsOwn()
    {
        _channelSkuRepository.Upsert(Csku("CH_A", "SKU1", "MSKU1", 5000m));
        SavePartner("CH_A", "펩투나", isPriceMaster: false);

        var result = _resolver.Resolve("CH_A", "SKU1", "MSKU1");

        Assert.AreEqual(5000m, result.Price);
        Assert.AreEqual(SupplyPriceSource.Own, result.Source);
    }

    [TestMethod]
    public void Resolve_OwnZero_MasterHasSameCskuCode_ReturnsInherited()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true);
        SavePartner("CH_SUB", "펩투나", isPriceMaster: false);
        _channelSkuRepository.Upsert(Csku("CH_SUB", "SKU1", "MSKU1", 0));
        _channelSkuRepository.Upsert(Csku("CH_MASTER", "SKU1", "MSKU1", 7000m));

        var result = _resolver.Resolve("CH_SUB", "SKU1", "MSKU1");

        Assert.AreEqual(7000m, result.Price);
        Assert.AreEqual(SupplyPriceSource.Inherited, result.Source);
        Assert.AreEqual("CH_MASTER", result.MasterChannelCode);
    }

    [TestMethod]
    public void Resolve_NoOwnRow_MasterHasDifferentCskuCode_FallsBackToMsku()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true);
        SavePartner("CH_SUB", "펩투나", isPriceMaster: false);
        // CH_SUB에는 SKU1 행 자체가 없음(미매핑 CSKU). 대표채널엔 다른 코드지만 같은 Msku.
        _channelSkuRepository.Upsert(Csku("CH_MASTER", "MASTER-SKU-X", "MSKU1", 9000m));

        var result = _resolver.Resolve("CH_SUB", "SKU1", "MSKU1");

        Assert.AreEqual(9000m, result.Price);
        Assert.AreEqual(SupplyPriceSource.Inherited, result.Source);
    }

    [TestMethod]
    public void Resolve_MasterCskuAmbiguous_TwoRowsSameMsku_ReturnsUnassigned()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true);
        SavePartner("CH_SUB", "펩투나", isPriceMaster: false);
        _channelSkuRepository.Upsert(Csku("CH_MASTER", "MASTER-A", "MSKU1", 1000m));
        _channelSkuRepository.Upsert(Csku("CH_MASTER", "MASTER-B", "MSKU1", 2000m));

        var result = _resolver.Resolve("CH_SUB", "SKU1", "MSKU1");

        Assert.AreEqual(SupplyPriceSource.Unassigned, result.Source);
        Assert.IsTrue(result.IsAmbiguousMasterSkuMatch);
    }

    [TestMethod]
    public void Resolve_MasterCskuExistsButZero_ReturnsUnassigned_NoMskuFallback()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true);
        SavePartner("CH_SUB", "펩투나", isPriceMaster: false);
        _channelSkuRepository.Upsert(Csku("CH_SUB", "SKU1", "MSKU1", 0));
        _channelSkuRepository.Upsert(Csku("CH_MASTER", "SKU1", "MSKU1", 0));

        var result = _resolver.Resolve("CH_SUB", "SKU1", "MSKU1");

        Assert.AreEqual(SupplyPriceSource.Unassigned, result.Source);
        Assert.IsFalse(result.IsAmbiguousMasterSkuMatch);
    }

    [TestMethod]
    public void Resolve_NoPriceMasterInGroup_ReturnsUnassigned()
    {
        SavePartner("CH_SUB", "펩투나", isPriceMaster: false);
        _channelSkuRepository.Upsert(Csku("CH_SUB", "SKU1", "MSKU1", 0));

        var result = _resolver.Resolve("CH_SUB", "SKU1", "MSKU1");

        Assert.AreEqual(SupplyPriceSource.Unassigned, result.Source);
    }

    [TestMethod]
    public void Resolve_NoDocPartyForChannel_ReturnsUnassigned()
    {
        _channelSkuRepository.Upsert(Csku("CH_ORPHAN", "SKU1", "MSKU1", 0));

        var result = _resolver.Resolve("CH_ORPHAN", "SKU1", "MSKU1");

        Assert.AreEqual(SupplyPriceSource.Unassigned, result.Source);
    }

    [TestMethod]
    public void Resolve_ChannelIsItsOwnMaster_OwnZero_ReturnsUnassigned()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true);
        _channelSkuRepository.Upsert(Csku("CH_MASTER", "SKU1", "MSKU1", 0));

        var result = _resolver.Resolve("CH_MASTER", "SKU1", "MSKU1");

        Assert.AreEqual(SupplyPriceSource.Unassigned, result.Source);
    }

    [TestMethod]
    public void Resolve_MasterPriceZero_ReturnsUnassigned()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true);
        SavePartner("CH_SUB", "펩투나", isPriceMaster: false);
        _channelSkuRepository.Upsert(Csku("CH_MASTER", "SKU1", "MSKU1", 0));

        var result = _resolver.Resolve("CH_SUB", "SKU1", "MSKU1");

        Assert.AreEqual(SupplyPriceSource.Unassigned, result.Source);
    }
}

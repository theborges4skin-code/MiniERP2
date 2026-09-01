using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerConsolidationPriceEntryServiceTests
{
    private string _testFolder = string.Empty;
    private ChannelSkuRepository _channelSkuRepository = new();
    private DocPartyRepository _docPartyRepository = new();
    private PartnerConsolidationPriceEntryService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _channelSkuRepository = new ChannelSkuRepository();
        _docPartyRepository = new DocPartyRepository();
        _service = new PartnerConsolidationPriceEntryService(_channelSkuRepository, _docPartyRepository);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private void SavePartner(string channelCode, string companyName, bool isPriceMaster, string profileName = "") =>
        _docPartyRepository.Save(new DocParty
        {
            ChannelCode = channelCode,
            CompanyName = companyName,
            ProfileName = string.IsNullOrEmpty(profileName) ? channelCode : profileName,
            IsPriceMaster = isPriceMaster,
        });

    [TestMethod]
    public void SavePrice_NoPriceMasterChannel_ReturnsNoPriceMasterChannel()
    {
        SavePartner("CH1", "펩투나", isPriceMaster: false);

        var outcome = _service.SavePrice("펩투나", "MSKU1", 5000m);

        Assert.AreEqual(PartnerConsolidationPriceEntryResult.NoPriceMasterChannel, outcome.Result);
    }

    [TestMethod]
    public void SavePrice_MasterHasExistingCskuForMsku_UpdatesItsSupplyPrice()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true, profileName: "쿠팡일반");
        _channelSkuRepository.Upsert(new ChannelSkuModel { ChannelCode = "CH_MASTER", CskuCode = "M-SKU1", Msku = "MSKU1", SupplyPrice = 0 });

        var outcome = _service.SavePrice("펩투나", "MSKU1", 7000m);

        Assert.AreEqual(PartnerConsolidationPriceEntryResult.Saved, outcome.Result);
        Assert.AreEqual("M-SKU1", outcome.CskuCode);
        var updated = _channelSkuRepository.GetByChannelAndCskuCode("CH_MASTER", "M-SKU1");
        Assert.AreEqual(7000m, updated!.SupplyPrice);
    }

    [TestMethod]
    public void SavePrice_MasterHasNoCskuForMsku_CreatesNewOne()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true, profileName: "쿠팡일반");

        var outcome = _service.SavePrice("펩투나", "MSKU1", 4500m);

        Assert.AreEqual(PartnerConsolidationPriceEntryResult.Saved, outcome.Result);
        Assert.IsNotNull(outcome.CskuCode);
        var created = _channelSkuRepository.GetByChannelAndCskuCode("CH_MASTER", outcome.CskuCode!);
        Assert.IsNotNull(created);
        Assert.AreEqual("MSKU1", created!.Msku);
        Assert.AreEqual(4500m, created.SupplyPrice);
    }

    [TestMethod]
    public void SavePrice_GeneratedCodeCollidesWithDifferentMsku_AppendsSuffix()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true, profileName: "쿠팡일반");
        // CskuCodeGenerator.BuildDefault("쿠팡일반", "MSKU1") == "쿠팡_MSKU1" — 미리 다른 상품이 그 코드를 쓰고 있게 만든다.
        _channelSkuRepository.Upsert(new ChannelSkuModel { ChannelCode = "CH_MASTER", CskuCode = "쿠팡_MSKU1", Msku = "OTHER-MSKU", SupplyPrice = 100 });

        var outcome = _service.SavePrice("펩투나", "MSKU1", 5000m);

        Assert.AreEqual(PartnerConsolidationPriceEntryResult.Saved, outcome.Result);
        Assert.AreNotEqual("쿠팡_MSKU1", outcome.CskuCode);
        // 기존 무관한 CSKU(OTHER-MSKU)는 그대로 보존돼야 한다.
        var untouched = _channelSkuRepository.GetByChannelAndCskuCode("CH_MASTER", "쿠팡_MSKU1");
        Assert.AreEqual("OTHER-MSKU", untouched!.Msku);
        Assert.AreEqual(100m, untouched.SupplyPrice);
    }

    [TestMethod]
    public void SavePrice_MasterHasAmbiguousCskuForMsku_ReturnsAmbiguous_DoesNotWrite()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true, profileName: "쿠팡일반");
        _channelSkuRepository.Upsert(new ChannelSkuModel { ChannelCode = "CH_MASTER", CskuCode = "M-A", Msku = "MSKU1", SupplyPrice = 0 });
        _channelSkuRepository.Upsert(new ChannelSkuModel { ChannelCode = "CH_MASTER", CskuCode = "M-B", Msku = "MSKU1", SupplyPrice = 0 });

        var outcome = _service.SavePrice("펩투나", "MSKU1", 5000m);

        Assert.AreEqual(PartnerConsolidationPriceEntryResult.AmbiguousMasterCsku, outcome.Result);
        Assert.AreEqual(0m, _channelSkuRepository.GetByChannelAndCskuCode("CH_MASTER", "M-A")!.SupplyPrice);
        Assert.AreEqual(0m, _channelSkuRepository.GetByChannelAndCskuCode("CH_MASTER", "M-B")!.SupplyPrice);
    }

    [TestMethod]
    public void SavePrice_PriceChangeOnExisting_RecordsHistory()
    {
        SavePartner("CH_MASTER", "펩투나", isPriceMaster: true, profileName: "쿠팡일반");
        _channelSkuRepository.Upsert(new ChannelSkuModel { ChannelCode = "CH_MASTER", CskuCode = "M-SKU1", Msku = "MSKU1", SupplyPrice = 1000m });

        _service.SavePrice("펩투나", "MSKU1", 2000m, reason: "취합 화면에서 입력");

        // ChannelSkuPriceHistory의 "Msku" 컬럼은 실제로는 CskuCode 값을 저장한다(ChannelSkuRepository.Upsert 참고).
        var history = _channelSkuRepository.GetPriceHistory("CH_MASTER", "M-SKU1");
        Assert.HasCount(1, history);
        Assert.AreEqual(1000m, history[0].OldPrice);
        Assert.AreEqual(2000m, history[0].NewPrice);
    }
}

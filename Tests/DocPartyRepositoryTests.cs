using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class DocPartyRepositoryTests
{
    private string _testFolder = string.Empty;
    private DocPartyRepository _repo = new();

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _repo = new DocPartyRepository();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private static DocParty Party(string channelCode, string companyName, bool isPriceMaster = false, decimal shipFee = 3000m) => new()
    {
        ChannelCode = channelCode,
        CompanyName = companyName,
        ProfileName = channelCode,
        IsPriceMaster = isPriceMaster,
        ShippingFeePerShipment = shipFee,
    };

    [TestMethod]
    public void Save_RoundTrip_PersistsPriceMasterAndShippingFee()
    {
        var party = Party("CH001", "펩투나", isPriceMaster: true, shipFee: 2500m);
        _repo.Save(party);

        var loaded = _repo.GetByChannelCode("CH001");

        Assert.IsNotNull(loaded);
        Assert.IsTrue(loaded!.IsPriceMaster);
        Assert.AreEqual(2500m, loaded.ShippingFeePerShipment);
    }

    [TestMethod]
    public void Save_NewParty_DefaultsShippingFeeTo3000()
    {
        var party = Party("CH002", "한결");
        _repo.Save(party);

        var loaded = _repo.GetByChannelCode("CH002");

        Assert.IsNotNull(loaded);
        Assert.IsFalse(loaded!.IsPriceMaster);
        Assert.AreEqual(3000m, loaded.ShippingFeePerShipment);
    }

    [TestMethod]
    public void FindPriceMasterInGroup_ReturnsOtherRowInSameCompany()
    {
        var a = Party("CH003", "펩투나", isPriceMaster: true);
        _repo.Save(a);
        var b = Party("CH004", "펩투나");
        _repo.Save(b);

        var found = _repo.FindPriceMasterInGroup("펩투나", excludeId: b.Id);

        Assert.IsNotNull(found);
        Assert.AreEqual(a.Id, found!.Id);
    }

    [TestMethod]
    public void FindPriceMasterInGroup_ExcludesSelf_ReturnsNullWhenSelfIsOnlyMaster()
    {
        var a = Party("CH005", "펩투나", isPriceMaster: true);
        _repo.Save(a);

        var found = _repo.FindPriceMasterInGroup("펩투나", excludeId: a.Id);

        Assert.IsNull(found);
    }

    [TestMethod]
    public void SetPriceMaster_ClearsOtherRowsInSameCompanyGroup()
    {
        var a = Party("CH006", "펩투나", isPriceMaster: true);
        _repo.Save(a);
        var b = Party("CH007", "펩투나");
        _repo.Save(b);

        _repo.SetPriceMaster(b.Id, "펩투나");

        var reloadedA = _repo.GetByChannelCode("CH006");
        var reloadedB = _repo.GetByChannelCode("CH007");
        Assert.IsFalse(reloadedA!.IsPriceMaster);
        Assert.IsTrue(reloadedB!.IsPriceMaster);
    }

    [TestMethod]
    public void SetPriceMaster_DoesNotAffectDifferentCompanyGroup()
    {
        var a = Party("CH008", "펩투나", isPriceMaster: true);
        _repo.Save(a);
        var other = Party("CH009", "한결", isPriceMaster: true);
        _repo.Save(other);
        var b = Party("CH010", "펩투나");
        _repo.Save(b);

        _repo.SetPriceMaster(b.Id, "펩투나");

        var reloadedOther = _repo.GetByChannelCode("CH009");
        Assert.IsTrue(reloadedOther!.IsPriceMaster, "다른 상호명 그룹의 대표단가 표시는 영향받지 않아야 한다.");
    }

    [TestMethod]
    public void GetPriceMasterByCompanyName_ReturnsMasterRow()
    {
        var a = Party("CH011", "펩투나");
        _repo.Save(a);
        var b = Party("CH012", "펩투나", isPriceMaster: true);
        _repo.Save(b);

        var found = _repo.GetPriceMasterByCompanyName("펩투나");

        Assert.IsNotNull(found);
        Assert.AreEqual("CH012", found!.ChannelCode);
    }

    [TestMethod]
    public void GetPriceMasterByCompanyName_NoMaster_ReturnsNull()
    {
        var a = Party("CH013", "펩투나");
        _repo.Save(a);

        Assert.IsNull(_repo.GetPriceMasterByCompanyName("펩투나"));
    }
}

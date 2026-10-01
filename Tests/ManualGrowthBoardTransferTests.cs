using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Services;

namespace MiniERP2.Tests;

[TestClass]
public class ManualGrowthBoardTransferTests
{
    private string _testFolder = string.Empty;
    private PartnerClosingRepository _closingRepo = null!;
    private ManualGrowthBoardTransfer _transfer = null!;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _closingRepo = new PartnerClosingRepository();
        _transfer = new ManualGrowthBoardTransfer(_closingRepo, new PartnerMasterRepository());
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private static ManualGrowthClosing Closing(string status) => new()
    {
        Period = "2026-09",
        ChannelCode = "CH069",
        SourceFileName = "거래명세표_이공이공인터내셔널.xlsx",
        SourceSheetName = "2609 제트",
        Status = status,
        Lines =
        [
            new() { LineDate = "2026-09-01", CskuCode = "이공이공 요석제거제 4L", MasterSku = "igusc4000", ItemName = "이공이공 요석제거제 4L",
                Qty = 96, SupplyAmount = 1003200m, Tax = 100320m, CostPrice = 2541m, Profit = 1103520m - 96 * 2541m },
            new() { LineDate = "", CskuCode = "할인", ItemName = "할인", Qty = 0, SupplyAmount = -1000m, Tax = -100m, Profit = -1100m },
        ],
    };

    [TestMethod]
    public void Transfer_Confirmed_SavesDatedLinesAsVatIncludedSnapshot()
    {
        var header = _transfer.Transfer(Closing(ManualGrowthClosing.StatusConfirmed), "이공그로스수동마감");

        Assert.AreEqual("확정", header.Status);
        Assert.AreEqual("이공그로스수동마감", header.PartyName);
        var lines = _closingRepo.GetLinesByClosingId(header.Id);
        Assert.HasCount(2, lines);
        Assert.AreEqual(new DateTime(2026, 9, 1), lines[0].LineDate!.Value.Date);
        Assert.AreEqual(11495m, lines[0].UnitPrice);                  // 10,450 공급가 → VAT포함
        Assert.AreEqual(new DateTime(2026, 9, 30), lines[1].LineDate!.Value.Date); // 날짜 없으면 말일
        Assert.AreEqual(1m, lines[1].Qty);                             // 금액만 있는 행은 수량 1
        Assert.AreEqual(1103520m - 1100m, header.TotalSupply);
    }

    [TestMethod]
    public void Transfer_Unconfirmed_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            _transfer.Transfer(Closing(ManualGrowthClosing.StatusUnconfirmed), "이공그로스수동마감"));
    }

    [TestMethod]
    public void FindByCompanyName_FallsBackToProfileName()
    {
        var repo = new DocPartyRepository();
        repo.Save(new DocParty { ProfileName = "이공그로스수동마감", CompanyName = "주식회사 이공이공인터내셔널", RegNo = "269-88-01547", ChannelCode = "CH069" });

        Assert.AreEqual("269-88-01547", repo.FindByCompanyName("이공그로스수동마감")?.RegNo);
        Assert.AreEqual("269-88-01547", repo.FindByCompanyName("주식회사 이공이공인터내셔널")?.RegNo);
    }
}

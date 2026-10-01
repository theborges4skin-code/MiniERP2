using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Exporters;
using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerConsolidationClosingTransferTests
{
    private string _testFolder = string.Empty;
    private string _filePath = string.Empty;
    private PartnerClosingRepository _closingRepo = null!;
    private PartnerConsolidationClosingTransfer _transfer = null!;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _filePath = Path.Combine(_testFolder, "rollup.xlsx");
        _closingRepo = new PartnerClosingRepository();
        _transfer = new PartnerConsolidationClosingTransfer(_closingRepo, new PartnerMasterRepository());
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private void ExportSample()
    {
        var summaries = new List<PartnerConsolidationCompanySummary>
        {
            new() { CompanyName = "주식회사 한결관리", TotalQuantity = 5, ShipmentCount = 4, ShippingFeeTotal = 12000m, UnassignedPriceCount = 1 },
        };
        var details = new List<PartnerConsolidationCskuDetail>
        {
            new() { CompanyName = "주식회사 한결관리", CskuCode = "온_한_rz500", Msku = "rz500", ProductName = "면도기세정액500ml",
                InvoiceDisplayName = "아이스버블 면도기세정액 (500ml)", Quantity = 3, SupplyPrice = 4000m, CostPrice = 1500m },
            new() { CompanyName = "주식회사 한결관리", CskuCode = "온_한_vg", Msku = "vg", ProductName = "vg",
                Quantity = 2, SupplyPrice = 0m, CostPrice = null },
        };
        PartnerConsolidationExporter.Export(summaries, details, [], [], [], _ => 3000m, _filePath);
    }

    [TestMethod]
    public void ReadFile_BuildsLinesAndShipping_SkipsTotalRow()
    {
        ExportSample();

        var read = PartnerConsolidationClosingTransfer.ReadFile(_filePath);

        Assert.IsNull(read.Error);
        var p = read.Packages.Single();
        Assert.AreEqual("주식회사 한결관리", p.CompanyName);
        Assert.HasCount(2, p.ProductLines);
        Assert.AreEqual("아이스버블 면도기세정액 (500ml)", p.ProductLines[0].ItemName); // 송장출력용 상품명 우선
        Assert.AreEqual("vg", p.ProductLines[1].ItemName);                            // 비어 있으면 품목명
        Assert.AreEqual(1, p.MissingCostCount);
        Assert.AreEqual(4, p.ShipmentCount);
        Assert.AreEqual(12000m + 12000m, p.TotalSupply);
    }

    [TestMethod]
    public void Transfer_Confirm_CreatesManualPartyWithSnapshotAndShippingLine()
    {
        ExportSample();
        var p = PartnerConsolidationClosingTransfer.ReadFile(_filePath).Packages.Single();

        var header = _transfer.Transfer("2026-09", p, "rollup.xlsx", confirm: true);

        Assert.AreEqual("확정", header.Status);
        Assert.IsNotNull(header.ConfirmedAt);
        Assert.StartsWith("MANUAL:", header.PartyKey);
        var summary = _closingRepo.GetSummary("2026-09", header.PartyKey);
        Assert.HasCount(3, summary.Lines);
        var shipping = summary.Lines.Single(l => l.CskuCode == PartnerConsolidationClosingTransfer.ShippingLineCode);
        Assert.AreEqual(4m, shipping.Qty);
        Assert.AreEqual(3000m, shipping.UnitPrice);
        Assert.AreEqual(0m, shipping.Profit);
        Assert.AreEqual(24000m, summary.TotalSupply);
        Assert.AreEqual((4000m - 1500m) * 3, summary.TotalProfit);
        Assert.AreEqual(new DateTime(2026, 9, 30), summary.Lines[0].LineDate!.Value.Date);
    }

    [TestMethod]
    public void Transfer_Draft_ThenConfirmedTwice_SecondThrowsAndKeepsSameParty()
    {
        ExportSample();
        var p = PartnerConsolidationClosingTransfer.ReadFile(_filePath).Packages.Single();

        var draft = _transfer.Transfer("2026-09", p, "rollup.xlsx", confirm: false);
        Assert.AreEqual("대조중", draft.Status);
        Assert.IsNull(draft.ConfirmedAt);

        var confirmed = _transfer.Transfer("2026-09", p, "rollup.xlsx", confirm: true);
        Assert.AreEqual(draft.PartyKey, confirmed.PartyKey);
        Assert.AreEqual(draft.Id, confirmed.Id);
        Assert.HasCount(3, _closingRepo.GetLinesByClosingId(confirmed.Id)); // 덮어쓰기(중복 없음)

        Assert.ThrowsExactly<InvalidOperationException>(() => _transfer.Transfer("2026-09", p, "rollup.xlsx", confirm: true));
    }

    [TestMethod]
    public void Transfer_AgainAfterAdjustment_KeepsAdjustmentLine()
    {
        ExportSample();
        var p = PartnerConsolidationClosingTransfer.ReadFile(_filePath).Packages.Single();
        var draft = _transfer.Transfer("2026-09", p, "rollup.xlsx", confirm: false);
        _closingRepo.AddManualLine("2026-09", draft.PartyKey, draft.PartyName, new PartnerClosingLine
        {
            LineDate = new DateTime(2026, 9, 30), CskuCode = PartnerClosingRepository.AdjustmentLineCode, ItemName = "광고비 지원", Qty = 1, UnitPrice = -6000m, Profit = -6000m,
        });

        var again = _transfer.Transfer("2026-09", p, "rollup.xlsx", confirm: true);

        var lines = _closingRepo.GetLinesByClosingId(again.Id);
        Assert.HasCount(4, lines);
        Assert.AreEqual(24000m - 6000m, again.TotalSupply);
        Assert.AreEqual((4000m - 1500m) * 3 - 6000m, again.TotalProfit);
    }
}

using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class FbaTrackingImporterTests
{
    private string _testFolder = string.Empty;
    private string _excelFilePath = string.Empty;
    private FbaOrderRepository _repository = new();

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _excelFilePath = Path.Combine(_testFolder, "tracking.xlsx");
        _repository = new FbaOrderRepository();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private void SeedOrder(string fbaNo, params (int BoxSeq, string MatchKey)[] boxes)
    {
        var order = new FbaOrder { FbaNo = fbaNo, OrderDate = DateTime.Today, ReceiverName = "R", Phone = "P", Address = "A" };
        var boxModels = boxes.Select(b => new FbaBox { FbaNo = fbaNo, BoxSeq = b.BoxSeq, MatchKey = b.MatchKey }).ToList();
        var items = boxes.Select(b => new FbaBoxItem { FbaNo = fbaNo, BoxSeq = b.BoxSeq, ItemSeq = 1, Csku = "CSKU", ItemName = "Item", Qty = 1 }).ToList();
        _repository.SaveOrder(order, boxModels, items);
    }

    private void WriteResultFile(params (string MatchKey, string TrackingNo)[] rows)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Sheet1");
        sheet.Cells[1, 1].Value = "고객주문번호";
        sheet.Cells[1, 2].Value = "운송장번호";
        for (int i = 0; i < rows.Length; i++)
        {
            sheet.Cells[i + 2, 1].Value = rows[i].MatchKey;
            sheet.Cells[i + 2, 2].Value = rows[i].TrackingNo;
        }
        ExportHelper.SaveExcel(package, _excelFilePath);
    }

    [TestMethod]
    public void Import_MatchesByMatchKey_AndAppliesTrackingNumbers()
    {
        SeedOrder("FBA-1", (1, "[SEND] SHIP1 총 2박스중 1번째"), (2, "[SEND] SHIP1 총 2박스중 2번째"));
        WriteResultFile(
            ("[SEND] SHIP1 총 2박스중 1번째", "T001"),
            ("[SEND] SHIP1 총 2박스중 2번째", "T002"));

        var result = new FbaTrackingImporter(_repository).Import(_excelFilePath);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.AppliedCount);
        var (_, boxes, _) = _repository.GetOrder("FBA-1");
        Assert.AreEqual("T001", boxes.Single(b => b.BoxSeq == 1).TrackingNo);
        Assert.AreEqual("T002", boxes.Single(b => b.BoxSeq == 2).TrackingNo);
    }

    [TestMethod]
    public void Import_UnmatchedRow_CancelsEntireBatch()
    {
        SeedOrder("FBA-1", (1, "[SEND] SHIP1 총 1박스중 1번째"));
        WriteResultFile(("[SEND] UNKNOWN 총 1박스중 1번째", "T001"));

        var result = new FbaTrackingImporter(_repository).Import(_excelFilePath);

        Assert.IsFalse(result.Success);
        Assert.HasCount(1, result.UnmatchedRows);
        var (_, boxes, _) = _repository.GetOrder("FBA-1");
        Assert.IsNull(boxes.Single().TrackingNo);
    }

    [TestMethod]
    public void Import_DuplicateMatchKeyAcrossPendingOrders_ReportsInconsistentAndAppliesNothing()
    {
        // Shipment ID 미입력 상태로 여러 발주가 동시에 미출고면 고객주문번호가 우연히 겹칠 수
        // 있다(§7.2) — 이 경우 자동반영하지 않고 불일치로 보고해야 한다.
        SeedOrder("FBA-1", (1, "[SEND]  총 1박스중 1번째"));
        SeedOrder("FBA-2", (1, "[SEND]  총 1박스중 1번째"));
        WriteResultFile(("[SEND]  총 1박스중 1번째", "T001"));

        var result = new FbaTrackingImporter(_repository).Import(_excelFilePath);

        Assert.IsFalse(result.Success);
        Assert.HasCount(1, result.InconsistentBoxes);
        Assert.AreEqual(0, result.AppliedCount);
    }

    [TestMethod]
    public void Import_ConflictingTrackingNumbersForSameMatchKey_ReportsInconsistent()
    {
        SeedOrder("FBA-1", (1, "[SEND] SHIP1 총 1박스중 1번째"));
        WriteResultFile(
            ("[SEND] SHIP1 총 1박스중 1번째", "T001"),
            ("[SEND] SHIP1 총 1박스중 1번째", "T002"));

        var result = new FbaTrackingImporter(_repository).Import(_excelFilePath);

        Assert.IsFalse(result.Success);
        Assert.HasCount(1, result.InconsistentBoxes);
    }

    [TestMethod]
    public void Import_AlreadyTrackedBoxIsNotPending_SoNewFileDoesNotReapplyOrUnmatch()
    {
        SeedOrder("FBA-1", (1, "[SEND] SHIP1 총 1박스중 1번째"));
        _repository.ApplyTracking("FBA-1", 1, "T001");
        WriteResultFile(("[SEND] SHIP1 총 1박스중 1번째", "T999"));

        var result = new FbaTrackingImporter(_repository).Import(_excelFilePath);

        // 이미 운송장이 등록된 박스는 GetPendingBoxes에서 빠지므로 미매칭으로 보고된다(부분반영 금지).
        Assert.IsFalse(result.Success);
        Assert.HasCount(1, result.UnmatchedRows);
        var (_, boxes, _) = _repository.GetOrder("FBA-1");
        Assert.AreEqual("T001", boxes.Single().TrackingNo);
    }
}

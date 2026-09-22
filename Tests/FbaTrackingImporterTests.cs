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

    /// <summary>택배사 "운송장출력데이터 상세" 파일 원본 구조를 그대로 재현한다 — 컬럼이 35개이고
    /// 운송장번호(F)/고객주문번호(P)가 중간에 끼어 있으며, 값은 각각 하이픈 표기와
    /// "[SEND] {Shipment ID} 총 N박스중 M번째" 문장이다.</summary>
    private void WriteCourierResultFile(params (string MatchKey, string TrackingNo)[] rows)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Sheet1");
        string[] headers =
        [
            "No", "", "기업고객", "기업고객전화번호", "기업고객주소", "운송장번호", "보내는분명", "보내는분전화번호",
            "보내는분우편번호", "보내는분주소", "받는분명", "받는분전화번호", "받는분우편번호", "받는분주소",
            "접수일자", "고객주문번호", "집화일자", "배송일자", "운임", "수량", "상품코드", "상품명", "단품명",
            "배송메세지1", "배송메세지2", "상태", "예약구분", "운임구분", "도착지코드", "Sub도착지코드",
            "주소약칭", "배송예정점소", "배송예정사원", "배송사원닉네임", "에러메시지",
        ];
        for (int col = 0; col < headers.Length; col++) sheet.Cells[1, col + 1].Value = headers[col];

        const int trackingCol = 6;
        const int matchKeyCol = 16;
        for (int i = 0; i < rows.Length; i++)
        {
            var row = i + 2;
            sheet.Cells[row, 1].Value = i + 4;
            sheet.Cells[row, 3].Value = "주식회사 신안코퍼레이션";
            sheet.Cells[row, trackingCol].Value = rows[i].TrackingNo;
            sheet.Cells[row, 11].Value = $"KW 인천센터{i + 1}";
            sheet.Cells[row, matchKeyCol].Value = rows[i].MatchKey;
            sheet.Cells[row, 26].Value = "집화지시";
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

    /// <summary>택배사 원본 파일(35컬럼, 헤더가 중간 컬럼에 있고 운송장번호는 하이픈 표기)을 가공 없이
    /// 그대로 넣을 수 있어야 한다 — 실제 "운송장출력데이터 상세_*.xlsx" 기준.</summary>
    [TestMethod]
    public void Import_RawCourierExportLayout_AppliesByShipmentIdAndBoxSeq()
    {
        SeedOrder("FBA-20260914-01",
            (1, "[SEND] FBA19PS3XW2J 총 4박스중 1번째"),
            (2, "[SEND] FBA19PS3XW2J 총 4박스중 2번째"),
            (3, "[SEND] FBA19PS3XW2J 총 4박스중 3번째"),
            (4, "[SEND] FBA19PS3XW2J 총 4박스중 4번째"));
        WriteCourierResultFile(
            ("[SEND] FBA19PS3XW2J 총 4박스중 1번째", "6004-0294-8523"),
            ("[SEND] FBA19PS3XW2J 총 4박스중 2번째", "6004-0294-8545"),
            ("[SEND] FBA19PS3XW2J 총 4박스중 3번째", "6004-0294-8556"),
            ("[SEND] FBA19PS3XW2J 총 4박스중 4번째", "6004-0294-8560"));

        var result = new FbaTrackingImporter(_repository).Import(_excelFilePath);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(4, result.AppliedCount);
        var (_, boxes, _) = _repository.GetOrder("FBA-20260914-01");
        Assert.AreEqual("6004-0294-8523", boxes.Single(b => b.BoxSeq == 1).TrackingNo);
        Assert.AreEqual("6004-0294-8560", boxes.Single(b => b.BoxSeq == 4).TrackingNo);
        Assert.AreEqual("운송장등록", boxes.Single(b => b.BoxSeq == 4).Status);
    }

    /// <summary>총박스수는 매칭 기준이 아니다 — 결과 파일 발행 후 박스를 추가/삭제해 재채번되면
    /// 문자열 전체 비교로는 매칭이 깨지지만, (Shipment ID, 박스순번)으로는 계속 맞아야 한다.</summary>
    [TestMethod]
    public void Import_TotalBoxCountChangedAfterFileWasIssued_StillMatchesByBoxSeq()
    {
        SeedOrder("FBA-1", (1, "[SEND] SHIP1 총 5박스중 1번째"), (2, "[SEND] SHIP1 총 5박스중 2번째"));
        WriteResultFile(
            ("[SEND] SHIP1 총 4박스중 1번째", "T001"),
            ("[SEND] SHIP1 총 4박스중 2번째", "T002"));

        var result = new FbaTrackingImporter(_repository).Import(_excelFilePath);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.AppliedCount);
    }

    /// <summary>하루치 택배 결과 파일에는 FBO 발주(#FBO…)나 일반 택배 건이 섞여 온다 — 이 행들은
    /// 미매칭으로 보고해 파일 전체를 막지 말고 조용히 건너뛰어야 한다.</summary>
    [TestMethod]
    public void Import_NonFbaRowsInSameFile_AreSkippedNotTreatedAsUnmatched()
    {
        SeedOrder("FBA-1", (1, "[SEND] SHIP1 총 1박스중 1번째"));
        WriteResultFile(
            ("#FBO26071301-03", "T900"),
            ("설레는01", "T901"),
            ("", "T902"),
            ("[SEND] SHIP1 총 1박스중 1번째", "T001"));

        var result = new FbaTrackingImporter(_repository).Import(_excelFilePath);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.AppliedCount);
        Assert.AreEqual(3, result.SkippedRowCount);
        Assert.IsEmpty(result.UnmatchedRows);
    }

    /// <summary>같은 파일을 두 번 불러와도(또는 이미 반영을 끝낸 과거 발주가 하루치 파일에 섞여
    /// 있어도) 실패로 막히지 않고 이미 반영된 건으로 건너뛴다.</summary>
    [TestMethod]
    public void Import_SameFileTwice_SecondRunSkipsAlreadyAppliedRows()
    {
        SeedOrder("FBA-1", (1, "[SEND] SHIP1 총 1박스중 1번째"));
        WriteResultFile(("[SEND] SHIP1 총 1박스중 1번째", "T001"));

        Assert.IsTrue(new FbaTrackingImporter(_repository).Import(_excelFilePath).Success);
        var second = new FbaTrackingImporter(_repository).Import(_excelFilePath);

        Assert.IsTrue(second.Success);
        Assert.AreEqual(0, second.AppliedCount);
        Assert.HasCount(1, second.AlreadyAppliedRows);
        var (_, boxes, _) = _repository.GetOrder("FBA-1");
        Assert.AreEqual("T001", boxes.Single().TrackingNo);
    }

    /// <summary>같은 박스에 이미 다른 운송장번호가 등록돼 있으면 조용히 덮어쓰지 않고 보고한다.</summary>
    [TestMethod]
    public void Import_BoxAlreadyHasDifferentTrackingNo_ReportsInconsistent()
    {
        SeedOrder("FBA-1", (1, "[SEND] SHIP1 총 1박스중 1번째"));
        _repository.ApplyTracking("FBA-1", 1, "T001");
        WriteResultFile(("[SEND] SHIP1 총 1박스중 1번째", "T999"));

        var result = new FbaTrackingImporter(_repository).Import(_excelFilePath);

        Assert.IsFalse(result.Success);
        Assert.HasCount(1, result.InconsistentBoxes);
        var (_, boxes, _) = _repository.GetOrder("FBA-1");
        Assert.AreEqual("T001", boxes.Single().TrackingNo);
    }

    [TestMethod]
    public void Import_UnmatchedRow_CancelsEntireBatch()
    {
        SeedOrder("FBA-1", (1, "[SEND] SHIP1 총 1박스중 1번째"));
        WriteResultFile(
            ("[SEND] UNKNOWN 총 1박스중 1번째", "T900"),
            ("[SEND] SHIP1 총 1박스중 1번째", "T001"));

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

    /// <summary>필수 컬럼이 없으면 어떤 파일을 넣어야 하는지 알려주는 예외를 던진다.</summary>
    [TestMethod]
    public void Import_FileWithoutRequiredColumns_Throws()
    {
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Sheet1");
            sheet.Cells[1, 1].Value = "엉뚱한헤더";
            sheet.Cells[2, 1].Value = "값";
            ExportHelper.SaveExcel(package, _excelFilePath);
        }

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => new FbaTrackingImporter(_repository).Import(_excelFilePath));
        Assert.Contains("고객주문번호", ex.Message);
    }
}

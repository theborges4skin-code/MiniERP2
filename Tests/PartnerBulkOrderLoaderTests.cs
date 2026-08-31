using MiniERP2.DataLoaders;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

/// <summary>
/// 거래처 마감보드 엑셀 일괄 추가(PartnerBulkOrderImportDialog)의 파싱 로직만 다룬다 — CSKU 매핑
/// 조회/DB 삽입은 UI 다이얼로그와 OutboundRepositoryTests가 담당한다.
/// </summary>
[TestClass]
public class PartnerBulkOrderLoaderTests
{
    private string _testFolder = string.Empty;
    private string _excelFilePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        _excelFilePath = Path.Combine(_testFolder, "bulk_orders.xlsx");
        ExcelLicense.Ensure();
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_testFolder, recursive: true);
    }

    private void WriteAndSave(Action<ExcelWorksheet> writeRows)
    {
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Sheet1");
        sheet.Cells[1, 1].Value = PartnerBulkOrderLoader.SaleDateHeader;
        sheet.Cells[1, 2].Value = PartnerBulkOrderLoader.QtyHeader;
        sheet.Cells[1, 3].Value = PartnerBulkOrderLoader.CskuHeader;
        sheet.Cells[1, 4].Value = PartnerBulkOrderLoader.UnitPriceHeader;
        writeRows(sheet);
        package.SaveAs(new FileInfo(_excelFilePath));
    }

    [TestMethod]
    public void Load_ValidRows_ParsesAllFieldsWithoutErrors()
    {
        WriteAndSave(sheet =>
        {
            sheet.Cells[2, 1].Value = new DateTime(2026, 8, 5);
            sheet.Cells[2, 2].Value = 3;
            sheet.Cells[2, 3].Value = "CSKU-1";
            sheet.Cells[2, 4].Value = 12000;

            sheet.Cells[3, 1].Value = new DateTime(2026, 8, 6);
            sheet.Cells[3, 2].Value = 1;
            sheet.Cells[3, 3].Value = "CSKU-2";
            sheet.Cells[3, 4].Value = 5000;
        });

        var rows = PartnerBulkOrderLoader.Load(_excelFilePath);

        Assert.HasCount(2, rows);
        Assert.IsEmpty(rows[0].Errors);
        Assert.AreEqual(new DateTime(2026, 8, 5), rows[0].SaleDate);
        Assert.AreEqual(3, rows[0].Qty);
        Assert.AreEqual("CSKU-1", rows[0].CskuCode);
        Assert.AreEqual(12000m, rows[0].UnitPrice);
    }

    [TestMethod]
    public void Load_MissingRequiredHeader_Throws()
    {
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Sheet1");
        sheet.Cells[1, 1].Value = PartnerBulkOrderLoader.SaleDateHeader;
        sheet.Cells[1, 2].Value = PartnerBulkOrderLoader.QtyHeader;
        // CSKU/단가 헤더를 일부러 빠뜨린다.
        package.SaveAs(new FileInfo(_excelFilePath));

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => PartnerBulkOrderLoader.Load(_excelFilePath));
        StringAssert.Contains(ex.Message, PartnerBulkOrderLoader.CskuHeader);
        StringAssert.Contains(ex.Message, PartnerBulkOrderLoader.UnitPriceHeader);
    }

    [TestMethod]
    public void Load_BlankCskuOrNonPositiveQty_RecordsRowErrors()
    {
        WriteAndSave(sheet =>
        {
            // CSKU 없음
            sheet.Cells[2, 1].Value = new DateTime(2026, 8, 5);
            sheet.Cells[2, 2].Value = 1;
            sheet.Cells[2, 4].Value = 1000;

            // 수량 0 이하
            sheet.Cells[3, 1].Value = new DateTime(2026, 8, 6);
            sheet.Cells[3, 2].Value = 0;
            sheet.Cells[3, 3].Value = "CSKU-1";
            sheet.Cells[3, 4].Value = 1000;
        });

        var rows = PartnerBulkOrderLoader.Load(_excelFilePath);

        Assert.HasCount(2, rows);
        Assert.IsNotEmpty(rows[0].Errors);
        Assert.IsNotEmpty(rows[1].Errors);
    }

    [TestMethod]
    public void Load_TrailingBlankRow_IsSkipped()
    {
        WriteAndSave(sheet =>
        {
            sheet.Cells[2, 1].Value = new DateTime(2026, 8, 5);
            sheet.Cells[2, 2].Value = 1;
            sheet.Cells[2, 3].Value = "CSKU-1";
            sheet.Cells[2, 4].Value = 1000;
            // 3행은 완전히 비워둔다(엑셀에서 흔한 트레일링 빈 행).
        });

        var rows = PartnerBulkOrderLoader.Load(_excelFilePath);

        Assert.HasCount(1, rows);
    }
}

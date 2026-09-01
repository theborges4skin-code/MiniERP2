using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class CskuStatExporterTests
{
    private string _filePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"CskuStatExporterTests_{Guid.NewGuid()}.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    private static CskuStatBatch Batch() => new() { Id = 1, Period = "2026-08", Memo = "메모", ExchangeRate = 9.5m, CreatedAt = new DateTime(2026, 8, 12) };

    [TestMethod]
    public void Export_AlwaysWritesCoreSheets_AndSkipsEmptyOptionalSheets()
    {
        CskuStatExporter.Export(Batch(), [], [], [], _filePath, includeRawSheet: false, code => code);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheetNames = package.Workbook.Worksheets.Select(s => s.Name).ToList();

        CollectionAssert.Contains(sheetNames, "CSKU집계");
        CollectionAssert.Contains(sheetNames, "예외·미매핑");
        CollectionAssert.Contains(sheetNames, "로드정보");
        CollectionAssert.DoesNotContain(sheetNames, "로켓그로스");
        CollectionAssert.DoesNotContain(sheetNames, "아마존");
        CollectionAssert.DoesNotContain(sheetNames, "원본행");
    }

    [TestMethod]
    public void Export_IncludeRawSheet_AddsRawSheet()
    {
        var rows = new List<CskuStatSourceRow>
        {
            new() { FileName = "a.xlsx", ChannelCode = "COUPANG", CskuCode = "CSKU1", Qty = 1, Revenue = 100m, Status = "매핑(1:1)", RowClass = CskuStatRowClass.Normal },
        };

        CskuStatExporter.Export(Batch(), [], rows, [], _filePath, includeRawSheet: true, code => code);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        Assert.IsNotNull(package.Workbook.Worksheets["원본행"]);
    }

    [TestMethod]
    public void Export_RocketGrossSheet_RenamesShippingHeader()
    {
        var lines = new List<CskuStatLine>
        {
            new() { FileKind = CskuFileKind.RocketGross, ChannelCode = "COUPANG", ChannelName = "쿠팡로켓", CskuCode = "CSKU1", Qty = 1, Revenue = 100m, Shipping = 10m },
        };

        CskuStatExporter.Export(Batch(), lines, [], [], _filePath, includeRawSheet: false, code => code);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["로켓그로스"];
        Assert.IsNotNull(sheet);
        Assert.AreEqual("그로스배송비", sheet.Cells[1, 12].Value);
    }

    [TestMethod]
    public void Export_AmazonSheet_AddsConvertedColumns()
    {
        var lines = new List<CskuStatLine>
        {
            new() { FileKind = CskuFileKind.Amazon, ChannelCode = "AMZUS", ChannelName = "아마존US", CskuCode = "CSKU1", Qty = 1, Revenue = 100m, Settlement = 90m, Profit = 20m },
        };

        CskuStatExporter.Export(Batch(), lines, [], [], _filePath, includeRawSheet: false, code => code);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["아마존"];
        Assert.IsNotNull(sheet);
        Assert.AreEqual("매출액(원)", sheet.Cells[1, 16].Value);
        Assert.AreEqual(950d, Convert.ToDouble(sheet.Cells[2, 16].Value));
        Assert.AreEqual(9.5d, Convert.ToDouble(sheet.Cells[2, 19].Value));
    }

    [TestMethod]
    public void Export_ExceptionSheet_ClassifiesExcludedVsUnmapped()
    {
        var rows = new List<CskuStatSourceRow>
        {
            new() { FileName = "a.xlsx", ChannelCode = "COUPANG", Status = "제외(배송비 등)", RowClass = CskuStatRowClass.Excluded },
            new() { FileName = "a.xlsx", ChannelCode = "COUPANG", Status = "매핑 실패", RowClass = CskuStatRowClass.Unmapped },
        };

        CskuStatExporter.Export(Batch(), [], rows, [], _filePath, includeRawSheet: false, code => code == "COUPANG" ? "쿠팡" : code);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["예외·미매핑"];
        Assert.AreEqual("예외", sheet.Cells[2, 16].Value);
        Assert.AreEqual("미매핑", sheet.Cells[3, 16].Value);
        Assert.AreEqual("쿠팡", sheet.Cells[2, 4].Value);
    }

    [TestMethod]
    public void Export_LoadInfoSheet_WritesBatchHeaderAndFileTable()
    {
        var files = new List<CskuStatFile> { new() { FileName = "a.xlsx", FileKind = CskuFileKind.General, RowCount = 2, SumQty = 3, SumRevenue = 100m, SumProfit = 10m } };
        var rows = new List<CskuStatSourceRow>
        {
            new() { FileName = "a.xlsx", ChannelCode = "COUPANG", Status = "매핑(1:1)", RowClass = CskuStatRowClass.Normal },
            new() { FileName = "a.xlsx", ChannelCode = "COUPANG", Status = "매핑 실패", RowClass = CskuStatRowClass.Unmapped },
        };

        CskuStatExporter.Export(Batch(), [], rows, files, _filePath, includeRawSheet: false, code => code);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["로드정보"];
        Assert.AreEqual("2026-08", sheet.Cells[1, 2].Value);
        Assert.AreEqual("a.xlsx", sheet.Cells[8, 1].Value);
        Assert.AreEqual(1, Convert.ToInt32(sheet.Cells[8, 4].Value));
        Assert.AreEqual(1, Convert.ToInt32(sheet.Cells[8, 6].Value));
    }
}

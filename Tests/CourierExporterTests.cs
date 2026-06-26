using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class CourierExporterTests
{
    private string _filePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"CourierExporterTests_{Guid.NewGuid()}.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    [TestMethod]
    public async Task ExportAsync_WritesHeadersAndMappedValuesPerCourierFormat()
    {
        var courier = new CourierMaster
        {
            CourierName = "테스트택배",
            HeaderMappingJson = """{ "받는분": "Recipient", "연락처": "Phone", "운송장번호": "TrackingNo" }"""
        };
        var orders = new List<OfsOrderItem>
        {
            new() { Recipient = "홍길동", Phone = "010-1234-5678", TrackingNo = "T001" },
            new() { Recipient = "김철수", Phone = "010-9876-5432", TrackingNo = "T002" },
        };

        var exporter = new CourierExporter();
        await exporter.ExportAsync(orders, courier, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["Sheet1"];

        Assert.AreEqual("받는분", sheet.Cells[1, 1].Value);
        Assert.AreEqual("연락처", sheet.Cells[1, 2].Value);
        Assert.AreEqual("운송장번호", sheet.Cells[1, 3].Value);

        Assert.AreEqual("홍길동", sheet.Cells[2, 1].Value);
        Assert.AreEqual("010-1234-5678", sheet.Cells[2, 2].Value);
        Assert.AreEqual("T001", sheet.Cells[2, 3].Value);

        Assert.AreEqual("김철수", sheet.Cells[3, 1].Value);
        Assert.AreEqual("T002", sheet.Cells[3, 3].Value);
    }
}

using System.Data;
using MiniERP2.DataManagement;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class ManagedTableExcelIOTests
{
    private string _filePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"ManagedTableExcelIOTests_{Guid.NewGuid()}.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    private static DataTable BuildSampleTable()
    {
        var table = new DataTable();
        table.Columns.Add("Sku", typeof(string));
        table.Columns.Add("ItemName", typeof(string));
        table.Columns.Add("CostPrice", typeof(decimal));
        table.Rows.Add("SKU-1", "상품A", 1000m);
        table.Rows.Add("SKU-2", "상품B", 2000m);
        table.AcceptChanges();
        return table;
    }

    [TestMethod]
    public void Export_OnlySelectedColumns_WritesJustThoseHeaders()
    {
        var table = BuildSampleTable();

        var rowCount = ManagedTableExcelIO.Export(table, ["Sku", "CostPrice"], null, null, _filePath);

        Assert.AreEqual(2, rowCount);
        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["Sheet1"];
        Assert.AreEqual("Sku", sheet.Cells[1, 1].Value);
        Assert.AreEqual("CostPrice", sheet.Cells[1, 2].Value);
        Assert.AreEqual("SKU-1", sheet.Cells[2, 1].Value);
    }

    [TestMethod]
    public void Export_WithFilter_OnlyExportsMatchingRows()
    {
        var table = BuildSampleTable();

        var rowCount = ManagedTableExcelIO.Export(table, ["Sku", "ItemName"], "Sku", "SKU-2", _filePath);

        Assert.AreEqual(1, rowCount);
        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["Sheet1"];
        Assert.AreEqual("상품B", sheet.Cells[2, 2].Value);
    }

    [TestMethod]
    public void Read_ReturnsHeadersAndRowsAsText()
    {
        var table = BuildSampleTable();
        ManagedTableExcelIO.Export(table, ["Sku", "ItemName", "CostPrice"], null, null, _filePath);

        var (headers, rows) = ManagedTableExcelIO.Read(_filePath);

        Assert.HasCount(3, headers);
        Assert.HasCount(2, rows);
        Assert.AreEqual("SKU-1", rows[0]["Sku"]);
        Assert.AreEqual("상품A", rows[0]["ItemName"]);
    }

    [TestMethod]
    public void ConvertValue_DecimalColumn_ParsesNumericText()
    {
        var result = ManagedTableExcelIO.ConvertValue(typeof(decimal), "1500.5");

        Assert.AreEqual(1500.5m, result);
    }

    [TestMethod]
    public void ConvertValue_InvalidNumberForDecimalColumn_ReturnsDbNull()
    {
        var result = ManagedTableExcelIO.ConvertValue(typeof(decimal), "숫자아님");

        Assert.AreEqual(DBNull.Value, result);
    }

    [TestMethod]
    public void ConvertValue_EmptyTextForStringColumn_ReturnsNull()
    {
        var result = ManagedTableExcelIO.ConvertValue(typeof(string), "");

        Assert.IsNull(result);
    }
}

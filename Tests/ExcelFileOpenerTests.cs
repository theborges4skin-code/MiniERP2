using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class ExcelFileOpenerTests
{
    private string _filePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"ExcelFileOpenerTests_{Guid.NewGuid()}.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    [TestMethod]
    public void Open_PasswordProtectedFile_WithoutPassword_ThrowsEncryptedExcelFileException()
    {
        CreatePasswordProtectedFile("secret123");

        Assert.ThrowsExactly<EncryptedExcelFileException>(() =>
        {
            using var package = ExcelFileOpener.Open(_filePath);
        });
    }

    [TestMethod]
    public void Open_PasswordProtectedFile_WithCorrectPassword_Succeeds()
    {
        CreatePasswordProtectedFile("secret123");

        using var package = ExcelFileOpener.Open(_filePath, "secret123");

        Assert.AreEqual("상품A", package.Workbook.Worksheets[0].Cells[1, 1].Value);
    }

    [TestMethod]
    public void Open_PlainFile_OpensWithoutPassword()
    {
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Sheet1");
            sheet.Cells[1, 1].Value = "값1";
            package.SaveAs(new FileInfo(_filePath));
        }

        using var opened = ExcelFileOpener.Open(_filePath);
        Assert.AreEqual("값1", opened.Workbook.Worksheets[0].Cells[1, 1].Value);
    }

    private void CreatePasswordProtectedFile(string password)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Sheet1");
        sheet.Cells[1, 1].Value = "상품A";
        package.SaveAs(new FileInfo(_filePath), password);
    }
}

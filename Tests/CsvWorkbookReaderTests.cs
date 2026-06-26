using System.Text;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class CsvWorkbookReaderTests
{
    private string _filePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"CsvWorkbookReaderTests_{Guid.NewGuid()}.csv");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    [TestMethod]
    public void LoadAsPackage_Utf8WithBom_ParsesHeaderAndRows()
    {
        var content = "상품명,옵션명,수량\r\n상품A,옵션1,2\r\n상품B,옵션2,3\r\n";
        File.WriteAllText(_filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        using var package = CsvWorkbookReader.LoadAsPackage(_filePath);
        var sheet = package.Workbook.Worksheets[0];

        Assert.AreEqual("상품명", sheet.Cells[1, 1].Value);
        Assert.AreEqual("상품A", sheet.Cells[2, 1].Value);
        Assert.AreEqual("2", sheet.Cells[2, 3].Value);
        Assert.AreEqual("상품B", sheet.Cells[3, 1].Value);
    }

    [TestMethod]
    public void LoadAsPackage_Cp949Encoded_DecodesKoreanCorrectly()
    {
        var content = "상품명,수량\r\n테스트상품,1\r\n";
        var cp949 = System.Text.CodePagesEncodingProvider.Instance.GetEncoding(949)!;
        File.WriteAllBytes(_filePath, cp949.GetBytes(content));

        using var package = CsvWorkbookReader.LoadAsPackage(_filePath);
        var sheet = package.Workbook.Worksheets[0];

        Assert.AreEqual("상품명", sheet.Cells[1, 1].Value);
        Assert.AreEqual("테스트상품", sheet.Cells[2, 1].Value);
    }

    [TestMethod]
    public void LoadAsPackage_QuotedFieldWithEmbeddedComma_ParsesAsSingleField()
    {
        var content = "상품명,비고\r\n\"상품A, 한정판\",특이사항 없음\r\n";
        File.WriteAllText(_filePath, content, Encoding.UTF8);

        using var package = CsvWorkbookReader.LoadAsPackage(_filePath);
        var sheet = package.Workbook.Worksheets[0];

        Assert.AreEqual("상품A, 한정판", sheet.Cells[2, 1].Value);
        Assert.AreEqual("특이사항 없음", sheet.Cells[2, 2].Value);
    }
}

using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class DocumentExporterLegacyStatementTests
{
    private string _filePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"DocStatementExportTests_{Guid.NewGuid()}.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    private static LegacyStatementExportItem MakeItem(string sheetName, decimal storedSupply, decimal storedTax, decimal storedTotal)
    {
        var supplier = new DocParty { CompanyName = "(주)신안코퍼레이션", RegNo = "1078755466" };
        var buyer = new DocParty { CompanyName = "테스트거래처", RegNo = "1111111111" };
        var statement = new DocStatement
        {
            SourceFileName = "f.xlsx",
            SourceSheetName = sheetName,
            IssueDate = new DateTime(2024, 3, 15),
            TotalSupply = storedSupply,
            TotalTax = storedTax,
            TotalAmount = storedTotal,
            TotalQty = 1,
        };
        var lines = new List<DocStatementLine>
        {
            new() { ItemName = "품목A", Qty = 1, UnitPrice = 999, SupplyAmount = 909, Tax = 90, Total = 999 },
        };
        return new LegacyStatementExportItem(statement, supplier, buyer, lines);
    }

    [TestMethod]
    public void ExportLegacyStatements_UsesStoredTotals_NotRecomputedFromLines()
    {
        // 라인 합(909+90=999)과 다른 저장된 합계값을 일부러 넣어, 재계산이 아니라 저장값을 그대로
        // 쓰는지 검증한다(DocStatement 모델 주석의 핵심 요구사항).
        var item = MakeItem("시트1", storedSupply: 12345, storedTax: 1234, storedTotal: 13579);

        DocumentExporter.ExportLegacyStatements(new List<LegacyStatementExportItem> { item }, _filePath);

        using var pkg = new ExcelPackage(new FileInfo(_filePath));
        var ws = pkg.Workbook.Worksheets[0];
        string allText = string.Join("|", Enumerable.Range(1, ws.Dimension.End.Row)
            .SelectMany(r => Enumerable.Range(1, ws.Dimension.End.Column).Select(c => ws.Cells[r, c].Text)));

        // 셀 Numberformat이 "#,##0"이라 .Text는 천단위 콤마가 붙은 형태로 렌더링된다.
        StringAssert.Contains(allText, "12,345");
        StringAssert.Contains(allText, "1,234");
        StringAssert.Contains(allText, "13,579");
    }

    [TestMethod]
    public void ExportLegacyStatements_DuplicateSheetNames_AreMadeUniqueWithoutThrowing()
    {
        var items = new List<LegacyStatementExportItem>
        {
            MakeItem("같은시트명", 1000, 100, 1100),
            MakeItem("같은시트명", 2000, 200, 2200),
            MakeItem("같은시트명", 3000, 300, 3300),
        };

        DocumentExporter.ExportLegacyStatements(items, _filePath);

        using var pkg = new ExcelPackage(new FileInfo(_filePath));
        Assert.AreEqual(3, pkg.Workbook.Worksheets.Count);
        var names = pkg.Workbook.Worksheets.Select(w => w.Name).ToList();
        Assert.AreEqual(3, names.Distinct().Count());
    }

    [TestMethod]
    public void ExportLegacyStatements_WritesSupplierAndBuyerCompanyNames()
    {
        var item = MakeItem("시트1", 900, 90, 990);

        DocumentExporter.ExportLegacyStatements(new List<LegacyStatementExportItem> { item }, _filePath);

        using var pkg = new ExcelPackage(new FileInfo(_filePath));
        var ws = pkg.Workbook.Worksheets[0];
        string allText = string.Join("|", Enumerable.Range(1, ws.Dimension.End.Row)
            .SelectMany(r => Enumerable.Range(1, ws.Dimension.End.Column).Select(c => ws.Cells[r, c].Text)));

        StringAssert.Contains(allText, "신안코퍼레이션");
        StringAssert.Contains(allText, "테스트거래처");
        StringAssert.Contains(allText, "품목A");
    }
}

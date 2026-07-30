using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class DocumentExporterSalesLedgerCombinedTests
{
    private string _testFolder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_testFolder, recursive: true);

    private static SalesLedgerDoc SimpleLedger(string buyerName) => new()
    {
        Supplier = new DocParty { CompanyName = "공급자" },
        Buyer = new DocParty { CompanyName = buyerName },
        Lines = [new SalesLedgerLineItem { Year = 2026, Month = 7, Day = 1, ItemName = "상품A", Qty = 1, UnitPrice = 10000m, CostPrice = 4000m }],
    };

    [TestMethod]
    public void ExportSalesLedgersCombined_WritesOverviewSheetPlusOneSheetPerConfirmedParty()
    {
        // 거래처마감보드 §9 요청: a,b,c,d,e 중 a,b,c만 마감확정 → 현황 시트 1개(전부) + 확정된 3개 시트.
        var overview = new List<SalesLedgerOverviewRow>
        {
            new() { PartyName = "A", Status = "확정", IsConfirmed = true, TotalQty = 1, TotalSupply = 10000m, TotalProfit = 6000m },
            new() { PartyName = "B", Status = "확정", IsConfirmed = true, TotalQty = 1, TotalSupply = 10000m, TotalProfit = 6000m },
            new() { PartyName = "C", Status = "확정", IsConfirmed = true, TotalQty = 1, TotalSupply = 10000m, TotalProfit = 6000m },
            new() { PartyName = "D", Status = "미확인", IsConfirmed = false, TotalQty = 2, TotalSupply = 20000m, TotalProfit = 8000m },
            new() { PartyName = "E", Status = "대조중", IsConfirmed = false, TotalQty = 0, TotalSupply = 0m, TotalProfit = 0m },
        };
        var ledgers = new List<(string PartyName, SalesLedgerDoc Doc)>
        {
            ("A", SimpleLedger("A")),
            ("B", SimpleLedger("B")),
            ("C", SimpleLedger("C")),
        };
        var filePath = Path.Combine(_testFolder, "combined.xlsx");

        DocumentExporter.ExportSalesLedgersCombined(overview, ledgers, filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(filePath));
        var sheetNames = package.Workbook.Worksheets.Select(w => w.Name).ToList();

        // 현황 시트 1개 + 확정 거래처(A,B,C) 시트 3개 = 총 4개. D,E는 매출장 시트가 생기지 않아야 한다.
        Assert.HasCount(4, sheetNames);
        Assert.AreEqual("현황", sheetNames[0]);
        CollectionAssert.Contains(sheetNames, "A");
        CollectionAssert.Contains(sheetNames, "B");
        CollectionAssert.Contains(sheetNames, "C");
        CollectionAssert.DoesNotContain(sheetNames, "D");
        CollectionAssert.DoesNotContain(sheetNames, "E");

        var overviewSheet = package.Workbook.Worksheets["현황"];
        var overviewText = string.Join(" ", Enumerable.Range(1, overviewSheet.Dimension.End.Row)
            .Select(r => overviewSheet.Cells[r, 1].Text));
        StringAssert.Contains(overviewText, "D"); // 미확정 거래처도 현황표엔 나와야 함
        StringAssert.Contains(overviewText, "E");
    }
}

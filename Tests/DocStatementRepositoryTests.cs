using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class DocStatementRepositoryTests
{
    private string _testFolder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private static DocStatement MakeStatement(int partyId, DateTime issueDate, string sheetName, params (string Item, decimal Qty, decimal UnitPrice)[] lines)
    {
        var statement = new DocStatement
        {
            PartyId = partyId,
            IssueDate = issueDate,
            IssueYearMonth = issueDate.ToString("yyyy-MM"),
            SourceFileName = "f.xlsx",
            SourceSheetName = sheetName,
            TemplateSignature = "N-INC",
        };
        int rowNo = 1;
        foreach (var (item, qty, unitPrice) in lines)
        {
            decimal total = qty * unitPrice;
            decimal supply = Math.Round(total / 1.1m, 0, MidpointRounding.AwayFromZero);
            statement.Lines.Add(new DocStatementLine
            {
                RowNo = rowNo++,
                ItemName = item,
                Qty = qty,
                UnitPrice = unitPrice,
                SupplyAmount = supply,
                Tax = total - supply,
                Total = total,
            });
        }
        statement.TotalSupply = statement.Lines.Sum(l => l.SupplyAmount);
        statement.TotalTax = statement.Lines.Sum(l => l.Tax);
        statement.TotalAmount = statement.Lines.Sum(l => l.Total);
        statement.TotalQty = statement.Lines.Sum(l => l.Qty);
        return statement;
    }

    [TestMethod]
    public void GetFiltered_ByPartyId_ReturnsOnlyThatPartysStatements()
    {
        var repo = new DocStatementRepository();
        repo.Upsert(MakeStatement(1, new DateTime(2024, 1, 10), "s1", ("A", 1, 1000)));
        repo.Upsert(MakeStatement(2, new DateTime(2024, 1, 15), "s2", ("B", 1, 2000)));

        var result = repo.GetFiltered(partyId: 1, from: null, to: null);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("s1", result[0].SourceSheetName);
    }

    [TestMethod]
    public void GetFiltered_ByDateRange_ExcludesStatementsOutsideRange()
    {
        var repo = new DocStatementRepository();
        repo.Upsert(MakeStatement(1, new DateTime(2024, 1, 10), "jan", ("A", 1, 1000)));
        repo.Upsert(MakeStatement(1, new DateTime(2024, 3, 10), "mar", ("B", 1, 1000)));

        var result = repo.GetFiltered(partyId: null, from: new DateTime(2024, 2, 1), to: new DateTime(2024, 2, 28));

        Assert.AreEqual(0, result.Count);

        var febToMar = repo.GetFiltered(partyId: null, from: new DateTime(2024, 2, 1), to: new DateTime(2024, 3, 31));
        Assert.AreEqual(1, febToMar.Count);
        Assert.AreEqual("mar", febToMar[0].SourceSheetName);
    }

    [TestMethod]
    public void GetFiltered_NoFilters_ReturnsAll()
    {
        var repo = new DocStatementRepository();
        repo.Upsert(MakeStatement(1, new DateTime(2024, 1, 10), "s1", ("A", 1, 1000)));
        repo.Upsert(MakeStatement(2, new DateTime(2024, 5, 1), "s2", ("B", 1, 1000)));

        var result = repo.GetFiltered(null, null, null);

        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void GetLines_ReturnsLinesInRowNoOrder()
    {
        var repo = new DocStatementRepository();
        var statement = MakeStatement(1, new DateTime(2024, 1, 1), "multi",
            ("첫줄", 1, 1000), ("둘째줄", 2, 2000), ("셋째줄", 3, 3000));
        repo.Upsert(statement);

        var lines = repo.GetLines(statement.Id);

        Assert.AreEqual(3, lines.Count);
        CollectionAssert.AreEqual(new[] { "첫줄", "둘째줄", "셋째줄" }, lines.Select(l => l.ItemName).ToList());
    }
}

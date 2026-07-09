using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Migration;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class LegacyStatementCommitServiceTests
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

    private static ParsedStatementSheet MakeSheet(string fileName, string sheetName, string regNo, string company, decimal qty = 1, decimal total = 1100)
    {
        var sheet = new ParsedStatementSheet
        {
            SourceFileName = fileName,
            SourceSheetName = sheetName,
            TemplateSignature = "N-INC",
            Buyer = new ParsedPartyInfo { RegNo = regNo, CompanyName = company },
            IssueDate = new DateTime(2024, 1, 1),
        };
        sheet.Lines.Add(new ParsedStatementLine
        {
            ItemName = "테스트품목",
            Qty = qty,
            UnitPrice = total / qty,
            Total = total,
            SupplyAmount = Math.Round(total / 1.1m, 0, MidpointRounding.AwayFromZero),
            Tax = total - Math.Round(total / 1.1m, 0, MidpointRounding.AwayFromZero),
        });
        return sheet;
    }

    private static int CountLinesInDb(int statementId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM DocStatementLineTable WHERE StatementId = $id";
        cmd.Parameters.AddWithValue("$id", statementId);
        return (int)(long)cmd.ExecuteScalar()!;
    }

    [TestMethod]
    public void Commit_RegNoMatchesExistingParty_ReusesPartyWithoutOverwritingFields()
    {
        var partyRepo = new DocPartyRepository();
        var existing = new DocParty { RegNo = "111-11-11111", CompanyName = "기존거래처", ChannelCode = "COUPANG" };
        partyRepo.Save(existing);
        Assert.IsTrue(existing.IsActive); // ChannelCode가 있으므로 활성

        var service = new LegacyStatementCommitService();
        var sheet = MakeSheet("f.xlsx", "s1", "111-11-11111", "기존거래처(다른표기)");

        var result = service.Commit(new[] { sheet });

        Assert.AreEqual(0, result.NewPartiesCreated);
        Assert.AreEqual(1, result.ExistingPartiesReused);
        Assert.AreEqual(1, result.StatementsSaved);

        var reloaded = partyRepo.FindByRegNo("111-11-11111")!;
        Assert.AreEqual("기존거래처", reloaded.CompanyName); // 마이그레이션이 기존 필드를 덮어쓰지 않음
        Assert.IsTrue(reloaded.IsActive); // 기존 활성 상태 보존
    }

    [TestMethod]
    public void Commit_UnknownRegNo_CreatesNewInactiveParty()
    {
        var service = new LegacyStatementCommitService();
        var sheet = MakeSheet("f.xlsx", "s1", "999-99-99999", "신규거래처");

        var result = service.Commit(new[] { sheet });

        Assert.AreEqual(1, result.NewPartiesCreated);
        Assert.AreEqual(0, result.ExistingPartiesReused);

        var created = new DocPartyRepository().FindByRegNo("999-99-99999");
        Assert.IsNotNull(created);
        Assert.IsFalse(created!.IsActive); // ChannelCode 없음 -> 비활성
    }

    [TestMethod]
    public void Commit_AnonymousBuyersWithSameName_CreatesSeparatePartiesWithoutMerging()
    {
        var service = new LegacyStatementCommitService();
        var sheet1 = MakeSheet("f1.xlsx", "미국손님", "", "");
        var sheet2 = MakeSheet("f2.xlsx", "미국손님 (2)", "", "");

        var result = service.Commit(new[] { sheet1, sheet2 });

        Assert.AreEqual(2, result.NewPartiesCreated); // 이름/등록번호가 같아도(둘 다 공란) 병합하지 않는다
        Assert.AreEqual(0, result.ExistingPartiesReused);
    }

    [TestMethod]
    public void Commit_SameSourceTwice_ReplacesLinesWithoutDuplication()
    {
        var service = new LegacyStatementCommitService();
        var statementRepo = new DocStatementRepository();
        var sheet = MakeSheet("f.xlsx", "s1", "222-22-22222", "재실행거래처");

        service.Commit(new[] { sheet });
        var firstId = statementRepo.GetAll().Single().Id;
        Assert.AreEqual(1, CountLinesInDb(firstId));

        // 재실행: 같은 (파일명, 시트명) — replace 정책이므로 라인이 누적되지 않아야 한다.
        var sheetAgain = MakeSheet("f.xlsx", "s1", "222-22-22222", "재실행거래처");
        var secondResult = service.Commit(new[] { sheetAgain });

        Assert.AreEqual(0, secondResult.NewPartiesCreated); // 등록번호로 기존 거래처 재사용
        Assert.AreEqual(1, secondResult.ExistingPartiesReused);

        var statements = statementRepo.GetAll();
        Assert.AreEqual(1, statements.Count); // 헤더도 중복되지 않음(UNIQUE 재사용)
        Assert.AreEqual(1, CountLinesInDb(statements[0].Id));
    }

    [TestMethod]
    public void Commit_SheetWithNoLines_IsSkipped()
    {
        var service = new LegacyStatementCommitService();
        var emptySheet = new ParsedStatementSheet { SourceFileName = "f.xlsx", SourceSheetName = "빈시트" };

        var result = service.Commit(new[] { emptySheet });

        Assert.AreEqual(1, result.StatementsSkipped);
        Assert.AreEqual(0, result.StatementsSaved);
    }
}

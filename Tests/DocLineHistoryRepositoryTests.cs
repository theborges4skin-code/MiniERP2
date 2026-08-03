using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class DocLineHistoryRepositoryTests
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

    private static DocLineHistory MakeLine(
        string channelCode, string cskuCode, DocLineHistoryType docType, DateTime issueDate, decimal total = 1000m) => new()
    {
        DocGroupKey = $"{docType}-{channelCode}-{issueDate:yyyyMMdd}",
        DocNo = "TEST0001",
        DocType = docType,
        ChannelCode = channelCode,
        ChannelName = channelCode + "이름",
        CskuCode = cskuCode,
        ItemNameSnap = "테스트품목",
        Qty = 1,
        UnitPrice = total,
        SupplyAmount = total,
        Tax = 0,
        Total = total,
        IssueDate = issueDate,
        CreatedAt = DateTime.Now,
    };

    [TestMethod]
    public void Add_ThenQuery_ReturnsSameRecordWithDerivedYearMonthAndQuarter()
    {
        var repo = new DocLineHistoryRepository();
        repo.Add(MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 7, 15)));

        var result = repo.Query(channelCode: "CH001").Single();

        Assert.AreEqual("CSKU-A", result.CskuCode);
        Assert.AreEqual("2026-07", result.YearMonth);
        Assert.AreEqual("2026-Q3", result.Quarter);
    }

    [TestMethod]
    public void Query_FiltersByChannelAndCsku_AcrossDocTypes()
    {
        // 조회축 핵심 요구: 같은 채널×CSKU면 문서유형이 달라도(견적/명세/가격조정) 함께 뽑혀야 한다.
        var repo = new DocLineHistoryRepository();
        repo.AddRange(new[]
        {
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 5, 1)),
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Statement, new DateTime(2026, 6, 1)),
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.PriceAdjustment, new DateTime(2026, 7, 1)),
            MakeLine("CH001", "CSKU-B", DocLineHistoryType.Quote, new DateTime(2026, 7, 1)),
            MakeLine("CH002", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 7, 1)),
        });

        var result = repo.Query(channelCode: "CH001", cskuCode: "CSKU-A");

        Assert.AreEqual(3, result.Count);
        CollectionAssert.AreEquivalent(
            new[] { DocLineHistoryType.Quote, DocLineHistoryType.Statement, DocLineHistoryType.PriceAdjustment },
            result.Select(r => r.DocType).ToList());
    }

    [TestMethod]
    public void Query_ByDocType_FiltersToThatTypeOnly()
    {
        var repo = new DocLineHistoryRepository();
        repo.AddRange(new[]
        {
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 7, 1)),
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Statement, new DateTime(2026, 7, 1)),
        });

        var result = repo.Query(docType: DocLineHistoryType.Statement);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(DocLineHistoryType.Statement, result[0].DocType);
    }

    [TestMethod]
    public void Query_ByYearMonth_MatchesOnlyThatMonth()
    {
        var repo = new DocLineHistoryRepository();
        repo.AddRange(new[]
        {
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 6, 30)),
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 7, 1)),
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 7, 31)),
        });

        var result = repo.Query(yearMonth: "2026-07");

        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void Query_ByQuarter_GroupsThreeMonthsTogether()
    {
        var repo = new DocLineHistoryRepository();
        repo.AddRange(new[]
        {
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 7, 1)),  // Q3
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 8, 1)),  // Q3
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 9, 30)), // Q3
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 10, 1)), // Q4
        });

        var result = repo.Query(quarter: "2026-Q3");

        Assert.AreEqual(3, result.Count);
    }

    [TestMethod]
    public void Query_UnmappedOnly_ReturnsOnlyEmptyCskuCode()
    {
        // G5 미매핑 버킷 — CSKU 없는 자유품목은 조회뷰에서 명확히 구분돼야 한다.
        var repo = new DocLineHistoryRepository();
        repo.AddRange(new[]
        {
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 7, 1)),
            MakeLine("CH001", "", DocLineHistoryType.Quote, new DateTime(2026, 7, 1)),
        });

        var result = repo.Query(cskuCodeIsUnmappedOnly: true);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("", result[0].CskuCode);
    }

    [TestMethod]
    public void Query_ByDateRange_ExcludesOutOfRangeIssueDates()
    {
        var repo = new DocLineHistoryRepository();
        repo.AddRange(new[]
        {
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 6, 30)),
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 7, 15)),
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 8, 1)),
        });

        var result = repo.Query(from: new DateTime(2026, 7, 1), to: new DateTime(2026, 7, 31));

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(new DateTime(2026, 7, 15), result[0].IssueDate);
    }

    [TestMethod]
    public void DeleteAll_ClearsEveryRecord()
    {
        var repo = new DocLineHistoryRepository();
        repo.AddRange(new[]
        {
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 7, 1)),
            MakeLine("CH002", "CSKU-B", DocLineHistoryType.Statement, new DateTime(2026, 7, 1)),
        });

        repo.DeleteAll();

        Assert.AreEqual(0, repo.Query().Count);
    }

    [TestMethod]
    public void GetCskuSummary_GroupsByChannelAndCsku_AcrossDocTypes()
    {
        // 문서관리 메인창 레벨1 요약 — 같은 채널×CSKU면 문서유형이 달라도 한 행으로 묶여야 한다.
        var repo = new DocLineHistoryRepository();
        repo.AddRange(new[]
        {
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 5, 1), total: 9000m),
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Statement, new DateTime(2026, 7, 15), total: 9500m),
            MakeLine("CH001", "CSKU-B", DocLineHistoryType.Quote, new DateTime(2026, 7, 1), total: 5000m),
        });

        var summary = repo.GetCskuSummary(channelCode: "CH001");

        Assert.AreEqual(2, summary.Count);
        var cskuA = summary.Single(s => s.CskuCode == "CSKU-A");
        Assert.AreEqual(2, cskuA.DocCount);
    }

    [TestMethod]
    public void GetCskuSummary_FirstAndLastUnitPrice_MatchEarliestAndLatestIssueDate()
    {
        var repo = new DocLineHistoryRepository();
        repo.AddRange(new[]
        {
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 5, 1), total: 9000m),
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 6, 1), total: 9200m),
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 7, 15), total: 9500m),
        });

        var summary = repo.GetCskuSummary().Single();

        Assert.AreEqual(9000m, summary.FirstUnitPrice);
        Assert.AreEqual(9500m, summary.LastUnitPrice);
        Assert.AreEqual(500m, summary.PriceChange);
        Assert.AreEqual(new DateTime(2026, 5, 1), summary.FirstIssueDate);
        Assert.AreEqual(new DateTime(2026, 7, 15), summary.LastIssueDate);
    }

    [TestMethod]
    public void GetCskuSummary_UnmappedLines_GroupIntoOwnBucket()
    {
        // G5 미매핑 버킷 — CSKU 없는 자유품목도 요약에서 별도 그룹으로 보여야 한다.
        var repo = new DocLineHistoryRepository();
        repo.AddRange(new[]
        {
            MakeLine("CH001", "", DocLineHistoryType.Quote, new DateTime(2026, 7, 1)),
            MakeLine("CH001", "", DocLineHistoryType.Quote, new DateTime(2026, 7, 15)),
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 7, 1)),
        });

        var summary = repo.GetCskuSummary();

        Assert.AreEqual(2, summary.Count);
        var unmapped = summary.Single(s => s.CskuCode == "");
        Assert.AreEqual(2, unmapped.DocCount);
    }

    [TestMethod]
    public void GetDocCountByChannel_CountsDistinctDocumentsNotLines()
    {
        // "1회성 채널 숨기기" 필터의 근거 데이터 — 문서 1건에 CSKU 줄이 여러 개 있어도 그 채널은
        // 여전히 "1건"으로 세야 한다(재주문 여부를 보는 게 목적이지 줄 수를 보는 게 아니므로).
        var repo = new DocLineHistoryRepository();
        var oneDoc = MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 7, 1));
        oneDoc.DocGroupKey = "DOC-1";
        var sameDocOtherLine = MakeLine("CH001", "CSKU-B", DocLineHistoryType.Quote, new DateTime(2026, 7, 1));
        sameDocOtherLine.DocGroupKey = "DOC-1";
        var secondDoc = MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, new DateTime(2026, 8, 1));
        secondDoc.DocGroupKey = "DOC-2";

        repo.AddRange(new[] { oneDoc, sameDocOtherLine, secondDoc });

        var counts = repo.GetDocCountByChannel();

        Assert.AreEqual(2, counts["CH001"]);
    }

    [TestMethod]
    public void GetDocCountByChannel_EmptyDocGroupKey_CountsEachLineSeparately()
    {
        // DocGroupKey가 비어있는 줄은 "그 줄 혼자 문서 1건"이라는 모델 문서화 그대로 — 빈 문자열끼리
        // 하나로 뭉쳐서 세면 안 된다.
        var repo = new DocLineHistoryRepository();
        var line1 = MakeLine("CH002", "CSKU-A", DocLineHistoryType.Statement, new DateTime(2026, 7, 1));
        line1.DocGroupKey = "";
        var line2 = MakeLine("CH002", "CSKU-B", DocLineHistoryType.Statement, new DateTime(2026, 7, 2));
        line2.DocGroupKey = "";

        repo.AddRange(new[] { line1, line2 });

        var counts = repo.GetDocCountByChannel();

        Assert.AreEqual(2, counts["CH002"]);
    }

    [TestMethod]
    public void GenerateNextTempQuoteNo_FirstOfDay_UsesSeq01()
    {
        var repo = new DocLineHistoryRepository();

        var quoteNo = repo.GenerateNextTempQuoteNo(new DateTime(2026, 7, 28));

        Assert.AreEqual("TQ26072801", quoteNo);
    }

    [TestMethod]
    public void GenerateNextTempQuoteNo_CountsDistinctDocNoNotRawLines()
    {
        // 한 문서(견적서 1건)에 줄이 여러 개 있어도 DocNo는 하나만 세야 한다 — 줄 수로 세면
        // 다음 견적서 번호가 실제 발행 건수보다 훨씬 앞서 나가버린다.
        var repo = new DocLineHistoryRepository();
        var date = new DateTime(2026, 7, 28);
        var firstDocNo = repo.GenerateNextTempQuoteNo(date);
        var lines = new[]
        {
            MakeLine("CH001", "CSKU-A", DocLineHistoryType.Quote, date),
            MakeLine("CH001", "CSKU-B", DocLineHistoryType.Quote, date),
            MakeLine("CH001", "CSKU-C", DocLineHistoryType.Quote, date),
        };
        foreach (var line in lines) line.DocNo = firstDocNo;
        repo.AddRange(lines);

        var secondDocNo = repo.GenerateNextTempQuoteNo(date);

        Assert.AreEqual("TQ26072801", firstDocNo);
        Assert.AreEqual("TQ26072802", secondDocNo);
    }
}

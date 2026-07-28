using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class DocHistoryRepositoryTests
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

    [TestMethod]
    public void Add_ThenGetFileBytes_ReturnsSameBytes()
    {
        // 문서발행 이력에서 원본 파일이 이동/삭제돼도 DB 백업본으로 복원해 열 수 있어야 한다는
        // 요청(사용자 신고)에 따라, 저장한 FileBytes가 그대로 조회되는지 확인한다.
        var repo = new DocHistoryRepository();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };

        repo.Add(new DocHistoryRecord
        {
            DocType = "거래명세표(VAT별도)",
            IssueDate = new DateTime(2026, 7, 1),
            BuyerName = "테스트거래처",
            TotalAmount = 10000m,
            FilePath = @"C:\temp\없어진파일.xlsx",
            CreatedAt = DateTime.Now,
            FileBytes = bytes,
        });

        var record = repo.Query(new DateTime(2026, 7, 1), new DateTime(2026, 7, 1)).Single();
        CollectionAssert.AreEqual(bytes, repo.GetFileBytes(record.Id));
    }

    [TestMethod]
    public void Add_WithoutFileBytes_GetFileBytesReturnsNull()
    {
        // 백업 자체가 실패했거나(권한 등) FileBytes 도입 이전 옛 이력은 null이어야 하고, "파일 열기"가
        // 이 경우 기존처럼 안내만 하고 끝나야 한다(DocHistoryForm.OpenSelected 참고).
        var repo = new DocHistoryRepository();

        repo.Add(new DocHistoryRecord
        {
            DocType = "견적서(기본)",
            IssueDate = new DateTime(2026, 7, 2),
            BuyerName = "테스트거래처2",
            TotalAmount = 5000m,
            FilePath = @"C:\temp\a.xlsx",
            CreatedAt = DateTime.Now,
            FileBytes = null,
        });

        var record = repo.Query(new DateTime(2026, 7, 2), new DateTime(2026, 7, 2)).Single();
        Assert.IsNull(repo.GetFileBytes(record.Id));
    }

    [TestMethod]
    public void Query_DoesNotPopulateFileBytes()
    {
        // 목록 조회는 큰 BLOB을 매번 읽지 않아야 한다(성능) — 실제로 열 때만 GetFileBytes로 조회.
        var repo = new DocHistoryRepository();

        repo.Add(new DocHistoryRecord
        {
            DocType = "견적서(기본)",
            IssueDate = new DateTime(2026, 7, 3),
            BuyerName = "테스트거래처3",
            TotalAmount = 1000m,
            FilePath = @"C:\temp\b.xlsx",
            CreatedAt = DateTime.Now,
            FileBytes = [9, 9, 9],
        });

        var record = repo.Query(new DateTime(2026, 7, 3), new DateTime(2026, 7, 3)).Single();
        Assert.IsNull(record.FileBytes);
    }
}

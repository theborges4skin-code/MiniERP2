using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class PriceQuoteRepositoryTests
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

    private static PriceQuote NewQuote(string channelCode = "CH001", string quoteNo = "Q26072001") => new()
    {
        QuoteNo = quoteNo,
        ChannelCode = channelCode,
        PriceKind = "Supply",
        QuoteFormType = "UnitOnly",
        Title = "테스트 견적",
        QuoteDate = new DateTime(2026, 7, 20),
        EffectiveFrom = new DateTime(2026, 7, 20),
        Status = "Draft",
    };

    [TestMethod]
    public void GenerateNextQuoteNo_EmptyTable_ReturnsSeq01()
    {
        var repository = new PriceQuoteRepository();
        var quoteNo = repository.GenerateNextQuoteNo(new DateTime(2026, 7, 20));

        Assert.AreEqual("Q26072001", quoteNo);
    }

    [TestMethod]
    public void GenerateNextQuoteNo_AfterExistingQuoteSameDay_IncrementsSeq()
    {
        var repository = new PriceQuoteRepository();
        repository.SaveQuote(NewQuote(quoteNo: "Q26072001"), []);

        var next = repository.GenerateNextQuoteNo(new DateTime(2026, 7, 20));

        Assert.AreEqual("Q26072002", next);
    }

    [TestMethod]
    public void SaveQuote_NewQuote_AssignsIdAndPersistsHeaderAndLines()
    {
        var repository = new PriceQuoteRepository();
        var quote = NewQuote();
        var lines = new List<PriceQuoteLine>
        {
            new() { CskuCode = "CSKU-1", Msku = "MSKU-1", ItemNameSnap = "품목A", NewPrice = 1000m },
            new() { CskuCode = "CSKU-2", Msku = "MSKU-2", ItemNameSnap = "품목B", NewPrice = 2000m },
        };

        var id = repository.SaveQuote(quote, lines);

        Assert.IsTrue(id > 0);
        Assert.AreEqual(id, quote.Id);

        var (saved, savedLines) = repository.GetQuote(id);
        Assert.IsNotNull(saved);
        Assert.AreEqual("Q26072001", saved.QuoteNo);
        Assert.AreEqual("CH001", saved.ChannelCode);
        Assert.AreEqual("테스트 견적", saved.Title);
        Assert.AreEqual(new DateTime(2026, 7, 20), saved.EffectiveFrom);
        Assert.HasCount(2, savedLines);
        Assert.AreEqual(1, savedLines[0].RowNo);
        Assert.AreEqual("CSKU-1", savedLines[0].CskuCode);
        Assert.AreEqual(2, savedLines[1].RowNo);
    }

    [TestMethod]
    public void SaveQuote_ExistingQuote_UpdatesHeaderAndReplacesLines()
    {
        var repository = new PriceQuoteRepository();
        var quote = NewQuote();
        var id = repository.SaveQuote(quote, [new() { CskuCode = "CSKU-1", Msku = "MSKU-1", NewPrice = 1000m }]);

        quote.Title = "수정된 제목";
        quote.Status = "Sent";
        repository.SaveQuote(quote, [new() { CskuCode = "CSKU-9", Msku = "MSKU-9", NewPrice = 9999m }]);

        var (saved, savedLines) = repository.GetQuote(id);
        Assert.IsNotNull(saved);
        Assert.AreEqual("수정된 제목", saved.Title);
        Assert.AreEqual("Sent", saved.Status);
        Assert.HasCount(1, savedLines);
        Assert.AreEqual("CSKU-9", savedLines[0].CskuCode);
    }

    [TestMethod]
    public void GetAll_WithLatestOnlyFilter_ExcludesSupersededQuotes()
    {
        var repository = new PriceQuoteRepository();
        var original = NewQuote(quoteNo: "Q26072001");
        var originalId = repository.SaveQuote(original, []);

        var revision = NewQuote(quoteNo: "Q26072002");
        revision.RootQuoteId = originalId;
        revision.RevisionNo = 1;
        var revisionId = repository.SaveQuote(revision, []);

        original.SupersededBy = revisionId;
        repository.SaveQuote(original, []);

        var latestOnly = repository.GetAll(channelCode: "CH001", latestOnly: true);

        Assert.HasCount(1, latestOnly);
        Assert.AreEqual(revisionId, latestOnly[0].Id);
    }

    [TestMethod]
    public void Delete_RemovesHeaderAndLines()
    {
        var repository = new PriceQuoteRepository();
        var id = repository.SaveQuote(NewQuote(), [new() { CskuCode = "CSKU-1", Msku = "MSKU-1", NewPrice = 1000m }]);

        repository.Delete(id);

        var (saved, savedLines) = repository.GetQuote(id);
        Assert.IsNull(saved);
        Assert.IsEmpty(savedLines);
    }

    [TestMethod]
    public void HasOutboundHistory_NoMatchingOutbound_ReturnsFalse()
    {
        var repository = new PriceQuoteRepository();

        var hasHistory = repository.HasOutboundHistory("CH001", "CSKU-1", "MSKU-1", new DateTime(2026, 7, 1), null);

        Assert.IsFalse(hasHistory);
    }

    [TestMethod]
    public void HasOutboundHistory_ShippedWithinEffectivePeriod_ReturnsTrue()
    {
        var outboundRepository = new OutboundRepository();
        outboundRepository.SaveOutbound(new[]
        {
            new MiniERP2.Models.OutboundDetail
            {
                ChannelCode = "CH001", OrderNo = "ORDER-QUOTE-1", TrackingNo = "T001",
                MskuCode = "CSKU-1", Qty = 1, SupplyPrice = 1000m,
            },
        });

        var repository = new PriceQuoteRepository();
        // OutboundDetail.CskuCode는 아직 아무도 채우지 않으므로(§9 Step 8 미착수) 이 시점의 모든
        // 출고 이력은 G9 fallback 경로(MskuCode 비교)를 탄다. 그리고 MskuCode는 이름과 달리 이미
        // CSKU 코드를 저장하므로(OutboundDetail.cs 주석 참고), fallback 인자에도 CSKU 코드를 넘긴다.
        var hasHistory = repository.HasOutboundHistory("CH001", "CSKU-1", "CSKU-1", DateTime.Today.AddDays(-1), null);

        Assert.IsTrue(hasHistory);
    }

    [TestMethod]
    public void HasOutboundHistory_ShippedBeforeEffectiveFrom_ReturnsFalse()
    {
        var outboundRepository = new OutboundRepository();
        outboundRepository.SaveOutbound(new[]
        {
            new MiniERP2.Models.OutboundDetail
            {
                ChannelCode = "CH001", OrderNo = "ORDER-QUOTE-2", TrackingNo = "T002",
                MskuCode = "CSKU-1", Qty = 1, SupplyPrice = 1000m,
            },
        });

        var repository = new PriceQuoteRepository();
        // 적용일이 미래(내일부터)라 오늘 출고확정된 이 건은 아직 이 견적 적용기간 밖이다.
        var hasHistory = repository.HasOutboundHistory("CH001", "CSKU-1", "CSKU-1", DateTime.Today.AddDays(1), null);

        Assert.IsFalse(hasHistory);
    }
}

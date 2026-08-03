using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class ChannelSkuRepositoryTests
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
    public void Upsert_ThenGet_ReturnsSavedItem()
    {
        var repository = new ChannelSkuRepository();
        var csku = new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-001", Msku = "MSKU-001", SupplyPrice = 5000m };
        repository.Upsert(csku);

        var saved = repository.GetByChannelAndCskuCode("COUPANG", "CSKU-001");

        Assert.IsNotNull(saved);
        Assert.AreEqual("COUPANG", saved.ChannelCode);
        Assert.AreEqual("CSKU-001", saved.CskuCode);
        Assert.AreEqual("MSKU-001", saved.Msku);
        Assert.AreEqual(5000m, saved.SupplyPrice);
    }

    [TestMethod]
    public void Upsert_WithChangedPrice_RecordsPriceHistory()
    {
        var repository = new ChannelSkuRepository();
        var beforeChange = DateTime.Now;
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-002", Msku = "MSKU-002", SupplyPrice = 5000m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-002", Msku = "MSKU-002", SupplyPrice = 5500m });
        var afterChange = DateTime.Now;

        var history = repository.GetPriceHistory("COUPANG", "CSKU-002");
        var saved = repository.GetByChannelAndCskuCode("COUPANG", "CSKU-002");

        Assert.HasCount(1, history);
        Assert.AreEqual(5000m, history[0].OldPrice);
        Assert.AreEqual(5500m, history[0].NewPrice);
        Assert.IsTrue(history[0].ChangedAt >= beforeChange && history[0].ChangedAt <= afterChange);
        Assert.AreEqual(5500m, saved!.SupplyPrice);
    }

    [TestMethod]
    public void Upsert_WithReason_RecordsReasonOnPriceHistory()
    {
        // B2B 가격조정 공문(§M6)의 "납품가 반영" 버튼이 의존하는 경로 — 문서 제목을 사유로 남긴다.
        var repository = new ChannelSkuRepository();
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-REASON", Msku = "MSKU-REASON", SupplyPrice = 1000m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-REASON", Msku = "MSKU-REASON", SupplyPrice = 1200m }, priceChangeReason: "2026년 3분기 단가 조정에 관한 건");

        var history = repository.GetPriceHistory("COUPANG", "CSKU-REASON");

        Assert.HasCount(1, history);
        Assert.AreEqual("2026년 3분기 단가 조정에 관한 건", history[0].Reason);
    }

    [TestMethod]
    public void Upsert_WithInvoiceDisplayName_PersistsAndCanBeUpdated()
    {
        var repository = new ChannelSkuRepository();
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-003", Msku = "MSKU-003", SupplyPrice = 1000m, InvoiceDisplayName = "샴푸 500ml" });

        var saved = repository.GetByChannelAndCskuCode("COUPANG", "CSKU-003");
        Assert.AreEqual("샴푸 500ml", saved!.InvoiceDisplayName);

        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-003", Msku = "MSKU-003", SupplyPrice = 1000m, InvoiceDisplayName = "샴푸 500ml(수정)" });
        var updated = repository.GetByChannelAndCskuCode("COUPANG", "CSKU-003");
        Assert.AreEqual("샴푸 500ml(수정)", updated!.InvoiceDisplayName);
    }

    [TestMethod]
    public void Upsert_MultipleCskuCodesForSameMsku_CoexistInSameChannel()
    {
        // 채널 안에서 같은 마스터SKU라도 옵션별로 CSKU 코드가 다를 수 있다(옵션1/2/3 분화).
        var repository = new ChannelSkuRepository();
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CPG_PRODA_1", Msku = "PRODA", SupplyPrice = 1000m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CPG_PRODA_2", Msku = "PRODA", SupplyPrice = 1200m });

        var option1 = repository.GetByChannelAndCskuCode("COUPANG", "CPG_PRODA_1");
        var option2 = repository.GetByChannelAndCskuCode("COUPANG", "CPG_PRODA_2");

        Assert.IsNotNull(option1);
        Assert.IsNotNull(option2);
        Assert.AreEqual("PRODA", option1!.Msku);
        Assert.AreEqual("PRODA", option2!.Msku);
        Assert.AreEqual(1000m, option1.SupplyPrice);
        Assert.AreEqual(1200m, option2.SupplyPrice);
    }

    [TestMethod]
    public void ResolveMasterSku_WithExistingCsku_ReturnsLinkedMsku()
    {
        var repository = new ChannelSkuRepository();
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CPG_PRODA_1", Msku = "PRODA", SupplyPrice = 1000m });

        var resolved = repository.ResolveMasterSku("COUPANG", "CPG_PRODA_1");

        Assert.AreEqual("PRODA", resolved);
    }

    [TestMethod]
    public void ResolveMasterSku_WithNoMatchingCsku_FallsBackToInputCode()
    {
        // CSKU로 등록되지 않은(과거 방식의 단순 1:1 규칙) 코드는 그대로 마스터SKU로 간주한다.
        var repository = new ChannelSkuRepository();

        var resolved = repository.ResolveMasterSku("COUPANG", "PRODA");

        Assert.AreEqual("PRODA", resolved);
    }

    [TestMethod]
    public void GetAllByChannel_ReturnsOnlyThatChannelsCskus()
    {
        var repository = new ChannelSkuRepository();
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-A", Msku = "MSKU-A", SupplyPrice = 1000m, InvoiceDisplayName = "A" });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-B", Msku = "MSKU-B", SupplyPrice = 2000m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "11ST", CskuCode = "CSKU-A", Msku = "MSKU-A", SupplyPrice = 1500m });

        var results = repository.GetAllByChannel("COUPANG");

        Assert.HasCount(2, results);
        Assert.IsTrue(results.All(c => c.ChannelCode == "COUPANG"));
        Assert.AreEqual("A", results.Single(c => c.CskuCode == "CSKU-A").InvoiceDisplayName);
    }

    [TestMethod]
    public void GetAllByMsku_ReturnsCorrectItems()
    {
        var repository = new ChannelSkuRepository();
        var targetMsku = "MSKU-TARGET-01";

        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-T1", Msku = targetMsku, SupplyPrice = 1000m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "11ST", CskuCode = "CSKU-T2", Msku = targetMsku, SupplyPrice = 1100m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "NAVER", CskuCode = "CSKU-T3", Msku = "MSKU-OTHER-02", SupplyPrice = 2000m });

        var results = repository.GetAllByMsku(targetMsku);

        Assert.HasCount(2, results);
        Assert.IsTrue(results.All(c => c.Msku == targetMsku));
        Assert.IsNotNull(results.Find(c => c.ChannelCode == "COUPANG"));
    }

    [TestMethod]
    public void Upsert_WithoutCostPriceOverride_DefaultsToNullMeaningMasterLinked()
    {
        var repository = new ChannelSkuRepository();
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-OVR-01", Msku = "MSKU-OVR-01", SupplyPrice = 1000m });

        var saved = repository.GetByChannelAndCskuCode("COUPANG", "CSKU-OVR-01");

        Assert.IsNotNull(saved);
        Assert.IsNull(saved!.CostPriceOverride);
    }

    [TestMethod]
    public void Upsert_WithCostPriceOverride_PersistsIncludingZero()
    {
        var repository = new ChannelSkuRepository();
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-OVR-02", Msku = "MSKU-OVR-02", SupplyPrice = 1000m, CostPriceOverride = 12345.5m });
        var savedWithValue = repository.GetByChannelAndCskuCode("COUPANG", "CSKU-OVR-02");
        Assert.AreEqual(12345.5m, savedWithValue!.CostPriceOverride);

        // 0은 "개별관리 상태에서 원가 0원"이라는 명시적 값이지 NULL(연동)이 아니다(§4.1) — 0도 그대로 저장되어야 한다.
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-OVR-02", Msku = "MSKU-OVR-02", SupplyPrice = 1000m, CostPriceOverride = 0m });
        var savedWithZero = repository.GetByChannelAndCskuCode("COUPANG", "CSKU-OVR-02");
        Assert.AreEqual(0m, savedWithZero!.CostPriceOverride);
    }

    [TestMethod]
    public void Upsert_TogglingCostPriceOverride_RecordsFieldHistoryWithMasterLinkedPlaceholder()
    {
        // §4.3: NULL을 그냥 넘기면 RecordFieldChange가 빈 문자열로 정규화해 "연동 상태"와 "빈 값"이
        // 구분되지 않으므로, "(마스터 연동)" 문구로 명시적으로 기록되어야 한다.
        var repository = new ChannelSkuRepository();
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-OVR-03", Msku = "MSKU-OVR-03", SupplyPrice = 1000m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-OVR-03", Msku = "MSKU-OVR-03", SupplyPrice = 1000m, CostPriceOverride = 500m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-OVR-03", Msku = "MSKU-OVR-03", SupplyPrice = 1000m, CostPriceOverride = null });

        var history = repository.GetFieldHistory("COUPANG", "CSKU-OVR-03")
            .Where(h => h.FieldName == "제조원가(개별관리)").ToList();

        Assert.HasCount(2, history);
        Assert.AreEqual("(마스터 연동)", history[0].OldValue);
        Assert.AreEqual("500", history[0].NewValue);
        Assert.AreEqual("500", history[1].OldValue);
        Assert.AreEqual("(마스터 연동)", history[1].NewValue);
    }

    [TestMethod]
    public void Upsert_UnchangedCostPriceOverride_DoesNotDuplicateFieldHistory()
    {
        var repository = new ChannelSkuRepository();
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-OVR-04", Msku = "MSKU-OVR-04", SupplyPrice = 1000m, CostPriceOverride = 500m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-OVR-04", Msku = "MSKU-OVR-04", SupplyPrice = 1000m, CostPriceOverride = 500m });

        var history = repository.GetFieldHistory("COUPANG", "CSKU-OVR-04").Where(h => h.FieldName == "제조원가(개별관리)");

        Assert.IsEmpty(history);
    }

    [TestMethod]
    public void GetAllByMsku_IncludesCostPriceOverride()
    {
        var repository = new ChannelSkuRepository();
        repository.Upsert(new ChannelSkuModel { ChannelCode = "COUPANG", CskuCode = "CSKU-OVR-05", Msku = "MSKU-OVR-05", SupplyPrice = 1000m, CostPriceOverride = 777m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = "11ST", CskuCode = "CSKU-OVR-06", Msku = "MSKU-OVR-05", SupplyPrice = 1100m });

        var results = repository.GetAllByMsku("MSKU-OVR-05");

        Assert.AreEqual(777m, results.Single(c => c.ChannelCode == "COUPANG").CostPriceOverride);
        Assert.IsNull(results.Single(c => c.ChannelCode == "11ST").CostPriceOverride);
    }

    [TestMethod]
    public void Delete_RemovesChannelSkuAndHistory()
    {
        var repository = new ChannelSkuRepository();
        var cskuCode = "CSKU-DEL-01";
        var channelCode = "COUPANG";

        // Create data to delete and its history
        repository.Upsert(new ChannelSkuModel { ChannelCode = channelCode, CskuCode = cskuCode, Msku = "MSKU-DEL-01", SupplyPrice = 1000m });
        repository.Upsert(new ChannelSkuModel { ChannelCode = channelCode, CskuCode = cskuCode, Msku = "MSKU-DEL-01", SupplyPrice = 1100m });
        // Create data to keep
        repository.Upsert(new ChannelSkuModel { ChannelCode = "11ST", CskuCode = cskuCode, Msku = "MSKU-DEL-01", SupplyPrice = 1200m });

        // Act
        repository.Delete(channelCode, cskuCode);

        // Assert
        var deletedItem = repository.GetByChannelAndCskuCode(channelCode, cskuCode);
        var deletedHistory = repository.GetPriceHistory(channelCode, cskuCode);
        Assert.IsNull(deletedItem, "Deleted item should not be found.");
        Assert.IsEmpty(deletedHistory, "History of deleted item should be empty.");
        Assert.IsNotNull(repository.GetByChannelAndCskuCode("11ST", cskuCode), "Other channel's item should not be deleted.");
    }
}

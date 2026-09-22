using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class ItemRepositoryTests
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
    public void Upsert_ThenGetBySku_ReturnsSavedItem()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "SKU-001", ItemName = "테스트상품", CostPrice = 1000m });

        var saved = repository.GetBySku("SKU-001");

        Assert.IsNotNull(saved);
        Assert.AreEqual("테스트상품", saved.ItemName);
        Assert.AreEqual(1000m, saved.CostPrice);
    }

    [TestMethod]
    public void Upsert_WithChangedCost_RecordsCostHistory()
    {
        var repository = new ItemRepository();
        var beforeChange = DateTime.Now;
        repository.Upsert(new ItemModel { Sku = "SKU-002", ItemName = "원가변경상품", CostPrice = 1000m });
        repository.Upsert(new ItemModel { Sku = "SKU-002", ItemName = "원가변경상품", CostPrice = 1200m });
        var afterChange = DateTime.Now;

        var history = repository.GetCostHistory("SKU-002");
        var saved = repository.GetBySku("SKU-002");

        Assert.HasCount(1, history);
        Assert.AreEqual(1000m, history[0].OldCost);
        Assert.AreEqual(1200m, history[0].NewCost);
        Assert.IsTrue(history[0].ChangedAt >= beforeChange && history[0].ChangedAt <= afterChange);
        Assert.AreEqual(1200m, saved!.CostPrice);
    }

    [TestMethod]
    public void Upsert_WithReserveFields_RoundTrips()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "SKU-RES", ItemName = "예비필드상품", CostPrice = 500m, Reserve1 = "규격A", Reserve2 = "제조사B", Reserve3 = null });

        var saved = repository.GetBySku("SKU-RES");

        Assert.IsNotNull(saved);
        Assert.AreEqual("규격A", saved.Reserve1);
        Assert.AreEqual("제조사B", saved.Reserve2);
        Assert.IsNull(saved.Reserve3);
    }

    [TestMethod]
    public void GetAll_ReturnsAllSavedItems()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "SKU-A", ItemName = "A", CostPrice = 100m });
        repository.Upsert(new ItemModel { Sku = "SKU-B", ItemName = "B", CostPrice = 200m });

        var all = repository.GetAll();

        Assert.HasCount(2, all);
    }

    [TestMethod]
    public void Delete_RemovesItemAndHistory()
    {
        var repository = new ItemRepository();
        var skuToDelete = "SKU-DEL-001";

        // 삭제할 데이터와 이력 생성
        repository.Upsert(new ItemModel { Sku = skuToDelete, ItemName = "삭제될 상품", CostPrice = 100m });
        repository.Upsert(new ItemModel { Sku = skuToDelete, ItemName = "삭제될 상품", CostPrice = 110m });
        repository.Upsert(new ItemModel { Sku = "SKU-KEEP-001", ItemName = "유지될 상품", CostPrice = 200m });

        repository.Delete(skuToDelete);

        var deletedItem = repository.GetBySku(skuToDelete);
        var deletedHistory = repository.GetCostHistory(skuToDelete);
        Assert.IsNull(deletedItem, "삭제된 아이템이 조회되면 안 됩니다.");
        Assert.IsEmpty(deletedHistory, "삭제된 아이템의 이력이 남아있으면 안 됩니다.");
        Assert.IsNotNull(repository.GetBySku("SKU-KEEP-001"), "다른 아이템이 삭제되면 안 됩니다.");
    }

    [TestMethod]
    public void Rename_UpdatesItemAndCostHistory()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "OLD-SKU", ItemName = "이름변경상품", CostPrice = 1000m });
        repository.Upsert(new ItemModel { Sku = "OLD-SKU", ItemName = "이름변경상품", CostPrice = 1200m }); // 원가이력 1건 생성

        var (success, _) = repository.Rename("OLD-SKU", "NEW-SKU");

        Assert.IsTrue(success);
        Assert.IsNull(repository.GetBySku("OLD-SKU"));
        Assert.IsNotNull(repository.GetBySku("NEW-SKU"));
        Assert.IsEmpty(repository.GetCostHistory("OLD-SKU"));
        Assert.HasCount(1, repository.GetCostHistory("NEW-SKU"));
    }

    [TestMethod]
    public void Rename_CascadesToChannelSkuAndPurchaseSku()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "OLD-SKU", ItemName = "상품", CostPrice = 1000m });
        new ChannelSkuRepository().Upsert(new ChannelSkuModel { ChannelCode = "CH1", CskuCode = "CH1_OLD-SKU", Msku = "OLD-SKU", SupplyPrice = 5000m });
        new PurchaseSkuRepository().Upsert(new PurchaseSkuModel { ChannelCode = "VEND1", Msku = "OLD-SKU", PurchasePrice = 800m });

        repository.Rename("OLD-SKU", "NEW-SKU");

        var csku = new ChannelSkuRepository().GetAll().Single(c => c.CskuCode == "CH1_OLD-SKU");
        Assert.AreEqual("NEW-SKU", csku.Msku);
        var purchaseSku = new PurchaseSkuRepository().GetAll().Single(p => p.ChannelCode == "VEND1");
        Assert.AreEqual("NEW-SKU", purchaseSku.Msku);
    }

    [TestMethod]
    public void Rename_CascadesToRuleConditionTargetMskuButNotTargetSku()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "OLD-SKU", ItemName = "상품", CostPrice = 1000m });

        using (var connection = SqliteConnectionFactory.OpenConnection())
        {
            using var command = connection.CreateCommand();
            // TargetSku는 CSKU 코드 전용 칸이라, 값이 우연히 옛 마스터SKU와 같아도 바뀌면 안 된다.
            command.CommandText = "INSERT INTO RuleCondition (ChannelCode, Key, TargetSku, TargetMsku) VALUES ('CH1', 'K1', 'OLD-SKU', 'OLD-SKU')";
            command.ExecuteNonQuery();
        }

        repository.Rename("OLD-SKU", "NEW-SKU");

        using var verifyConnection = SqliteConnectionFactory.OpenConnection();
        using var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = "SELECT TargetSku, TargetMsku FROM RuleCondition WHERE ChannelCode = 'CH1' AND Key = 'K1'";
        using var reader = verifyCommand.ExecuteReader();
        Assert.IsTrue(reader.Read());
        Assert.AreEqual("OLD-SKU", reader.GetString(0), "TargetSku는 CSKU 코드 칸이라 바뀌면 안 됩니다.");
        Assert.AreEqual("NEW-SKU", reader.GetString(1));
    }

    [TestMethod]
    public void Rename_UpdatesUnconfirmedClosingLineButLeavesConfirmedOneAsSnapshot()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "OLD-SKU", ItemName = "상품", CostPrice = 1000m });

        using (var connection = SqliteConnectionFactory.OpenConnection())
        {
            using var insertClosing = connection.CreateCommand();
            insertClosing.CommandText = """
                INSERT INTO PartnerClosingTable (Period, PartyKey, ConfirmedAt) VALUES ('2026-08', 'PARTY-UNCONFIRMED', NULL);
                INSERT INTO PartnerClosingTable (Period, PartyKey, ConfirmedAt) VALUES ('2026-08', 'PARTY-CONFIRMED', '2026-08-31');
                """;
            insertClosing.ExecuteNonQuery();

            using var getIds = connection.CreateCommand();
            getIds.CommandText = "SELECT Id, PartyKey FROM PartnerClosingTable";
            using var reader = getIds.ExecuteReader();
            var ids = new Dictionary<string, long>();
            while (reader.Read()) ids[reader.GetString(1)] = reader.GetInt64(0);

            using var insertLines = connection.CreateCommand();
            insertLines.CommandText = $"""
                INSERT INTO PartnerClosingLineTable (ClosingId, MasterSku) VALUES ({ids["PARTY-UNCONFIRMED"]}, 'OLD-SKU');
                INSERT INTO PartnerClosingLineTable (ClosingId, MasterSku) VALUES ({ids["PARTY-CONFIRMED"]}, 'OLD-SKU');
                """;
            insertLines.ExecuteNonQuery();
        }

        repository.Rename("OLD-SKU", "NEW-SKU");

        using var verifyConnection = SqliteConnectionFactory.OpenConnection();
        using var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = """
            SELECT c.PartyKey, l.MasterSku FROM PartnerClosingLineTable l
            JOIN PartnerClosingTable c ON c.Id = l.ClosingId
            """;
        using var verifyReader = verifyCommand.ExecuteReader();
        var results = new Dictionary<string, string>();
        while (verifyReader.Read()) results[verifyReader.GetString(0)] = verifyReader.GetString(1);

        Assert.AreEqual("NEW-SKU", results["PARTY-UNCONFIRMED"], "미확정 마감 라인은 새 SKU로 갱신되어야 합니다.");
        Assert.AreEqual("OLD-SKU", results["PARTY-CONFIRMED"], "확정된 마감 라인은 발행 시점 스냅샷이라 바뀌면 안 됩니다.");
    }

    [TestMethod]
    public void Rename_FailsWhenNewSkuAlreadyExists()
    {
        var repository = new ItemRepository();
        repository.Upsert(new ItemModel { Sku = "OLD-SKU", ItemName = "상품", CostPrice = 1000m });
        repository.Upsert(new ItemModel { Sku = "TAKEN-SKU", ItemName = "다른상품", CostPrice = 500m });

        var (success, message) = repository.Rename("OLD-SKU", "TAKEN-SKU");

        Assert.IsFalse(success);
        Assert.Contains("이미 사용 중", message);
        Assert.IsNotNull(repository.GetBySku("OLD-SKU"));
    }

    [TestMethod]
    public void Rename_FailsWhenOldSkuDoesNotExist()
    {
        var repository = new ItemRepository();

        var (success, message) = repository.Rename("NO-SUCH-SKU", "NEW-SKU");

        Assert.IsFalse(success);
        Assert.Contains("찾을 수 없습니다", message);
    }
}

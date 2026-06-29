using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.DataManagement;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

/// <summary>
/// 데이터 관리창의 핵심 로직(테이블 어댑터의 Insert/Update/Delete + 변경 적용 라우팅)을 검증한다.
/// WinForms UI(DataManagementForm)는 직접 테스트하기 어려우므로, 그 아래에서 실제로 DB를 건드리는
/// 부분만 떼어내 검증한다.
/// </summary>
[TestClass]
public class ManagedDataTableTests
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
    public void MasterSku_ApplyAddedRow_InsertsNewItem()
    {
        var adapter = new MasterSkuManagedTable();
        var table = adapter.LoadCurrent();

        table.Rows.Add("SKU-NEW", "신규상품", 1000m, "그룹A", null, null, null);

        var result = ManagedTableChangeApplier.Apply(adapter, table);

        Assert.AreEqual(1, result.Inserted);
        var saved = new ItemRepository().GetBySku("SKU-NEW");
        Assert.IsNotNull(saved);
        Assert.AreEqual("신규상품", saved.ItemName);
    }

    [TestMethod]
    public void MasterSku_ApplyModifiedRow_UpdatesExistingItem()
    {
        new ItemRepository().Upsert(new ItemModel { Sku = "SKU-1", ItemName = "기존상품", CostPrice = 100m });
        var adapter = new MasterSkuManagedTable();
        var table = adapter.LoadCurrent();

        table.Rows.Find("SKU-1")!["CostPrice"] = 500m;

        var result = ManagedTableChangeApplier.Apply(adapter, table);

        Assert.AreEqual(1, result.Updated);
        Assert.AreEqual(500m, new ItemRepository().GetBySku("SKU-1")!.CostPrice);
    }

    [TestMethod]
    public void MasterSku_ApplyDeletedRow_RemovesItem()
    {
        new ItemRepository().Upsert(new ItemModel { Sku = "SKU-1", ItemName = "삭제대상", CostPrice = 100m });
        var adapter = new MasterSkuManagedTable();
        var table = adapter.LoadCurrent();

        table.Rows.Find("SKU-1")!.Delete();

        var result = ManagedTableChangeApplier.Apply(adapter, table);

        Assert.AreEqual(1, result.Deleted);
        Assert.IsNull(new ItemRepository().GetBySku("SKU-1"));
    }

    [TestMethod]
    public void MasterSku_RenamingKeyColumn_DeletesOldAndInsertsNew()
    {
        // 키 컬럼(Sku) 값 자체를 그리드에서 바꾸면, 단순 Update로는 매칭이 안 되므로
        // 옛 키 삭제 + 새 키 삽입으로 자동 분리되어야 한다.
        new ItemRepository().Upsert(new ItemModel { Sku = "SKU-OLD", ItemName = "이름변경전", CostPrice = 100m });
        var adapter = new MasterSkuManagedTable();
        var table = adapter.LoadCurrent();

        table.Rows.Find("SKU-OLD")!["Sku"] = "SKU-NEW-NAME";

        var result = ManagedTableChangeApplier.Apply(adapter, table);

        Assert.AreEqual(1, result.Inserted);
        Assert.AreEqual(1, result.Deleted);
        Assert.IsNull(new ItemRepository().GetBySku("SKU-OLD"));
        Assert.IsNotNull(new ItemRepository().GetBySku("SKU-NEW-NAME"));
    }

    [TestMethod]
    public void Csku_ApplyAddedRow_InsertsNewCsku()
    {
        var adapter = new CskuManagedTable();
        var table = adapter.LoadCurrent();

        table.Rows.Add("CH-A", "CSKU-1", "MASTER-1", 1000m, "표시명");

        ManagedTableChangeApplier.Apply(adapter, table);

        var saved = new ChannelSkuRepository().GetByChannelAndCskuCode("CH-A", "CSKU-1");
        Assert.IsNotNull(saved);
        Assert.AreEqual("MASTER-1", saved.Msku);
    }

    [TestMethod]
    public void SimpleMapping_ApplyAddedAndDeletedRows_RoundTripsCorrectly()
    {
        var adapter = new SimpleMappingManagedTable(MappingRuleType.Exact, "1:1 매핑");
        var table = adapter.LoadCurrent();
        table.Rows.Add("CH-A", "상품A옵션1", "SKU-1");

        ManagedTableChangeApplier.Apply(adapter, table);

        var rules = new MappingRepository().GetAllRules(MappingRuleType.Exact);
        Assert.HasCount(1, rules);
        Assert.AreEqual("SKU-1", rules[0].TargetSku);

        // 다시 불러와서 삭제
        var reloaded = adapter.LoadCurrent();
        reloaded.Rows.Find(new object[] { "CH-A", "상품A옵션1" })!.Delete();
        var deleteResult = ManagedTableChangeApplier.Apply(adapter, reloaded);

        Assert.AreEqual(1, deleteResult.Deleted);
        Assert.HasCount(0, new MappingRepository().GetAllRules(MappingRuleType.Exact));
    }

    [TestMethod]
    public void ConditionalMapping_RoundTripsConditionsThroughSerializedText()
    {
        var repository = new MappingRepository();
        var detailId = repository.AddConditionRuleWithDetails("CH-A", "셔츠+블루", "SKU-1",
        [
            new MappingConditionDetail { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "셔츠", Logic = ConditionLogic.And },
            new MappingConditionDetail { HeaderField = StdField.OptionName, Operator = ConditionOperator.Contains, TargetValue = "블루", Logic = ConditionLogic.And },
        ]);
        Assert.IsTrue(detailId > 0);

        var adapter = new ConditionalMappingManagedTable();
        var table = adapter.LoadCurrent();

        Assert.HasCount(1, table.Rows);
        var conditionText = (string)table.Rows[0]["Condition"];
        Assert.Contains("ProductName", conditionText);
        Assert.Contains("셔츠", conditionText);

        // 조건 텍스트를 고쳐서 다시 저장(상세조건이 바뀜) — 1건만 남도록.
        table.Rows[0]["Condition"] = "AND ProductName Contains \"새조건\"";
        var result = ManagedTableChangeApplier.Apply(adapter, table);
        Assert.AreEqual(1, result.Updated);

        var updated = repository.GetAllConditionRulesWithDetails().Single();
        Assert.HasCount(1, updated.Details);
        Assert.AreEqual("새조건", updated.Details[0].TargetValue);
    }

    /// <summary>
    /// 회귀 테스트 — 같은 채널에 같은 Key(사람이 적는 설명 문구일 뿐 DB에 유니크 제약이 없음)를
    /// 가진 조건부 매핑 규칙이 2건 있으면, 예전엔 (ChannelCode, Key)를 DataTable의 PrimaryKey로
    /// 잘못 가정해서 LoadCurrent()가 System.Data.ConstraintException을 던져 데이터관리창을 여는
    /// 순간 프로그램 전체가 죽었다. 이제 DB의 실제 기본키인 Id를 쓰므로 죽지 않아야 한다.
    /// </summary>
    [TestMethod]
    public void ConditionalMapping_DuplicateChannelAndKey_LoadCurrentDoesNotThrow()
    {
        var repository = new MappingRepository();
        repository.AddConditionRuleWithDetails("CH-A", "같은설명", "SKU-1",
        [
            new MappingConditionDetail { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "상품A", Logic = ConditionLogic.And },
        ]);
        repository.AddConditionRuleWithDetails("CH-A", "같은설명", "SKU-2",
        [
            new MappingConditionDetail { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = "상품B", Logic = ConditionLogic.And },
        ]);

        var adapter = new ConditionalMappingManagedTable();
        var table = adapter.LoadCurrent();

        Assert.HasCount(2, table.Rows);

        // 두 행을 Id로 정확히 구분해 각각 수정할 수 있어야 한다(예전엔 Key로만 찾아 첫 번째
        // 행만 계속 고치는 버그가 있었음).
        table.Rows[1]["TargetSku"] = "SKU-2-수정됨";
        var result = ManagedTableChangeApplier.Apply(adapter, table);

        Assert.AreEqual(1, result.Updated);
        var rules = repository.GetAllConditionRulesWithDetails();
        Assert.HasCount(2, rules);
        Assert.IsTrue(rules.Any(r => r.Rule.TargetSku == "SKU-1"));
        Assert.IsTrue(rules.Any(r => r.Rule.TargetSku == "SKU-2-수정됨"));
    }
}

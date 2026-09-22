using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class MasterCostUpdatePlannerTests
{
    private static Dictionary<string, ItemModel> Master(params ItemModel[] items) =>
        items.ToDictionary(i => i.Sku, StringComparer.OrdinalIgnoreCase);

    [TestMethod]
    public void Build_SameCost_ExcludedFromChangedAndCountedAsUnchanged()
    {
        var master = Master(new ItemModel { Sku = "SKU-1", ItemName = "상품1", CostPrice = 1000m });
        var imported = new[] { new MasterCostUpdateImportRow("SKU-1", "상품1", 1000m) };

        var plan = MasterCostUpdatePlanner.Build(imported, master);

        Assert.IsEmpty(plan.Changed);
        Assert.AreEqual(1, plan.UnchangedCount);
        Assert.AreEqual(0, plan.NotFoundCount);
    }

    [TestMethod]
    public void Build_DifferentCost_IncludedInChanged()
    {
        var master = Master(new ItemModel { Sku = "SKU-1", ItemName = "상품1", CostPrice = 1000m });
        var imported = new[] { new MasterCostUpdateImportRow("SKU-1", "상품1", 1200m) };

        var plan = MasterCostUpdatePlanner.Build(imported, master);

        Assert.HasCount(1, plan.Changed);
        Assert.AreEqual(1000m, plan.Changed[0].OldCost);
        Assert.AreEqual(1200m, plan.Changed[0].NewCost);
        Assert.AreEqual(200m, plan.Changed[0].Diff);
    }

    [TestMethod]
    public void Build_WithinFloatTolerance_TreatedAsUnchanged()
    {
        // ItemTable.CostPrice는 REAL(부동소수)로 저장되므로 저장 왕복 과정의 미세한 오차는
        // "동일"로 취급해야 한다(오탐 방지).
        var master = Master(new ItemModel { Sku = "SKU-1", ItemName = "상품1", CostPrice = 1000.00001m });
        var imported = new[] { new MasterCostUpdateImportRow("SKU-1", "상품1", 1000.00002m) };

        var plan = MasterCostUpdatePlanner.Build(imported, master);

        Assert.IsEmpty(plan.Changed);
        Assert.AreEqual(1, plan.UnchangedCount);
    }

    [TestMethod]
    public void Build_SkuNotInMaster_ExcludedAndCounted()
    {
        var master = Master(new ItemModel { Sku = "SKU-1", ItemName = "상품1", CostPrice = 1000m });
        var imported = new[] { new MasterCostUpdateImportRow("SKU-UNKNOWN", "미등록상품", 500m) };

        var plan = MasterCostUpdatePlanner.Build(imported, master);

        Assert.IsEmpty(plan.Changed);
        Assert.AreEqual(1, plan.NotFoundCount);
    }

    [TestMethod]
    public void Build_DuplicateSkuInImportFile_OnlyFirstValueUsed()
    {
        var master = Master(new ItemModel { Sku = "SKU-1", ItemName = "상품1", CostPrice = 1000m });
        var imported = new[]
        {
            new MasterCostUpdateImportRow("SKU-1", "상품1", 1200m),
            new MasterCostUpdateImportRow("SKU-1", "상품1", 1500m),
        };

        var plan = MasterCostUpdatePlanner.Build(imported, master);

        Assert.HasCount(1, plan.Changed);
        Assert.AreEqual(1200m, plan.Changed[0].NewCost);
        Assert.AreEqual(1, plan.DuplicateSkuCount);
    }

    [TestMethod]
    public void Build_MissingItemNameInImport_FallsBackToMasterItemName()
    {
        var master = Master(new ItemModel { Sku = "SKU-1", ItemName = "마스터상품명", CostPrice = 1000m });
        var imported = new[] { new MasterCostUpdateImportRow("SKU-1", "", 1200m) };

        var plan = MasterCostUpdatePlanner.Build(imported, master);

        Assert.AreEqual("마스터상품명", plan.Changed[0].ItemName);
    }

    [TestMethod]
    public void Build_BlankSku_Skipped()
    {
        var master = Master(new ItemModel { Sku = "SKU-1", ItemName = "상품1", CostPrice = 1000m });
        var imported = new[] { new MasterCostUpdateImportRow("   ", "무시됨", 1200m) };

        var plan = MasterCostUpdatePlanner.Build(imported, master);

        Assert.IsEmpty(plan.Changed);
        Assert.AreEqual(0, plan.UnchangedCount);
        Assert.AreEqual(0, plan.NotFoundCount);
    }
}

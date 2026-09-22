using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class ChannelCskuBulkUpdatePlannerTests
{
    private static Dictionary<string, ChannelSkuModel> Existing(params ChannelSkuModel[] items) =>
        items.ToDictionary(c => c.CskuCode, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, ItemModel> Master(params string[] skus) =>
        skus.ToDictionary(s => s, s => new ItemModel { Sku = s, ItemName = s, CostPrice = 0m }, StringComparer.OrdinalIgnoreCase);

    private static ChannelSkuModel Csku(string cskuCode, string msku, decimal supplyPrice, string? invoiceDisplayName = null, string unit = "kg", string? packing = null, string? note = null) =>
        new()
        {
            ChannelCode = "COUPANG",
            CskuCode = cskuCode,
            Msku = msku,
            SupplyPrice = supplyPrice,
            InvoiceDisplayName = invoiceDisplayName,
            Unit = unit,
            Packing = packing,
            Note = note,
        };

    // 변경 전=변경 후 코드로 값만 고치는 표준 케이스(이름변경 없음)를 짧게 만들기 위한 헬퍼.
    private static ChannelCskuImportRow Row(string cskuCode, string msku, decimal supplyPrice, string? invoiceDisplayName = null, string unit = "kg", string? packing = null, string? note = null) =>
        new(cskuCode, cskuCode, msku, invoiceDisplayName, supplyPrice, unit, packing, note);

    [TestMethod]
    public void Build_AllFieldsSame_ExcludedFromChangedAndCountedAsUnchanged()
    {
        var existing = Existing(Csku("C1", "M1", 1000m, "표시명", "kg", "20kg", "메모"));
        var imported = new[] { Row("C1", "M1", 1000m, "표시명", "kg", "20kg", "메모") };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, existing, Master("M1"));

        Assert.IsEmpty(plan.Changed);
        Assert.AreEqual(1, plan.UnchangedCount);
    }

    [TestMethod]
    public void Build_SupplyPriceDifferent_IncludedInChanged()
    {
        var existing = Existing(Csku("C1", "M1", 1000m));
        var imported = new[] { Row("C1", "M1", 1500m) };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, existing, Master("M1"));

        Assert.HasCount(1, plan.Changed);
        Assert.IsTrue(plan.Changed[0].SupplyPriceChanged);
        Assert.IsFalse(plan.Changed[0].MskuChanged);
        Assert.IsFalse(plan.Changed[0].CodeChanged);
    }

    [TestMethod]
    public void Build_OriginalCskuCodeNotInCurrentChannel_ExcludedAndCountedAsNotFound()
    {
        var existing = Existing(Csku("C1", "M1", 1000m));
        var imported = new[] { Row("C-UNKNOWN", "M1", 1000m) };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, existing, Master("M1"));

        Assert.IsEmpty(plan.Changed);
        Assert.AreEqual(1, plan.NotFoundCount);
    }

    [TestMethod]
    public void Build_MskuNotRegisteredInMaster_ExcludedAndCountedAsInvalid()
    {
        var existing = Existing(Csku("C1", "M1", 1000m));
        var imported = new[] { Row("C1", "M-GHOST", 1200m) };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, existing, Master("M1"));

        Assert.IsEmpty(plan.Changed);
        Assert.AreEqual(1, plan.InvalidMskuCount);
    }

    [TestMethod]
    public void Build_BlankMskuInFile_ExcludedAndCountedAsInvalid()
    {
        var existing = Existing(Csku("C1", "M1", 1000m));
        var imported = new[] { Row("C1", "   ", 1200m) };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, existing, Master("M1"));

        Assert.IsEmpty(plan.Changed);
        Assert.AreEqual(1, plan.InvalidMskuCount);
    }

    [TestMethod]
    public void Build_DuplicateOriginalCskuCodeInFile_OnlyFirstValueUsed()
    {
        var existing = Existing(Csku("C1", "M1", 1000m));
        var imported = new[]
        {
            Row("C1", "M1", 1200m),
            Row("C1", "M1", 1500m),
        };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, existing, Master("M1"));

        Assert.HasCount(1, plan.Changed);
        Assert.AreEqual(1200m, plan.Changed[0].NewSupplyPrice);
        Assert.AreEqual(1, plan.DuplicateCskuCount);
    }

    [TestMethod]
    public void Build_BlankOriginalAndNewCskuCode_Skipped()
    {
        var existing = Existing(Csku("C1", "M1", 1000m));
        var imported = new[] { new ChannelCskuImportRow("   ", "   ", "M1", null, 1200m, "kg", null, null) };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, existing, Master("M1"));

        Assert.IsEmpty(plan.Changed);
        Assert.AreEqual(0, plan.NotFoundCount);
        Assert.AreEqual(0, plan.UnchangedCount);
    }

    [TestMethod]
    public void Build_BlankUnitInFile_DefaultsToKg()
    {
        var existing = Existing(Csku("C1", "M1", 1000m, unit: "개"));
        var imported = new[] { Row("C1", "M1", 1000m, unit: "") };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, existing, Master("M1"));

        Assert.HasCount(1, plan.Changed);
        Assert.AreEqual("kg", plan.Changed[0].NewUnit);
        Assert.IsTrue(plan.Changed[0].UnitChanged);
    }

    [TestMethod]
    public void ToUpdatedModel_PreservesCostPriceOverrideFromExisting()
    {
        var existing = Csku("C1", "M1", 1000m);
        existing.CostPriceOverride = 777m;
        var imported = new[] { Row("C1", "M1", 1500m) };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, Existing(existing), Master("M1"));
        var updated = plan.Changed[0].ToUpdatedModel();

        Assert.AreEqual(777m, updated.CostPriceOverride);
        Assert.AreEqual(1500m, updated.SupplyPrice);
    }

    [TestMethod]
    public void Build_NewCskuCodeDifferentFromOriginal_DetectedAsCodeChanged()
    {
        var existing = Existing(Csku("OLD1", "M1", 1000m));
        var imported = new[] { new ChannelCskuImportRow("OLD1", "NEW1", "M1", null, 1000m, "kg", null, null) };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, existing, Master("M1"));

        Assert.HasCount(1, plan.Changed);
        Assert.IsTrue(plan.Changed[0].CodeChanged);
        Assert.AreEqual("NEW1", plan.Changed[0].NewCskuCode);
        Assert.AreEqual("NEW1", plan.Changed[0].ToUpdatedModel().CskuCode);
    }

    [TestMethod]
    public void Build_NewCskuCodeAlreadyUsedByDifferentExistingCsku_ExcludedAsCodeConflict()
    {
        var existing = Existing(Csku("OLD1", "M1", 1000m), Csku("TAKEN", "M2", 500m));
        var imported = new[] { new ChannelCskuImportRow("OLD1", "TAKEN", "M1", null, 1000m, "kg", null, null) };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, existing, Master("M1", "M2"));

        Assert.IsEmpty(plan.Changed);
        Assert.AreEqual(1, plan.CodeConflictCount);
    }

    [TestMethod]
    public void Build_TwoRowsRenamedToSameNewCode_SecondExcludedAsCodeConflict()
    {
        var existing = Existing(Csku("OLD1", "M1", 1000m), Csku("OLD2", "M1", 1000m));
        var imported = new[]
        {
            new ChannelCskuImportRow("OLD1", "DUPTARGET", "M1", null, 1000m, "kg", null, null),
            new ChannelCskuImportRow("OLD2", "DUPTARGET", "M1", null, 1000m, "kg", null, null),
        };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, existing, Master("M1"));

        Assert.HasCount(1, plan.Changed);
        Assert.AreEqual("OLD1", plan.Changed[0].Existing.CskuCode);
        Assert.AreEqual(1, plan.CodeConflictCount);
    }

    [TestMethod]
    public void Build_BlankNewCskuCode_FallsBackToOriginalCode()
    {
        var existing = Existing(Csku("C1", "M1", 1000m));
        var imported = new[] { new ChannelCskuImportRow("C1", "   ", "M1", null, 1500m, "kg", null, null) };

        var plan = ChannelCskuBulkUpdatePlanner.Build(imported, existing, Master("M1"));

        Assert.HasCount(1, plan.Changed);
        Assert.IsFalse(plan.Changed[0].CodeChanged);
        Assert.AreEqual("C1", plan.Changed[0].NewCskuCode);
    }
}

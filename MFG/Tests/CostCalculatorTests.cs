using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Tests;

[TestClass]
public class CostCalculatorTests
{
    private static Material Mat(long id, double priceExcl, double? density, bool taxFree = false) => new()
    {
        Id = id, Name = "m" + id, Density = density, IsTaxFree = taxFree,
        CurrentPrice = new MaterialPrice { PricePerKg = Vat.FromExclusive(priceExcl, taxFree), EffectiveDate = "2026-10-20" },
    };

    /// <summary>신제조법 H14 핸드워시를 그대로 옮긴 배합 — 엑셀 결과(kg당 287.7, 300ml 1149.3963, 500ml 1334.9939)와 같아야 한다.</summary>
    private static (Recipe, Dictionary<long, Material>) Handwash()
    {
        var mats = new[]
        {
            Mat(1, 0, 1), Mat(2, 1900, null), Mat(3, 2700, null), Mat(4, 2800, 1.26), Mat(5, 23000, 0.95),
            Mat(6, 2500, null), Mat(7, 37000, null), Mat(8, 30000, null),
        }.ToDictionary(m => m.Id);
        var recipe = new Recipe
        {
            MeasuredDensity = 1.001,
            Lines =
            [
                new() { MaterialId = 1, Percent = 0.8813 }, new() { MaterialId = 2, Percent = 0.105 }, new() { MaterialId = 3, Percent = 0.002 },
                new() { MaterialId = 4, Percent = 0.004 }, new() { MaterialId = 5, Percent = 0.0002 }, new() { MaterialId = 6, Percent = 0.006 },
                new() { MaterialId = 7, Percent = 0.001 }, new() { MaterialId = 8, Percent = 0.0005 },
            ],
            Sizes =
            [
                new() { VolumeMl = 300, Costs = [new() { Category = SizeCostCategory.Container, ManualAmount = Vat.FromExclusive(863) }, new() { Category = SizeCostCategory.Label, ManualAmount = Vat.FromExclusive(200) }] },
                new() { VolumeMl = 500, SortOrder = 1, Costs = [new() { Category = SizeCostCategory.Container, ManualAmount = Vat.FromExclusive(991) }, new() { Category = SizeCostCategory.Label, ManualAmount = Vat.FromExclusive(200) }] },
            ],
        };
        return (recipe, mats);
    }

    [TestMethod]
    public void Handwash_MatchesExcel_InExclusiveView()
    {
        var (recipe, mats) = Handwash();
        var calc = new CostCalculator(id => mats.GetValueOrDefault(id), _ => null);
        var r = calc.CalculateRecipe(recipe, showIncluded: false);

        Assert.IsTrue(r.TotalIsOk);
        Assert.AreEqual(287.7, r.KgCost, 1e-9);
        Assert.AreEqual(1149.3963, r.Sizes[0].UnitCost, 1e-4);
        Assert.AreEqual(1334.9939, r.Sizes[1].UnitCost, 1e-4);
    }

    [TestMethod]
    public void IncludedView_IsExclusiveTimes1_1()
    {
        var (recipe, mats) = Handwash();
        var calc = new CostCalculator(id => mats.GetValueOrDefault(id), _ => null);
        var excl = calc.CalculateRecipe(recipe, false);
        var incl = calc.CalculateRecipe(recipe, true);
        Assert.AreEqual(excl.KgCost * 1.1, incl.KgCost, 1e-9);
        Assert.AreEqual(excl.Sizes[0].UnitCost * 1.1, incl.Sizes[0].UnitCost, 1e-9);
    }

    [TestMethod]
    public void TaxFreeMaterial_SameInBothViews()
    {
        var mats = new[] { Mat(1, 1000, 1, taxFree: true) }.ToDictionary(m => m.Id);
        var calc = new CostCalculator(id => mats.GetValueOrDefault(id), _ => null);
        var recipe = new Recipe { Lines = [new() { MaterialId = 1, Percent = 1 }] };
        Assert.AreEqual(1000, calc.CalculateRecipe(recipe, true).KgCost, 1e-9);
        Assert.AreEqual(1000, calc.CalculateRecipe(recipe, false).KgCost, 1e-9);
    }

    [TestMethod]
    public void CalculatedDensity_IsHarmonicOverMaterialsWithDensity()
    {
        // 엑셀 공식: ROUND(Σ투입(비중있음) / Σ(투입/비중), 3). 비중 없는 원료는 제외.
        var mats = new[] { Mat(1, 0, 1.0), Mat(2, 0, 0.79), Mat(3, 0, null) }.ToDictionary(m => m.Id);
        var calc = new CostCalculator(id => mats.GetValueOrDefault(id), _ => null);
        var recipe = new Recipe { Lines = [new() { MaterialId = 1, Percent = 0.5 }, new() { MaterialId = 2, Percent = 0.4 }, new() { MaterialId = 3, Percent = 0.1 }] };
        var r = calc.CalculateRecipe(recipe, false);
        Assert.AreEqual(Math.Round(0.9 / (0.5 / 1.0 + 0.4 / 0.79), 3), r.CalculatedDensity, 1e-12);
        Assert.AreEqual(r.CalculatedDensity, r.AppliedDensity);
        Assert.AreEqual(1, r.MissingDensityCount);
    }

    [TestMethod]
    public void PackLink_UsesCurrentPackPriceTimesQuantity()
    {
        var pack = new PackMaterial { Id = 9, CurrentPrice = new PackMaterialPrice { UnitPrice = 110 } };
        var calc = new CostCalculator(_ => null, id => id == 9 ? pack : null);
        var cost = new RecipeSizeCost { Category = SizeCostCategory.Cap, PackMaterialId = 9, Quantity = 2, ManualAmount = 999 };
        Assert.AreEqual(220, calc.CostLineAmount(cost, true), 1e-9);
        Assert.AreEqual(200, calc.CostLineAmount(cost, false), 1e-9);
    }

    [TestMethod]
    public void MaterialPortion_MlUsesDensity_GDoesNot()
    {
        var mats = new[] { Mat(1, 3300, 0.96) }.ToDictionary(m => m.Id);
        var calc = new CostCalculator(id => mats.GetValueOrDefault(id), _ => null);
        Assert.AreEqual(3300 * 0.5 * 0.96, calc.MaterialPortionCost(1, 500, "ml", false)!.Value, 1e-9);
        Assert.AreEqual(3300 * 0.5, calc.MaterialPortionCost(1, 500, "g", false)!.Value, 1e-9);
    }

    [TestMethod]
    public void Bundle_SumsComponentTotals_AndDetectsCycle()
    {
        var single = new FinishedProduct { Id = 1, SourceType = ProductSourceType.Manual, ManualRaw = 1100, ShippingCost = 110 };
        var bundle = new FinishedProduct { Id = 2, SourceType = ProductSourceType.Bundle, Components = [new() { ComponentProductId = 1, Quantity = 2 }], EtcCost = 55 };
        var products = new Dictionary<long, FinishedProduct> { [1] = single, [2] = bundle };
        var calc = new ProductCostCalculator(_ => null, id => products.GetValueOrDefault(id), new CostCalculator(_ => null, _ => null));

        var r = calc.Calculate(bundle, false);
        Assert.AreEqual(2200, r.Raw, 1e-9);   // (1000+100) × 2
        Assert.AreEqual(2250, r.Total, 1e-9);
        Assert.IsNull(r.Problem);

        single.SourceType = ProductSourceType.Bundle;
        single.Components = [new() { ComponentProductId = 2, Quantity = 1 }];
        Assert.IsNotNull(calc.Calculate(bundle, false).Problem);
    }

    [TestMethod]
    public void CodeGenerator_ContinuesNumberingAndWidth()
    {
        Assert.AreEqual("RM138", CodeGenerator.NextFrom(["RM001", "RM137", "PK005"], "RM", 3));
        Assert.AreEqual("RM001", CodeGenerator.NextFrom([], "RM", 3));
        Assert.AreEqual("C13", CodeGenerator.NextFrom(["C01", "C12", "S03"], "C", 2));
        Assert.AreEqual("H21", CodeGenerator.NextFrom(["H20"], "H", 2));
    }

    [TestMethod]
    public void PickCurrentPrice_PrefersDefaultSupplierThenLatest()
    {
        var prices = new List<MaterialPrice>
        {
            new() { Id = 1, PricePerKg = 100, EffectiveDate = "2026-01-01", SupplierCode = "A" },
            new() { Id = 2, PricePerKg = 200, EffectiveDate = "2026-05-01", SupplierCode = "B" },
            new() { Id = 3, PricePerKg = 300, EffectiveDate = "", SupplierCode = "B" },
        };
        Assert.AreEqual(200, MaterialRepository.PickCurrent(prices, null)!.PricePerKg);
        Assert.AreEqual(100, MaterialRepository.PickCurrent(prices, "A")!.PricePerKg);
        Assert.AreEqual(200, MaterialRepository.PickCurrent(prices, "Z")!.PricePerKg);
    }

    [TestMethod]
    public void GuessSupplierAndCategory()
    {
        var suppliers = new List<Supplier>
        {
            new() { ChannelCode = "CH050", Name = "(주)동방플라스틱" }, new() { ChannelCode = "CH048", Name = "(주)서림" },
            new() { ChannelCode = "CH062", Name = "아크팩" },
        };
        Assert.AreEqual("CH050", Import.SeedImporter.GuessSupplier("동방, 500ml 시럽+뚜껑", suppliers)?.ChannelCode);
        Assert.AreEqual("CH048", Import.SeedImporter.GuessSupplier("박스_100ml 160*120*90 (서림)", suppliers)?.ChannelCode);
        Assert.AreEqual("CH062", Import.SeedImporter.GuessSupplier("아크, 100ml 뾰족+뚜껑", suppliers)?.ChannelCode);
        Assert.IsNull(Import.SeedImporter.GuessSupplier("대동, 4L 말통", suppliers));

        Assert.AreEqual(SizeCostCategory.Box, Import.SeedImporter.GuessPackCategory("박스_4L1개 220*205*295"));
        Assert.AreEqual(SizeCostCategory.Cap, Import.SeedImporter.GuessPackCategory("대동, 54파이 뚜껑"));
        Assert.AreEqual(SizeCostCategory.Container, Import.SeedImporter.GuessPackCategory("대동, 4L 말통"));
        Assert.AreEqual(SizeCostCategory.Container, Import.SeedImporter.GuessPackCategory("국일, 30ml 스포이드병(원형)"));
    }
}

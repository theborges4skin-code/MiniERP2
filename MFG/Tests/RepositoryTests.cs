using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Tests;

[TestClass]
public class RepositoryTests : TestDb
{
    [TestMethod]
    public void Material_AutoCode_PriceHistory_And_DuplicateCheck()
    {
        var repo = new MaterialRepository();
        var a = new Material { Name = "글리세린", Aliases = "VG\n식물성글리세린", Density = 1.26 };
        repo.Save(a);
        var b = new Material { Name = "에탄올" };
        repo.Save(b);
        Assert.AreEqual("RM001", a.Code);
        Assert.AreEqual("RM002", b.Code);

        repo.AddPrice(new MaterialPrice { MaterialId = a.Id, PricePerKg = 3080, EffectiveDate = "2026-04-30" });
        repo.AddPrice(new MaterialPrice { MaterialId = a.Id, PricePerKg = 3300, EffectiveDate = "2026-10-01" });
        Assert.AreEqual(3300, repo.Get(a.Id)!.CurrentPrice!.PricePerKg);
        Assert.HasCount(2, repo.GetPrices(a.Id));

        Assert.HasCount(1, repo.FindByNameOrAlias(" vg "));
        Assert.HasCount(0, repo.FindByNameOrAlias("글리세린", excludeId: a.Id));
    }

    [TestMethod]
    public void Recipe_Save_KeepsSizeIds_SoProductLinksSurvive()
    {
        var m = new Material { Name = "정제수" };
        new MaterialRepository().Save(m);
        var recipes = new RecipeRepository();
        var recipe = new Recipe { Code = "H01", Name = "테스트", Lines = [new() { MaterialId = m.Id, Percent = 1 }], Sizes = [new() { VolumeMl = 300 }, new() { VolumeMl = 500 }] };
        recipes.Save(recipe);
        var sizeId = recipe.Sizes[0].Id;

        var product = new FinishedProduct { Name = "p", SourceType = ProductSourceType.RecipeSize, RecipeSizeId = sizeId };
        new ProductRepository().Save(product);

        var loaded = recipes.Get(recipe.Id)!;
        loaded.Sizes[0].VolumeMl = 310;
        loaded.Sizes.RemoveAt(1);
        loaded.Sizes.Add(new RecipeSize { VolumeMl = 1000 });
        recipes.Save(loaded);

        var again = recipes.Get(recipe.Id)!;
        Assert.AreEqual(sizeId, again.Sizes[0].Id);
        Assert.AreEqual(310, again.Sizes[0].VolumeMl);
        Assert.HasCount(2, again.Sizes);
        Assert.AreEqual(sizeId, new ProductRepository().GetAll().Single().RecipeSizeId);
    }

    [TestMethod]
    public void Confirm_WritesSnapshot_And_ReconfirmFlagAfterPriceChange()
    {
        var mats = new MaterialRepository();
        var m = new Material { Name = "에탄올", Density = 0.79 };
        mats.Save(m);
        mats.AddPrice(new MaterialPrice { MaterialId = m.Id, PricePerKg = 2145, EffectiveDate = "2026-10-20" });
        var recipes = new RecipeRepository();
        var recipe = new Recipe { Code = "D01", Name = "디퓨저", Lines = [new() { MaterialId = m.Id, Percent = 1 }] };
        recipes.Save(recipe);

        var snap = CostSnapshot.Load();
        var r = snap.Recipes.Single();
        var res = snap.Recipe(r, true);
        recipes.Confirm(r.Id, "확인함", res.KgCost, res.AppliedDensity, snap.BuildConfirmSnapshot(r));

        snap = CostSnapshot.Load();
        Assert.IsTrue(snap.Recipes.Single().IsConfirmed);
        Assert.IsFalse(snap.RecipeNeedsReconfirm(snap.Recipes.Single()));

        mats.AddPrice(new MaterialPrice { MaterialId = m.Id, PricePerKg = 2500, EffectiveDate = "2026-11-01" });
        snap = CostSnapshot.Load();
        Assert.IsTrue(snap.RecipeNeedsReconfirm(snap.Recipes.Single()));
    }

    [TestMethod]
    public void Backup_CreatesFiles_AndKeepsThree()
    {
        new MaterialRepository().Save(new Material { Name = "x" });
        var svc = new MfgBackupService();
        for (var i = 0; i < 5; i++)
        {
            svc.CreateBackup($"t{i}");
            Thread.Sleep(1100); // 파일명이 초 단위 타임스탬프
        }
        Assert.HasCount(3, svc.GetBackups());
        svc.CreateOrUpdateDailyBackup();
        svc.CreateOrUpdateDailyBackup();
        Assert.HasCount(1, svc.GetDailyBackups());
        Assert.HasCount(3, svc.GetBackups());
    }
}

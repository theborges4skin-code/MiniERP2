using MiniERP2.MFG.Config;
using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;
using MiniERP2.MFG.Import;
using MiniERP2.MFG.Integration;

namespace MiniERP2.MFG.Tests;

[TestClass]
public class CodeBorrowTests : TestDb
{
    [TestMethod]
    public void PickStem_UsesOneLiterSkuOrCommonPrefix()
    {
        Assert.AreEqual("cocog", CodeBorrowService.PickStem([("cocog", "코코글루코사이드 1L"), ("cocog18", "코코글루코사이드 18L"), ("cocog50", "코코글루코사이드 50ml")]));
        Assert.AreEqual("vitacp", CodeBorrowService.PickStem([("vitacp100", "비타민C 100g"), ("vitacp1kg", "비타민C 1kg"), ("vitacp500", "비타민C 500g")]));
        Assert.AreEqual("tw80", CodeBorrowService.PickStem([("tw80_100", "트윈80, 100ml"), ("tw80_18", "트윈80, 18L"), ("tw80_4", "트윈80, 4L")]));
        Assert.AreEqual("dp", CodeBorrowService.PickStem([("dp100", "D-판테놀 100ml"), ("dp10amp", "D-판테놀 10ml 앰플"), ("dp50", "D-판테놀 50ml")]));
        Assert.AreEqual("ehg", CodeBorrowService.PickStem([("ehg", "에칠헥실글리세린 1KG")]));
    }

    [TestMethod]
    public void BaseName_StripsSizeChannelAndQualifiers()
    {
        Assert.AreEqual("코코글루코사이드", CodeBorrowService.BaseName("코코글루코사이드 1L"));
        Assert.AreEqual("아데노신", CodeBorrowService.BaseName("아데노신 파우더 100g").Trim());
        Assert.AreEqual("레몬추출물", CodeBorrowService.BaseName("레몬추출물(SB) 1L"));
        Assert.AreEqual("병풀추출물", CodeBorrowService.BaseName("병풀추출물 10ml 앰플"));
    }

    [TestMethod]
    public void Propose_And_Apply_BorrowsAndKeepsUserCodes()
    {
        var repo = new MaterialRepository();
        var a = new Material { Code = "mfg_rm029", Name = "코코글루코사이드" };
        var b = new Material { Code = "mfg_rm002", Name = "글리세린" };
        var c = new Material { Code = "mfg_mycustom", Name = "SLES" };   // 사용자가 직접 고친 코드 → 유지
        var d = new Material { Code = "mfg_rm001", Name = "정제수" };    // master에 없음 → 번호 코드 유지
        foreach (var m in new[] { a, b, c, d }) repo.Save(m);
        var master = new List<(string, string)>
        {
            ("cocog", "코코글루코사이드 1L"), ("cocog18", "코코글루코사이드 18L"), ("cpgcocog", "쿠팡그로스_코코글루코사이드 1L"),
            ("vg", "VG 1L"), ("vg500", "VG 500ml"), ("sles", "SLES 1L"),
        };
        var (borrowed, _) = CodeBorrowService.ApplyBorrowedMaterialCodes(master);
        Assert.AreEqual(2, borrowed);
        var all = repo.GetAll().ToDictionary(m => m.Name, m => m.Code);
        Assert.AreEqual("mfg_cocog", all["코코글루코사이드"]);
        Assert.AreEqual("mfg_vg", all["글리세린"]);
        Assert.AreEqual("mfg_mycustom", all["SLES"]);
        Assert.AreEqual("mfg_rm001", all["정제수"]);
        StringAssert.Contains(repo.GetAll().Single(m => m.Name == "글리세린").Aliases, "VG");
    }

    [TestMethod]
    public void LegacyMigration_PrefixesEverything_Idempotent()
    {
        new MaterialRepository().Save(new Material { Code = "RM005", Name = "x" });
        new PackMaterialRepository().Save(new PackMaterial { Code = "PK012", Name = "y" });
        new RecipeRepository().Save(new Recipe { Code = "H14", Name = "z" });
        Assert.IsTrue(CodeBorrowService.NeedsLegacyMigration());
        CodeBorrowService.MigrateLegacyCodes();
        Assert.IsFalse(CodeBorrowService.NeedsLegacyMigration());
        Assert.AreEqual("mfg_rm005", new MaterialRepository().GetAll().Single().Code);
        Assert.AreEqual("mfg_pk012", new PackMaterialRepository().GetAll().Single().Code);
        Assert.AreEqual("mfg_h14", new RecipeRepository().GetAll().Single().Code);
        Assert.AreEqual((0, 0, 0), CodeBorrowService.MigrateLegacyCodes());
        Assert.AreEqual("H", MfgCodes.RecipeCategory("mfg_h14"));
        Assert.AreEqual("mfg_h15", new RecipeRepository().NextCode("H"));
        Assert.AreEqual("mfg_rm006", new MaterialRepository().Save(new Material { Name = "new" }) > 0 ? new MaterialRepository().GetAll().Single(m => m.Name == "new").Code : "");
    }

    /// <summary>실데이터 미리보기: 원본 엑셀 이관 + 실제 MiniERP2 DB(읽기 전용)로 차용 결과를 파일로 남긴다(검토용).</summary>
    [TestMethod]
    public void RealData_BorrowPreview()
    {
        const string recipe = @"C:\00_CompanyWorks\회사폴더\제조법관련(신규).xlsx";
        var bridge = new MiniErp2Bridge(MfgPaths.DefaultMiniErp2DatabasePath);
        if (!File.Exists(recipe) || !bridge.IsAvailable) Assert.Inconclusive("원본 엑셀 또는 MiniERP2 DB 없음");
        new SeedImporter().Run(recipe, null, []);
        var (borrowed, report) = CodeBorrowService.ApplyBorrowedMaterialCodes(SeedRunner.MasterItems(bridge));
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "mfg_borrow_preview.txt"), report);
        Assert.IsGreaterThan(30, borrowed);
        var codes = new MaterialRepository().GetAll().Select(m => m.Code).ToList();
        Assert.IsTrue(codes.All(MfgCodes.HasPrefix));
        CollectionAssert.AllItemsAreUnique(codes);
    }
}

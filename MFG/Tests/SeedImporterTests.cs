using MiniERP2.MFG.Domain;
using MiniERP2.MFG.Import;

namespace MiniERP2.MFG.Tests;

/// <summary>
/// 실제 원본 엑셀로 초기 이관을 돌려 검증한다. 원본 파일이 없는 PC에서는 건너뛴다(Inconclusive).
/// 경로는 환경변수 MFG_RECIPE_XLSX / MFG_PM_XLSX로 바꿀 수 있다(원본 대신 사본으로 돌릴 때).
/// </summary>
[TestClass]
public class SeedImporterTests : TestDb
{
    private static string RecipePath => Environment.GetEnvironmentVariable("MFG_RECIPE_XLSX") ?? @"C:\00_CompanyWorks\회사폴더\제조법관련(신규).xlsx";
    private static string PmPath => Environment.GetEnvironmentVariable("MFG_PM_XLSX") ?? @"C:\00_CompanyWorks\회사폴더\02_견적,거래명세_보냄\온라인 매출내역\product_master.xlsx";

    [TestMethod]
    public void RealWorkbooks_ImportAndRecipeKgCostsMatchSheets()
    {
        if (!File.Exists(RecipePath) || !File.Exists(PmPath)) Assert.Inconclusive("원본 엑셀이 없는 환경");

        var suppliers = new List<Supplier>
        {
            new() { ChannelCode = "CH050", Name = "(주)동방플라스틱" }, new() { ChannelCode = "CH048", Name = "(주)서림" },
            new() { ChannelCode = "CH052", Name = "(주)청진" }, new() { ChannelCode = "CH056", Name = "(주)다담피앤씨" },
        };
        var importer = new SeedImporter();
        importer.Run(RecipePath, PmPath, suppliers);
        var rep = importer.Report;

        Assert.AreEqual(137, rep.Materials);
        Assert.AreEqual(61, rep.Recipes);
        Assert.IsGreaterThan(500, rep.Products);
        Assert.IsGreaterThan(40, rep.Packs);

        var snap = CostSnapshot.Load();
        var mismatches = new List<string>();
        foreach (var (code, name, sheetKg, id) in rep.RecipeChecks)
        {
            var calc = snap.Recipe(snap.Recipes.Single(r => r.Id == id), showIncluded: false).KgCost;
            if (Math.Abs(calc - sheetKg) > 0.01) mismatches.Add($"{code} {name}: 시트 {sheetKg} / 계산 {calc}");
        }
        // S04는 시트에 원료목록과 다른 단가(30000)가 직접 입력된 행이 있어 다를 수 있다(이관 경고로 보고됨).
        Assert.IsTrue(mismatches.All(m => m.StartsWith("S04", StringComparison.Ordinal)), string.Join("\n", mismatches));

        var h14 = snap.Recipes.Single(r => r.Code == "H14");
        Assert.AreEqual(RecipeStatus.Draft, h14.Status);
        var sizes = snap.Recipe(h14, false).Sizes;
        Assert.AreEqual(1149.3963, sizes.Single(s => s.Size.VolumeMl == 300).UnitCost, 1e-3);

        // PM 수동값 이관: VAT포함 합계 = PM G열. foam300 G = 1264.3359(=1149.396×1.1, PM은 1149 기준이라 1263.9).
        var foam = snap.Products.Values.Single(p => p.Sku == "foam300");
        var total = snap.ProductCalculator(true).Calculate(foam, true).Total;
        Assert.AreEqual(1263.9, total, 0.05);
        StringAssert.Contains(foam.SuggestedLink, "H14");
    }
}

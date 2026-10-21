using MiniERP2.MFG.Data;
using MiniERP2.MFG.DataManagement;
using MiniERP2.MFG.Domain;
using MiniERP2.MFG.Import;
using OfficeOpenXml;

namespace MiniERP2.MFG.Tests;

[TestClass]
public class DataManagementTests : TestDb
{
    private (Material Water, Material Sles, Recipe Recipe) Seed()
    {
        var mats = new MaterialRepository();
        var water = new Material { Name = "정제수" };
        var sles = new Material { Name = "SLES" };
        mats.Save(water);
        mats.Save(sles);
        var recipe = new Recipe { Code = "mfg_h14", Name = "핸드워시", Lines = [new() { MaterialId = water.Id, Percent = 0.9 }, new() { MaterialId = sles.Id, Percent = 0.1 }] };
        new RecipeRepository().Save(recipe);
        return (water, sles, recipe);
    }

    private string ExportAll(ManagedTable adapter)
    {
        var table = adapter.LoadCurrent();
        var path = Path.Combine(Folder, adapter.DisplayName + ".xlsx");
        ManagedTableExcelIO.Export(table, table.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName).ToList(), null, null, path, adapter.InfoColumns);
        return path;
    }

    private static void EditCell(string path, string header, string matchHeader, string matchValue, object? newValue)
    {
        ExcelPackage.License.SetNonCommercialPersonal("MiniERP2");
        using var p = new ExcelPackage(new FileInfo(path));
        var ws = p.Workbook.Worksheets[0];
        int Col(string h) => Enumerable.Range(1, ws.Dimension.End.Column).First(c => ws.Cells[1, c].Text == h);
        for (var r = 2; r <= ws.Dimension.End.Row; r++)
        {
            if (ws.Cells[r, Col(matchHeader)].Text == matchValue) ws.Cells[r, Col(header)].Value = newValue;
        }
        p.Save();
    }

    private static ApplyResult ImportAndSave(ManagedTable adapter, string path, bool overwrite = true)
    {
        var table = adapter.LoadCurrent();
        var (headers, rows) = ManagedTableExcelIO.Read(path);
        var plan = ManagedTableImporter.Analyze(adapter, table, headers, rows);
        Assert.HasCount(0, plan.Errors, string.Join("\n", plan.Errors));
        ManagedTableImporter.Stage(adapter, table, plan, overwrite);
        return ManagedTableChangeApplier.Apply(adapter, table);
    }

    [TestMethod]
    public void RenameMaterialCodeByFile_KeepsRecipeLinks()
    {
        var (_, sles, recipe) = Seed();
        var adapter = new MaterialTable();
        var path = ExportAll(adapter);
        EditCell(path, "코드", "원료명", "SLES", "mfg_sles");

        var result = ImportAndSave(adapter, path);
        Assert.AreEqual(1, result.Updated);
        Assert.AreEqual(0, result.Inserted);
        Assert.AreEqual("mfg_sles", new MaterialRepository().Get(sles.Id)!.Code);
        var lines = new RecipeRepository().Get(recipe.Id)!.Lines;
        Assert.AreEqual(sles.Id, lines.Single(l => Math.Abs(l.Percent - 0.1) < 1e-9).MaterialId);

        // 배합행 표에서도 새 코드로 보인다.
        var lineTable = new RecipeLineTable().LoadCurrent();
        Assert.IsTrue(lineTable.Rows.Cast<System.Data.DataRow>().Any(r => (string)r["원료코드"] == "mfg_sles"));
    }

    [TestMethod]
    public void NewRowsWithoutId_InsertWithAutoCode_AndFallbackMatchByCode()
    {
        Seed();
        var adapter = new MaterialTable();
        var path = Path.Combine(Folder, "new.xlsx");
        ExcelPackage.License.SetNonCommercialPersonal("MiniERP2");
        using (var p = new ExcelPackage())
        {
            var ws = p.Workbook.Worksheets.Add("Sheet1");
            ws.Cells[1, 1].Value = "코드"; ws.Cells[1, 2].Value = "원료명"; ws.Cells[1, 3].Value = "비중"; ws.Cells[1, 4].Value = "사용";
            ws.Cells[2, 1].Value = ""; ws.Cells[2, 2].Value = "에탄올"; ws.Cells[2, 3].Value = 0.79; ws.Cells[2, 4].Value = "Y";
            ws.Cells[3, 1].Value = "mfg_rm001"; ws.Cells[3, 2].Value = "정제수(RO)"; ws.Cells[3, 3].Value = 1; ws.Cells[3, 4].Value = "Y"; // Id 없이 코드로 기존 행 매칭
            p.SaveAs(new FileInfo(path));
        }
        var result = ImportAndSave(adapter, path);
        Assert.AreEqual(1, result.Inserted);
        Assert.AreEqual(1, result.Updated);
        var all = new MaterialRepository().GetAll();
        Assert.AreEqual("mfg_rm003", all.Single(m => m.Name == "에탄올").Code);
        Assert.AreEqual("정제수(RO)", all.Single(m => m.Code == "mfg_rm001").Name);
    }

    [TestMethod]
    public void FailedRow_RollsBackWholeSave()
    {
        Seed();
        var adapter = new MaterialTable();
        var path = ExportAll(adapter);
        EditCell(path, "코드", "원료명", "SLES", "mfg_rm001"); // 정제수와 코드 중복 → UNIQUE 실패
        EditCell(path, "비고", "원료명", "정제수", "바뀌면 안 됨");
        var table = adapter.LoadCurrent();
        var (headers, rows) = ManagedTableExcelIO.Read(path);
        var plan = ManagedTableImporter.Analyze(adapter, table, headers, rows);
        ManagedTableImporter.Stage(adapter, table, plan, overwrite: true);
        var ex = Assert.Throws<InvalidOperationException>(() => ManagedTableChangeApplier.Apply(adapter, table));
        StringAssert.Contains(ex.Message, "중복");
        Assert.AreEqual("", new MaterialRepository().GetAll().Single(m => m.Name == "정제수").Note);
    }

    [TestMethod]
    public void BadNumber_ReportsRowAndStagesNothing()
    {
        Seed();
        var adapter = new MaterialTable();
        var path = ExportAll(adapter);
        EditCell(path, "비중", "원료명", "SLES", "abc");
        var table = adapter.LoadCurrent();
        var (headers, rows) = ManagedTableExcelIO.Read(path);
        var plan = ManagedTableImporter.Analyze(adapter, table, headers, rows);
        Assert.HasCount(1, plan.Errors);
        StringAssert.Contains(plan.Errors[0], "비중");
    }

    [TestMethod]
    public void AllTables_LoadAndRoundTripWithoutChanges_OnRealData()
    {
        const string recipe = @"C:\00_CompanyWorks\회사폴더\제조법관련(신규).xlsx";
        const string pm = @"C:\00_CompanyWorks\회사폴더\02_견적,거래명세_보냄\온라인 매출내역\product_master.xlsx";
        if (!File.Exists(recipe) || !File.Exists(pm)) Assert.Inconclusive("원본 엑셀 없음");
        new SeedImporter().Run(recipe, pm, [new Supplier { ChannelCode = "CH050", Name = "(주)동방플라스틱" }]);
        new SupplierRepository().Sync([("CH050", "(주)동방플라스틱")]);

        foreach (var adapter in MfgTables.All())
        {
            var path = ExportAll(adapter);
            var table = adapter.LoadCurrent();
            var (headers, rows) = ManagedTableExcelIO.Read(path);
            var plan = ManagedTableImporter.Analyze(adapter, table, headers, rows);
            Assert.HasCount(0, plan.Errors, adapter.DisplayName + ": " + string.Join("\n", plan.Errors.Take(5)));
            Assert.HasCount(0, plan.NewRows, adapter.DisplayName + " 내보낸 파일을 그대로 불러오면 신규가 없어야 함");
            var (_, overwritten) = ManagedTableImporter.Stage(adapter, table, plan, overwrite: true);
            Assert.AreEqual(0, overwritten, adapter.DisplayName + " 내보낸 파일을 그대로 불러오면 바뀌는 행이 없어야 함");
        }
    }
}

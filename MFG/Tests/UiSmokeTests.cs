using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;
using MiniERP2.MFG.Forms;
using MiniERP2.MFG.Import;

namespace MiniERP2.MFG.Tests;

/// <summary>
/// 화면 스모크 테스트: 실제 원본 엑셀을 임시 mfgdb로 이관한 뒤 모든 탭·편집기·대화상자를 만들고 갱신해
/// 런타임 예외(널 참조, 레이아웃, 바인딩 오류)가 없는지 확인한다. 원본 엑셀이 없으면 건너뛴다.
/// </summary>
[TestClass]
public class UiSmokeTests : TestDb
{
    private const string RecipePath = @"C:\00_CompanyWorks\회사폴더\제조법관련(신규).xlsx";
    private const string PmPath = @"C:\00_CompanyWorks\회사폴더\02_견적,거래명세_보냄\온라인 매출내역\product_master.xlsx";

    [STATestMethod]
    public void AllTabs_RefreshWithRealData_InBothVatModes()
    {
        if (!File.Exists(RecipePath) || !File.Exists(PmPath)) Assert.Inconclusive("원본 엑셀이 없는 환경");
        new SeedImporter().Run(RecipePath, PmPath, []);

        using var form = new MainForm();
        form.Opacity = 0;
        form.ShowInTaskbar = false;
        form.Show();
        Application.DoEvents();

        var tabs = form.Controls.OfType<TabControl>().Single();
        foreach (var vatIncluded in new[] { true, false })
        {
            foreach (TabPage page in tabs.TabPages)
            {
                tabs.SelectedTab = page;
                Application.DoEvents();
                ((IMfgTab)page.Controls[0]).RefreshData();
                Application.DoEvents();
            }
            form.Controls.OfType<FlowLayoutPanel>().First().Controls.OfType<RadioButton>().First(r => r.Text.StartsWith(vatIncluded ? "VAT별도" : "VAT포함")).Checked = true;
        }
        form.Close();
    }

    [STATestMethod]
    public void RecipeEditor_LoadsEveryRecipe_AndHandwashShowsExcelValue()
    {
        if (!File.Exists(RecipePath)) Assert.Inconclusive("원본 엑셀이 없는 환경");
        new SeedImporter().Run(RecipePath, null, []);
        var state = new AppState { ShowVatIncluded = false };

        using var host = new Form { Opacity = 0, ShowInTaskbar = false, Width = 1400, Height = 900 };
        var editor = new RecipeEditor(state);
        host.Controls.Add(editor);
        host.Show();
        Application.DoEvents();

        foreach (var r in new RecipeRepository().GetAll(withDetails: false))
        {
            editor.LoadRecipe(r.Id);
            Application.DoEvents();
            Assert.IsFalse(editor.IsDirty, $"{r.Code} 불러오기만 했는데 변경 상태가 됨");
        }

        var h14 = new RecipeRepository().GetAll(false).Single(r => r.Code == "H14");
        editor.LoadRecipe(h14.Id);
        var sizes = FindGrid(editor, g => g.Columns.Contains("item"));
        var unitRow = sizes.Rows.Cast<DataGridViewRow>().Single(row => (string)row.Tag! == "unit");
        Assert.AreEqual("1,149.4", unitRow.Cells[1].Value);
        state.ShowVatIncluded = true;   // VAT 전환 시 용량표가 다시 만들어지므로 행을 다시 찾는다
        Application.DoEvents();
        unitRow = sizes.Rows.Cast<DataGridViewRow>().Single(row => (string)row.Tag! == "unit");
        Assert.AreEqual("1,264.34", unitRow.Cells[1].Value);
        host.Close();
    }

    [STATestMethod]
    public void ProductDialog_OpensForEveryKindOfProduct()
    {
        if (!File.Exists(RecipePath) || !File.Exists(PmPath)) Assert.Inconclusive("원본 엑셀이 없는 환경");
        new SeedImporter().Run(RecipePath, PmPath, []);
        var state = new AppState();
        var snap = CostSnapshot.Load();

        // 수동값 그대로 + 제조법 연결(H14 300ml) + 묶음(foam300 ×2) 세 경우.
        var foam = snap.Products.Values.Single(p => p.Sku == "foam300");
        var foam2 = snap.Products.Values.Single(p => p.Sku == "foam300x2");
        var h14 = snap.Recipes.Single(r => r.Code == "H14");
        foam.SourceType = ProductSourceType.RecipeSize;
        foam.RecipeSizeId = h14.Sizes.Single(s => s.VolumeMl == 300).Id;
        new ProductRepository().Save(foam);
        foam2.SourceType = ProductSourceType.Bundle;
        foam2.Components = [new FinishedProductComponent { ComponentProductId = foam.Id, Quantity = 2 }];
        foam2.ManualRaw = 0;
        new ProductRepository().Save(foam2);
        snap = CostSnapshot.Load();

        var calc = snap.ProductCalculator(false);
        Assert.AreEqual(1149.3963, calc.Calculate(snap.Products[foam.Id], false).Total, 1e-3);
        Assert.AreEqual(2 * 1149.3963, calc.Calculate(snap.Products[foam2.Id], false).Total, 1e-3);

        foreach (var p in new[] { snap.Products[foam.Id], snap.Products[foam2.Id], snap.Products.Values.First(p => p.SourceType == ProductSourceType.Manual) })
        {
            using var dlg = new ProductDialog(state, snap, p) { Opacity = 0, ShowInTaskbar = false };
            dlg.Show();
            Application.DoEvents();
            dlg.Close();
        }
    }

    private static DataGridView FindGrid(Control root, Func<DataGridView, bool> predicate)
    {
        foreach (Control c in root.Controls)
        {
            if (c is DataGridView g && predicate(g)) return g;
            try { return FindGrid(c, predicate); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("grid not found");
    }
}

using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;
using MiniERP2.MFG.Import;

namespace MiniERP2.MFG.Forms;

/// <summary>
/// MiniERP2_MFG 메인 창. 탭: 원료 / 부자재 / 매입처 / 제조법 / 완제품 원가 / MiniERP2 전송 / 설정·백업.
/// 상단에 VAT 보기 전환과 미확정 현황을 항상 보여 준다.
/// </summary>
public class MainForm : Form
{
    private readonly AppState _state = new();
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly RadioButton _vatIncl = new() { Text = "VAT포함 보기", AutoSize = true, Margin = new Padding(8, 6, 4, 4) };
    private readonly RadioButton _vatExcl = new() { Text = "VAT별도 보기", AutoSize = true, Margin = new Padding(4, 6, 4, 4) };
    private readonly Label _status = Ui.Label("");
    private readonly HashSet<TabPage> _stale = [];
    private RecipesTab? _recipesTab;

    public MainForm()
    {
        Text = "MiniERP2_MFG — 제조원가관리";
        Width = 1600;
        Height = 950;
        StartPosition = FormStartPosition.CenterScreen;

        _vatIncl.Checked = _state.ShowVatIncluded;
        _vatExcl.Checked = !_state.ShowVatIncluded;
        _vatIncl.CheckedChanged += (_, _) => { if (_vatIncl.Checked) _state.ShowVatIncluded = true; };
        _vatExcl.CheckedChanged += (_, _) => { if (_vatExcl.Checked) _state.ShowVatIncluded = false; };

        var title = Ui.Label("MiniERP2_MFG 제조원가관리", bold: true);
        title.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        var top = Ui.Bar(title, _vatIncl, _vatExcl, Ui.Button("지금 백업", (_, _) => BackupNow()), _status);
        top.BackColor = SystemColors.ControlLight;

        AddTab("원료", new MaterialsTab(_state));
        AddTab("부자재", new PacksTab(_state));
        AddTab("매입처", new SuppliersTab(_state));
        _recipesTab = new RecipesTab(_state);
        AddTab("제조법", _recipesTab);
        AddTab("완제품 원가", new ProductsTab(_state));
        AddTab("MiniERP2 전송", new PushTab(_state));
        AddTab("설정·백업", new SettingsTab(_state));

        Controls.Add(_tabs);
        Controls.Add(top);

        _tabs.SelectedIndexChanged += (_, _) => RefreshSelected();
        _state.DataChanged += () =>
        {
            foreach (TabPage page in _tabs.TabPages) if (page != _tabs.SelectedTab) _stale.Add(page);
            UpdateStatus();
        };
        _state.VatModeChanged += () => { foreach (TabPage page in _tabs.TabPages) if (page != _tabs.SelectedTab) _stale.Add(page); };

        Shown += (_, _) => OnFirstShown();
        FormClosing += OnClosing;
    }

    private void AddTab(string text, UserControl content)
    {
        var page = new TabPage(text);
        page.Controls.Add(content);
        _tabs.TabPages.Add(page);
        _stale.Add(page);
    }

    private void RefreshSelected()
    {
        var page = _tabs.SelectedTab;
        if (page is null || !_stale.Remove(page)) return;
        if (page.Controls.Count > 0 && page.Controls[0] is IMfgTab tab)
        {
            Cursor = Cursors.WaitCursor;
            try { tab.RefreshData(); }
            finally { Cursor = Cursors.Default; }
        }
    }

    private void OnFirstShown()
    {
        if (!SeedImporter.HasData())
        {
            _tabs.SelectedTab = _tabs.TabPages[^1];
            RefreshSelected();
            Ui.Info(this, "mfgdb가 비어 있습니다.\n[설정·백업] 탭의 '초기 데이터 가져오기'로 제조법관련(신규).xlsx와 product_master.xlsx를 가져오세요.\n(원본 엑셀은 읽기만 하며, 모든 제조법은 '미확정'으로 들어옵니다.)");
        }
        else
        {
            _tabs.SelectedTab = _tabs.TabPages[3]; // 제조법
            RefreshSelected();
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        try
        {
            var snap = CostSnapshot.Load();
            var draftRecipes = snap.Recipes.Count(r => !r.IsConfirmed && !r.IsArchived);
            var reRecipes = snap.Recipes.Count(snap.RecipeNeedsReconfirm);
            var inclCalc = snap.ProductCalculator(true);
            var draftProducts = snap.Products.Values.Count(p => !p.IsConfirmed);
            var reProducts = snap.Products.Values.Count(p => snap.ProductNeedsReconfirm(p, inclCalc));
            _status.Text = $"미확정 제조법 {draftRecipes} · 미확정 완제품 {draftProducts}" +
                           (reRecipes + reProducts > 0 ? $" · 재확인 필요 {reRecipes + reProducts}" : "") + $"   |   저장은 VAT포함, 지금 {_state.VatLabel} 보기";
            _status.ForeColor = reRecipes + reProducts > 0 ? Ui.WarnFore : SystemColors.ControlText;
        }
        catch (Exception ex)
        {
            _status.Text = "상태 읽기 실패: " + ex.Message;
        }
    }

    private void BackupNow()
    {
        var path = new MfgBackupService().CreateBackup("manual");
        Ui.Info(this, $"백업했습니다.\n{path}");
        _state.NotifyDataChanged();
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_recipesTab is not null && !_recipesTab.ConfirmLeave()) { e.Cancel = true; return; }
        var backup = new MfgBackupService();
        try
        {
            if (File.Exists(Config.MfgPaths.DatabaseFilePath))
            {
                backup.CreateOrUpdateDailyBackup();
                // MiniERP2와 같이 매월 1일 종료 시 월간 전체백업을 묻는다.
                if (DateTime.Today.Day == 1 && backup.NeedsMonthlyBackup() &&
                    Ui.Confirm(this, "매월 1일 전체백업 — 이번 달 mfgdb 전체 백업을 하시겠습니까?\n(일일 자동백업과는 별도 파일로 보관됩니다.)", "월간 전체백업"))
                {
                    backup.CreateMonthlyBackup();
                }
            }
        }
        catch (Exception ex)
        {
            Utils.Log.Write($"[Closing] 백업 실패: {ex.Message}");
        }
    }
}

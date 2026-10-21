using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Forms;

/// <summary>제조법 목록(왼쪽) + 편집기(오른쪽). 미확정/확정/재확인 필요 필터.</summary>
public class RecipesTab : UserControl, IMfgTab
{
    private readonly AppState _state;
    private readonly RecipeRepository _repo = new();
    private readonly DataGridView _grid = Ui.Grid();
    private readonly TextBox _search = Ui.Text(160, input: false);
    private readonly ComboBox _statusFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox _categoryFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly CheckBox _showArchived = new() { Text = "보관 포함", AutoSize = true, Margin = new Padding(6, 8, 4, 4) };
    private readonly RecipeEditor _editor;
    private bool _filling;
    private long? _shownId;

    public RecipesTab(AppState state)
    {
        _state = state;
        Dock = DockStyle.Fill;
        _editor = new RecipeEditor(state);
        _editor.Saved += () => RefreshList();

        _statusFilter.Items.AddRange(["전체 상태", RecipeStatus.Draft, RecipeStatus.Confirmed, "재확인 필요"]);
        _statusFilter.SelectedIndex = 0;
        _categoryFilter.Items.Add("전체 분류");
        foreach (var (_, label) in RecipeRepository.Categories) _categoryFilter.Items.Add(label);
        _categoryFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndexChanged += (_, _) => RefreshList();
        _categoryFilter.SelectedIndexChanged += (_, _) => RefreshList();
        _showArchived.CheckedChanged += (_, _) => RefreshList();
        _search.TextChanged += (_, _) => Ui.ApplyFilter(_grid, _search.Text);

        _grid.MultiSelect = false;
        _grid.Columns.AddRange(Ui.Col("code", "코드", 50), Ui.Col("name", "제품명", 200), Ui.Col("status", "상태", 60),
            Ui.Col("kg", "kg당 원료비", 90, right: true), Ui.Col("sizes", "용량", 120), Ui.Col("flag", "확인", 90), Ui.Col("source", "원본 시트", 160));
        _grid.SelectionChanged += (_, _) => OnSelect();

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_grid);
        left.Controls.Add(Ui.Bar(Ui.Button("새 제조법", (_, _) => NewRecipe()), Ui.Button("새 버전으로 복사", (_, _) => CopyRecipe()), Ui.Button("삭제", (_, _) => DeleteRecipe())));
        left.Controls.Add(Ui.Bar(Ui.Label("검색"), _search, _statusFilter, _categoryFilter, _showArchived));

        var split = new SplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_editor);
        Controls.Add(split);
        Load += (_, _) => split.SplitterDistance = 520;
        _state.VatModeChanged += () => { if (Visible) RefreshList(); };
    }

    public void RefreshData()
    {
        RefreshList();
        if (!_editor.IsDirty) _editor.LoadRecipe(_shownId);
    }

    private void RefreshList()
    {
        var snap = CostSnapshot.Load();
        var show = _state.ShowVatIncluded;
        var status = _statusFilter.SelectedIndex;
        var prefix = _categoryFilter.SelectedIndex > 0 ? RecipeRepository.Categories[_categoryFilter.SelectedIndex - 1].Prefix : null;

        _filling = true;
        _grid.Columns["kg"]!.HeaderText = $"kg당 원료비({_state.VatLabel})";
        _grid.Rows.Clear();
        foreach (var r in snap.Recipes)
        {
            if (!_showArchived.Checked && r.IsArchived) continue;
            if (prefix is not null && MfgCodes.RecipeCategory(r.Code) != prefix) continue;
            var res = snap.Recipe(r, show);
            var reconfirm = snap.RecipeNeedsReconfirm(r);
            if (status == 1 && r.IsConfirmed) continue;
            if (status == 2 && !r.IsConfirmed) continue;
            if (status == 3 && !reconfirm) continue;

            var flags = new List<string>();
            if (reconfirm) flags.Add("재확인 필요");
            if (!res.TotalIsOk) flags.Add("합계≠100%");
            if (res.MissingPriceCount > 0) flags.Add("단가없음");
            var i = _grid.Rows.Add(r.Code, r.Name + (r.Version.Length > 0 ? $" ({r.Version})" : ""), r.Status, Fmt.Won(res.KgCost),
                string.Join(", ", r.Sizes.Select(s => Fmt.Num(s.VolumeMl))), string.Join(" ", flags), r.SourceSheet);
            var row = _grid.Rows[i];
            row.Tag = r.Id;
            if (r.IsConfirmed) Theme.ApplyConfirmed(row.Cells["status"].Style);
            if (flags.Count > 0) row.Cells["flag"].Style.ForeColor = Theme.WarnFore;
            if (r.IsArchived) row.DefaultCellStyle.ForeColor = Theme.MutedFore;
        }
        Ui.ApplyFilter(_grid, _search.Text);
        Ui.Reselect(_grid, _shownId);
        _filling = false;
    }

    private void OnSelect()
    {
        if (_filling) return;
        var id = Ui.SelectedId(_grid);
        if (id == _shownId) return;
        if (_editor.IsDirty)
        {
            var answer = MessageBox.Show(this, "편집 중인 제조법에 저장하지 않은 변경이 있습니다. 저장할까요?", "저장 확인", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !_editor.Save()))
            {
                _filling = true;
                Ui.Reselect(_grid, _shownId);
                _filling = false;
                return;
            }
        }
        _shownId = id;
        _editor.LoadRecipe(id);
    }

    /// <summary>닫기 전 확인용: 편집 중 변경이 있으면 저장 여부를 묻는다. 계속 진행해도 되면 true.</summary>
    /// <summary>코드로 제조법을 골라 편집기에 연다(화면 캡처 확인용).</summary>
    internal void ShowRecipe(string code)
    {
        var r = new RecipeRepository().GetAll(withDetails: false).FirstOrDefault(x => x.Code == code);
        if (r is null) return;
        _shownId = r.Id;
        RefreshList();
        _editor.LoadRecipe(r.Id);
    }

    public bool ConfirmLeave()
    {
        if (!_editor.IsDirty) return true;
        var answer = MessageBox.Show(this, "제조법에 저장하지 않은 변경이 있습니다. 저장할까요?", "저장 확인", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer == DialogResult.No || (answer == DialogResult.Yes && _editor.Save());
    }

    private void NewRecipe()
    {
        if (!ConfirmLeave()) return;
        using var dlg = new Form { Text = "새 제조법", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, Width = 420, Height = 200, MinimizeBox = false, MaximizeBox = false };
        var cat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        foreach (var (_, label) in RecipeRepository.Categories) cat.Items.Add(label);
        cat.SelectedIndex = 0;
        var name = Ui.Text(240);
        dlg.Controls.Add(Ui.Form(("분류(코드 첫 글자)", cat), ("제품명", name)));
        var ok = new Button { Text = "만들기", DialogResult = DialogResult.OK, Width = 90 };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 42 };
        bar.Controls.Add(ok);
        dlg.Controls.Add(bar);
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) != DialogResult.OK || name.Text.Trim().Length == 0) return;

        var prefix = RecipeRepository.Categories[cat.SelectedIndex].Prefix;
        var recipe = new Recipe { Code = _repo.NextCode(prefix), Name = name.Text.Trim(), Status = RecipeStatus.Draft };
        _repo.Save(recipe);
        _shownId = recipe.Id;
        _state.NotifyDataChanged();
        RefreshList();
        _editor.LoadRecipe(recipe.Id);
    }

    private void CopyRecipe()
    {
        if (_shownId is null || !ConfirmLeave()) return;
        var src = _repo.Get(_shownId.Value);
        if (src is null) return;
        var copy = new Recipe
        {
            Code = _repo.NextCode(MfgCodes.RecipeCategory(src.Code)), Name = src.Name, Version = $"{src.Code} 복사({MfgDb.Today()})", SourceSheet = src.SourceSheet,
            Status = RecipeStatus.Draft, MeasuredDensity = src.MeasuredDensity, BatchKg = src.BatchKg, ProcessMemo = src.ProcessMemo,
            Lines = src.Lines.Select(l => new RecipeLine { Stage = l.Stage, MaterialId = l.MaterialId, Percent = l.Percent, Memo = l.Memo, SortOrder = l.SortOrder }).ToList(),
            Sizes = src.Sizes.Select(s => new RecipeSize
            {
                VolumeMl = s.VolumeMl, Memo = s.Memo, SortOrder = s.SortOrder,
                Costs = s.Costs.Select(c => new RecipeSizeCost { Category = c.Category, PackMaterialId = c.PackMaterialId, ManualAmount = c.ManualAmount, Quantity = c.Quantity, Memo = c.Memo }).ToList(),
            }).ToList(),
        };
        _repo.Save(copy);
        _shownId = copy.Id;
        _state.NotifyDataChanged();
        RefreshList();
        _editor.LoadRecipe(copy.Id);
        Ui.Info(this, $"{src.Code}을(를) {copy.Code}(으)로 복사했습니다(미확정). 버전명을 알맞게 고쳐 저장하세요.");
    }

    private void DeleteRecipe()
    {
        if (_shownId is null) return;
        var r = _repo.Get(_shownId.Value);
        if (r is null) return;
        var sizeIds = r.Sizes.Select(s => s.Id).ToHashSet();
        var linked = new ProductRepository().GetAll().Count(p => p.RecipeSizeId is { } sid && sizeIds.Contains(sid));
        if (linked > 0) { Ui.Warn(this, $"이 제조법의 용량에 완제품 {linked}개가 연결되어 있어 삭제할 수 없습니다. 쓰지 않는 제조법은 '보관'으로 숨기세요."); return; }
        if (!Ui.Confirm(this, $"{r.Code} {r.Name}을(를) 삭제할까요? 되돌릴 수 없습니다(쓰지 않는 제조법은 '보관'을 권장).")) return;
        new MfgBackupService().CreateBackup("before_recipe_delete");
        _repo.Delete(r.Id);
        _shownId = null;
        _state.NotifyDataChanged();
        RefreshList();
        _editor.LoadRecipe(null);
    }
}

using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Forms;

/// <summary>
/// 완제품(SKU) 원가표 — product_master를 대체한다. 열 구성은 PM과 같다(원료/용기/패키지/스티커 등/기타/택배비 → 합계).
/// 더블클릭으로 편집(연결 유형·추가 비용), 선택 확정/미확정.
/// </summary>
public class ProductsTab : UserControl, IMfgTab
{
    private readonly AppState _state;
    private readonly ProductRepository _repo = new();
    private readonly DataGridView _grid = Ui.Grid();
    private readonly TextBox _search = Ui.Text(200, input: false);
    private readonly ComboBox _categoryFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ComboBox _statusFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly ComboBox _sourceFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox _onlyHints = new() { Text = "연결 후보 있는 것만", AutoSize = true, Margin = new Padding(6, 8, 4, 4) };
    private readonly Label _count = Ui.Label("");
    private CostSnapshot? _snap;
    private bool _filling;

    public ProductsTab(AppState state)
    {
        _state = state;
        Dock = DockStyle.Fill;
        _statusFilter.Items.AddRange(["전체 상태", RecipeStatus.Draft, RecipeStatus.Confirmed, "재확인 필요", "계산 문제"]);
        _statusFilter.SelectedIndex = 0;
        _sourceFilter.Items.AddRange(["전체 연결", "수동값", "제조법 용량", "원료 소분", "묶음"]);
        _sourceFilter.SelectedIndex = 0;
        foreach (var cb in new[] { _categoryFilter, _statusFilter, _sourceFilter }) cb.SelectedIndexChanged += (_, _) => { if (!_filling) Fill(); };
        _onlyHints.CheckedChanged += (_, _) => Fill();
        _search.TextChanged += (_, _) => { Ui.ApplyFilter(_grid, _search.Text); UpdateCount(); };

        _grid.Columns.AddRange(
            Ui.Col("cat", "구분", 90), Ui.Col("group", "상품그룹", 80), Ui.Col("name", "상품명", 260), Ui.Col("sku", "SKU", 110),
            Ui.Col("source", "연결", 80), Ui.Col("target", "연결 대상", 180),
            Ui.Col("raw", "원료", 80, right: true), Ui.Col("con", "용기", 70, right: true), Ui.Col("pkg", "패키지", 70, right: true),
            Ui.Col("stk", "스티커 등", 70, right: true), Ui.Col("etc", "기타", 70, right: true), Ui.Col("ship", "택배비", 70, right: true),
            Ui.Col("total", "합계", 90, right: true), Ui.Col("consumer", "소비자가", 80, right: true), Ui.Col("margin", "마진율", 60, right: true),
            Ui.Col("status", "상태", 55), Ui.Col("confirmed", "확정원가(VAT포함)", 110, right: true), Ui.Col("flag", "확인", 110),
            Ui.Col("hint", "연결 후보(유추)", 320), Ui.Col("notes", "비고", 260));
        _grid.Columns["total"]!.DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSelected(); };
        _grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; EditSelected(); } };

        Controls.Add(_grid);
        Controls.Add(Ui.Bar(Ui.Button("편집", (_, _) => EditSelected()), Ui.Button("선택 확정", (_, _) => ConfirmSelected()), Ui.Button("선택 미확정", (_, _) => DraftSelected()),
            Ui.Button("신규 품목", (_, _) => NewProduct()), Ui.Button("삭제", (_, _) => DeleteSelected()), _count));
        Controls.Add(Ui.Bar(Ui.Label("검색"), _search, _categoryFilter, _statusFilter, _sourceFilter, _onlyHints));
        _state.VatModeChanged += () => { if (Visible) RefreshData(); };
    }

    public void RefreshData()
    {
        _snap = CostSnapshot.Load();
        _filling = true;
        var keep = _categoryFilter.SelectedItem as string;
        _categoryFilter.Items.Clear();
        _categoryFilter.Items.Add("전체 구분");
        foreach (var c in _snap.Products.Values.Select(p => p.Category).Where(c => c.Length > 0).Distinct().OrderBy(c => c)) _categoryFilter.Items.Add(c);
        _categoryFilter.SelectedItem = keep;
        if (_categoryFilter.SelectedIndex < 0) _categoryFilter.SelectedIndex = 0;
        _filling = false;
        Fill();
    }

    private void Fill()
    {
        if (_snap is null) return;
        var keep = Ui.SelectedId(_grid);
        var show = _state.ShowVatIncluded;
        var calc = _snap.ProductCalculator(show);
        var inclCalc = show ? calc : _snap.ProductCalculator(true);
        var cat = _categoryFilter.SelectedIndex > 0 ? _categoryFilter.SelectedItem as string : null;
        var sourceLabel = _sourceFilter.SelectedIndex > 0 ? _sourceFilter.SelectedItem as string : null;

        _filling = true;
        foreach (var name in new[] { "raw", "total" }) _grid.Columns[name]!.HeaderText = (name == "raw" ? "원료" : "합계") + $"({_state.VatLabel})";
        _grid.Rows.Clear();
        foreach (var p in _snap.Products.Values.OrderBy(p => p.SortOrder).ThenBy(p => p.Id))
        {
            if (cat is not null && p.Category != cat) continue;
            if (sourceLabel is not null && ProductSourceType.Label(p.SourceType) != sourceLabel) continue;
            if (_onlyHints.Checked && p.SuggestedLink.Length == 0) continue;
            var r = calc.Calculate(p, show);
            var reconfirm = _snap.ProductNeedsReconfirm(p, inclCalc);
            switch (_statusFilter.SelectedIndex)
            {
                case 1 when p.IsConfirmed: continue;
                case 2 when !p.IsConfirmed: continue;
                case 3 when !reconfirm: continue;
                case 4 when r.Problem is null: continue;
            }

            var target = p.SourceType switch
            {
                ProductSourceType.RecipeSize when p.RecipeSizeId is { } sid => _snap.SizeLabel(sid),
                ProductSourceType.Material when p.MaterialId is { } mid => _snap.Materials.TryGetValue(mid, out var m) ? $"{m.Name} {Fmt.Num(p.MaterialQty ?? 0)}{p.MaterialQtyUnit}" : "(삭제된 원료)",
                ProductSourceType.Bundle => string.Join(" + ", p.Components.Select(c => (_snap.Products.TryGetValue(c.ComponentProductId, out var cp) ? cp.Sku ?? cp.Name : "?") + $"×{Fmt.Num(c.Quantity)}")),
                _ => "",
            };
            var flags = new List<string>();
            if (reconfirm) flags.Add("재확인 필요");
            if (r.Problem is not null) flags.Add(r.Problem);
            var inclTotal = show ? r.Total : inclCalc.Calculate(p, true).Total;
            var margin = p.ConsumerPrice is > 0 && inclTotal > 0 ? Fmt.Pct(1 - inclTotal / p.ConsumerPrice.Value) : "";

            var i = _grid.Rows.Add(p.Category, p.ProductGroup, p.Name, p.Sku ?? "", ProductSourceType.Label(p.SourceType), target,
                Fmt.Won(r.Raw), Zero(r.Container), Zero(r.Package), Zero(r.Sticker), Zero(r.Etc), Zero(r.Shipping), Fmt.Won(r.Total),
                p.ConsumerPrice is { } c ? Fmt.Won(c) : "", margin, p.Status, Fmt.Won(p.ConfirmedTotal), string.Join(" ", flags),
                p.SuggestedLink, p.Notes.Replace("\n", " / "));
            var row = _grid.Rows[i];
            row.Tag = p.Id;
            if (p.IsConfirmed) row.Cells["status"].Style.BackColor = Ui.ConfirmedBack;
            if (flags.Count > 0) row.Cells["flag"].Style.ForeColor = Ui.WarnFore;
            if (p.SourceType != ProductSourceType.Manual) row.Cells["raw"].Style.ForeColor = Ui.LinkFore;
        }
        Ui.ApplyFilter(_grid, _search.Text);
        Ui.Reselect(_grid, keep);
        _filling = false;
        UpdateCount();
    }

    private static string Zero(double v) => Math.Abs(v) < 0.0001 ? "" : Fmt.Won(v);

    private void UpdateCount()
    {
        var visible = _grid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible);
        var all = _snap?.Products.Count ?? 0;
        var confirmed = _snap?.Products.Values.Count(p => p.IsConfirmed) ?? 0;
        _count.Text = $"표시 {visible} / 전체 {all} (확정 {confirmed}, 미확정 {all - confirmed})";
    }

    private void EditSelected()
    {
        if (_snap is null || Ui.SelectedId(_grid) is not { } id || !_snap.Products.TryGetValue(id, out var p)) return;
        using var dlg = new ProductDialog(_state, _snap, p);
        if (dlg.ShowDialog(this) == DialogResult.OK) RefreshData();
    }

    private void NewProduct()
    {
        if (_snap is null) return;
        var p = new FinishedProduct { SortOrder = _snap.Products.Count == 0 ? 0 : _snap.Products.Values.Max(x => x.SortOrder) + 1 };
        using var dlg = new ProductDialog(_state, _snap, p);
        if (dlg.ShowDialog(this) == DialogResult.OK) RefreshData();
    }

    private void ConfirmSelected()
    {
        if (_snap is null) return;
        var ids = Ui.SelectedIds(_grid).ToList();
        if (ids.Count == 0) return;
        var calc = _snap.ProductCalculator(true);
        var problems = new List<string>();
        var ok = new List<FinishedProduct>();
        foreach (var id in ids)
        {
            var p = _snap.Products[id];
            var r = calc.Calculate(p, true);
            if (r.Problem is not null) problems.Add($"{p}: {r.Problem}");
            else ok.Add(p);
        }
        var msg = $"선택한 {ids.Count}개 중 {ok.Count}개를 지금 계산한 원가로 확정합니다." + (problems.Count > 0 ? $"\n\n계산 문제로 제외({problems.Count}):\n" + string.Join("\n", problems.Take(10)) : "");
        if (ok.Count == 0) { Ui.Warn(this, msg); return; }
        if (!Ui.Confirm(this, msg + "\n\n진행할까요?")) return;
        var now = MfgDb.Now();
        foreach (var p in ok)
        {
            p.Status = RecipeStatus.Confirmed;
            p.ConfirmedTotal = calc.Calculate(p, true).Total;
            p.ConfirmedAt = now;
            _repo.Save(p);
        }
        _state.NotifyDataChanged();
        RefreshData();
    }

    private void DraftSelected()
    {
        if (_snap is null) return;
        var ids = Ui.SelectedIds(_grid).ToList();
        if (ids.Count == 0 || !Ui.Confirm(this, $"선택한 {ids.Count}개를 미확정으로 돌릴까요?")) return;
        foreach (var id in ids)
        {
            var p = _snap.Products[id];
            p.Status = RecipeStatus.Draft;
            _repo.Save(p);
        }
        _state.NotifyDataChanged();
        RefreshData();
    }

    private void DeleteSelected()
    {
        if (_snap is null) return;
        var ids = Ui.SelectedIds(_grid).ToList();
        if (ids.Count == 0) return;
        var usedInBundles = _snap.Products.Values.Where(p => p.Components.Any(c => ids.Contains(c.ComponentProductId)) && !ids.Contains(p.Id)).ToList();
        var msg = $"선택한 {ids.Count}개 품목을 삭제할까요?" + (usedInBundles.Count > 0 ? $"\n\n다음 묶음 상품의 구성에서도 빠집니다:\n{string.Join("\n", usedInBundles.Take(10))}" : "");
        if (!Ui.Confirm(this, msg)) return;
        new MfgBackupService().CreateBackup("before_product_delete");
        foreach (var id in ids) _repo.Delete(id);
        _state.NotifyDataChanged();
        RefreshData();
    }
}

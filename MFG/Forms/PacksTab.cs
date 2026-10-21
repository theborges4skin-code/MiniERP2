using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Forms;

/// <summary>부자재(용기·캡·라벨·박스 등) 관리: 원/개 단가 이력, 매입처, 사용 제조법.</summary>
public class PacksTab : UserControl, IMfgTab
{
    private readonly AppState _state;
    private readonly PackMaterialRepository _repo = new();
    private readonly DataGridView _grid = Ui.Grid();
    private readonly TextBox _search = Ui.Text(220, input: false);
    private readonly ComboBox _categoryFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };

    private readonly Label _code = Ui.Label("-", bold: true);
    private readonly ComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox _name = Ui.Text(300);
    private readonly ComboBox _defaultSupplier = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _taxFree = new() { Text = "면세(VAT 없음)", AutoSize = true };
    private readonly CheckBox _active = new() { Text = "사용", AutoSize = true, Checked = true };
    private readonly TextBox _note = Ui.Text(300, multiline: true);
    private readonly DataGridView _prices = Ui.Grid();
    private readonly Label _usage = Ui.Label("");

    private List<PackMaterial> _packs = [];
    private List<Supplier> _suppliers = [];
    private PackMaterial? _current;
    private bool _filling;

    public PacksTab(AppState state)
    {
        _state = state;
        Dock = DockStyle.Fill;

        _categoryFilter.Items.Add("(전체 분류)");
        _categoryFilter.Items.AddRange(SizeCostCategory.PackCategories);
        _categoryFilter.SelectedIndex = 0;
        _categoryFilter.SelectedIndexChanged += (_, _) => RefreshData();
        _category.Items.AddRange(SizeCostCategory.PackCategories);

        _grid.Columns.AddRange(
            Ui.Col("code", "코드", 70), Ui.Col("cat", "분류", 110), Ui.Col("name", "품명·규격", 280), Ui.Col("price", "현재단가(원/개)", 110, right: true),
            Ui.Col("date", "적용일", 85), Ui.Col("supplier", "매입처", 130), Ui.Col("usage", "사용 제조법", 80, right: true), Ui.Col("note", "비고", 260));
        _grid.SelectionChanged += (_, _) => { if (!_filling) LoadDetail(Ui.SelectedId(_grid)); };
        _search.TextChanged += (_, _) => Ui.ApplyFilter(_grid, _search.Text);

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_grid);
        left.Controls.Add(Ui.Bar(Ui.Label("검색"), _search, _categoryFilter, Ui.Button("신규 부자재", (_, _) => { _grid.CurrentCell = null; LoadDetail(null); _name.Focus(); })));

        _prices.Columns.AddRange(Ui.Col("date", "적용일", 90), Ui.Col("price", "단가(원/개)", 100, right: true), Ui.Col("supplier", "매입처", 130),
            Ui.Col("source", "출처", 120), Ui.Col("note", "메모", 260));

        var form = Ui.Form(("코드", _code), ("분류", _category), ("품명·규격", _name), ("기본 매입처", _defaultSupplier), ("", _taxFree), ("", _active), ("비고", _note));
        var right = new Panel { Dock = DockStyle.Fill };
        var priceBox = new GroupBox { Text = "단가 이력 (맨 위 굵은 글씨 = 현재 단가)", Dock = DockStyle.Fill };
        priceBox.Controls.Add(_prices);
        priceBox.Controls.Add(Ui.Bar(Ui.Button("단가 추가", (_, _) => AddPrice()), Ui.Button("선택 단가 삭제", (_, _) => DeletePrice())));
        right.Controls.Add(priceBox);
        right.Controls.Add(_usage);
        _usage.Dock = DockStyle.Top;
        right.Controls.Add(Ui.Bar(Ui.Button("저장", (_, _) => SaveCurrent())));
        right.Controls.Add(form);

        var split = new SplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        Controls.Add(split);
        Load += (_, _) => split.SplitterDistance = Math.Max(300, Width - 520);
        _state.VatModeChanged += () => { if (Visible) RefreshData(); };
    }

    public void RefreshData()
    {
        var keep = _current?.Id;
        _suppliers = new SupplierRepository().GetAll();
        _packs = _repo.GetAll();
        var usage = _repo.GetUsage();
        var names = _suppliers.ToDictionary(s => s.ChannelCode, s => s.Name);
        _defaultSupplier.Items.Clear();
        _defaultSupplier.Items.Add("(지정 안 함)");
        foreach (var s in _suppliers) _defaultSupplier.Items.Add(s);

        _filling = true;
        _grid.Columns["price"]!.HeaderText = $"현재단가(원/개, {_state.VatLabel})";
        _grid.Rows.Clear();
        var cat = _categoryFilter.SelectedIndex > 0 ? _categoryFilter.SelectedItem as string : null;
        foreach (var p in _packs.Where(p => cat is null || p.Category == cat))
        {
            var cp = p.CurrentPrice;
            var i = _grid.Rows.Add(p.Code, p.Category, p.Name, cp is null ? "(없음)" : Fmt.Won(Vat.ToView(cp.UnitPrice, _state.ShowVatIncluded, p.IsTaxFree)),
                cp?.EffectiveDate ?? "", p.DefaultSupplierCode is { } sc ? names.GetValueOrDefault(sc, sc) : "",
                usage.TryGetValue(p.Id, out var u) ? u.Count : 0, p.Note.Split('\n')[0]);
            _grid.Rows[i].Tag = p.Id;
            if (!p.IsActive) _grid.Rows[i].DefaultCellStyle.ForeColor = Ui.MutedFore;
        }
        Ui.ApplyFilter(_grid, _search.Text);
        Ui.Reselect(_grid, keep);
        _filling = false;
        LoadDetail(Ui.SelectedId(_grid));
    }

    private void LoadDetail(long? id)
    {
        _current = id is null ? null : _packs.FirstOrDefault(p => p.Id == id);
        var p = _current ?? new PackMaterial();
        _code.Text = _current is null ? "(신규 — 저장 시 자동 부여)" : p.Code;
        _category.SelectedItem = p.Category;
        _name.Text = p.Name;
        _taxFree.Checked = p.IsTaxFree;
        _active.Checked = p.IsActive;
        _note.Text = p.Note.Replace("\n", Environment.NewLine);
        _defaultSupplier.SelectedIndex = 0;
        for (var i = 1; i < _defaultSupplier.Items.Count; i++)
        {
            if (((Supplier)_defaultSupplier.Items[i]!).ChannelCode == p.DefaultSupplierCode) _defaultSupplier.SelectedIndex = i;
        }

        _prices.Columns["price"]!.HeaderText = $"단가(원/개, {_state.VatLabel})";
        _prices.Rows.Clear();
        _usage.Text = "";
        if (_current is null) return;
        var names = _suppliers.ToDictionary(s => s.ChannelCode, s => s.Name);
        foreach (var pr in _repo.GetPrices(_current.Id))
        {
            var i = _prices.Rows.Add(pr.EffectiveDate.Length == 0 ? "(날짜 미상)" : pr.EffectiveDate, Fmt.Won(Vat.ToView(pr.UnitPrice, _state.ShowVatIncluded, p.IsTaxFree)),
                pr.SupplierCode is { } sc ? names.GetValueOrDefault(sc, sc) : "", pr.Source, pr.Note.Replace("\n", " / "));
            _prices.Rows[i].Tag = pr.Id;
            if (pr.Id == _current.CurrentPrice?.Id) _prices.Rows[i].DefaultCellStyle.Font = new Font(_prices.Font, FontStyle.Bold);
        }
        var usage = _repo.GetUsage();
        _usage.Text = usage.TryGetValue(_current.Id, out var codes) ? "사용 제조법: " + string.Join(", ", codes) : "사용 제조법: 없음 (제조법 용량표에서 '부자재 목록에서 선택'으로 연결)";
    }

    private void SaveCurrent()
    {
        if (_name.Text.Trim().Length == 0) { Ui.Warn(this, "품명을 입력하세요."); return; }
        var p = _current ?? new PackMaterial();
        p.Category = _category.SelectedItem as string ?? SizeCostCategory.EtcPack;
        p.Name = _name.Text.Trim();
        p.DefaultSupplierCode = _defaultSupplier.SelectedItem is Supplier s ? s.ChannelCode : null;
        p.IsTaxFree = _taxFree.Checked;
        p.IsActive = _active.Checked;
        p.Note = _note.Text.Replace("\r\n", "\n").Trim();
        var isNew = p.Id == 0;
        _repo.Save(p);
        _current = p;
        _state.NotifyDataChanged();
        RefreshData();
        Ui.Reselect(_grid, p.Id);
        if (isNew && Ui.Confirm(this, $"부자재 {p.Code}을(를) 등록했습니다. 지금 단가를 입력할까요?")) AddPrice();
    }

    private void AddPrice()
    {
        if (_current is null) { Ui.Warn(this, "먼저 부자재를 선택하거나 저장하세요."); return; }
        using var dlg = new PriceDialog($"단가 추가 — {_current}", "원/개", _suppliers, _current.DefaultSupplierCode, _state.ShowVatIncluded, _current.IsTaxFree, allowUncertain: false);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _repo.AddPrice(new PackMaterialPrice
        {
            PackMaterialId = _current.Id, SupplierCode = dlg.SupplierCode, UnitPrice = dlg.PriceIncl, EffectiveDate = dlg.EffectiveDate, Source = dlg.Source, Note = dlg.Note,
        });
        _state.NotifyDataChanged();
        RefreshData();
    }

    private void DeletePrice()
    {
        if (_prices.CurrentRow?.Tag is not long id) return;
        if (!Ui.Confirm(this, "선택한 단가 기록을 삭제할까요?")) return;
        _repo.DeletePrice(id);
        _state.NotifyDataChanged();
        RefreshData();
    }
}

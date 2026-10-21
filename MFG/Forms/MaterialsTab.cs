using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Forms;

/// <summary>
/// 원료 관리: 목록·검색, 신규 등록(코드 자동 채번 + 이름/별칭 중복 경고), 매입처별 단가 이력, 혼합물 구성, 사용 제조법.
/// </summary>
public class MaterialsTab : UserControl, IMfgTab
{
    private readonly AppState _state;
    private readonly MaterialRepository _repo = new();
    private readonly DataGridView _grid = Ui.Grid();
    private readonly TextBox _search = Ui.Text(220, input: false);
    private readonly CheckBox _showInactive = new() { Text = "사용중지 포함", AutoSize = true, Margin = new Padding(8, 8, 4, 4) };

    // 상세
    private readonly Label _code = Ui.Label("-", bold: true);
    private readonly TextBox _name = Ui.Text(260);
    private readonly TextBox _aliases = Ui.Text(260, multiline: true);
    private readonly TextBox _inci = Ui.Text(260);
    private readonly TextBox _cas = Ui.Text(160);
    private readonly TextBox _role = Ui.Text(200);
    private readonly TextBox _density = Ui.Text(100);
    private readonly CheckBox _isMixture = new() { Text = "혼합 원료", AutoSize = true };
    private readonly CheckBox _taxFree = new() { Text = "면세(VAT 없음)", AutoSize = true };
    private readonly CheckBox _active = new() { Text = "사용", AutoSize = true, Checked = true };
    private readonly ComboBox _defaultSupplier = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox _note = Ui.Text(260, multiline: true);
    private readonly DataGridView _prices = Ui.Grid();
    private readonly DataGridView _components = Ui.Grid(readOnly: false);
    private readonly Label _usage = Ui.Label("");

    private List<Material> _materials = [];
    private List<Supplier> _suppliers = [];
    private Material? _current;
    private bool _filling;

    public MaterialsTab(AppState state)
    {
        _state = state;
        Dock = DockStyle.Fill;

        _grid.Columns.AddRange(
            Ui.Col("code", "코드", 70), Ui.Col("name", "원료명", 180), Ui.Col("inci", "전성분명", 180), Ui.Col("cas", "CAS", 100),
            Ui.Col("density", "비중", 60, right: true), Ui.Col("price", "현재단가(원/kg)", 110, right: true), Ui.Col("date", "적용일", 85),
            Ui.Col("supplier", "매입처", 120), Ui.Col("uncertain", "확실도", 60), Ui.Col("usage", "사용 제조법", 70, right: true), Ui.Col("note", "비고", 240));
        _grid.SelectionChanged += (_, _) => { if (!_filling) LoadDetail(Ui.SelectedId(_grid)); };
        _search.TextChanged += (_, _) => Ui.ApplyFilter(_grid, _search.Text);
        _showInactive.CheckedChanged += (_, _) => RefreshData();

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_grid);
        left.Controls.Add(Ui.Bar(Ui.Label("검색"), _search, _showInactive, Ui.Button("신규 원료", (_, _) => NewMaterial())));

        // 상세(오른쪽): 기본정보 / 단가이력 / 혼합물 구성 탭
        var info = Ui.Form(
            ("코드", _code), ("원료명", _name), ("별칭(구 표기명, 줄바꿈)", _aliases), ("전성분명", _inci), ("CAS", _cas), ("역할", _role),
            ("비중(g/ml)", _density), ("", _isMixture), ("", _taxFree), ("", _active), ("기본 매입처", _defaultSupplier), ("비고", _note));
        var infoPage = new TabPage("기본정보") { AutoScroll = true };
        infoPage.Controls.Add(info);
        infoPage.Controls.Add(_usage);
        _usage.Dock = DockStyle.Bottom;
        infoPage.Controls.Add(Ui.Bar(Ui.Button("저장", (_, _) => SaveCurrent()), Ui.Button("사용중지/재사용", (_, _) => ToggleActive())));
        foreach (Control c in infoPage.Controls) if (c is FlowLayoutPanel f) f.Dock = DockStyle.Bottom;

        _prices.Columns.AddRange(
            Ui.Col("date", "적용일", 90), Ui.Col("price", "단가(원/kg)", 100, right: true), Ui.Col("supplier", "매입처", 130),
            Ui.Col("source", "출처", 120), Ui.Col("uncertain", "확실도", 60), Ui.Col("note", "메모", 300));
        var pricePage = new TabPage("단가 이력");
        pricePage.Controls.Add(_prices);
        pricePage.Controls.Add(Ui.Bar(Ui.Button("단가 추가", (_, _) => AddPrice()), Ui.Button("선택 단가 삭제", (_, _) => DeletePrice()),
            Ui.Label("맨 위(적용일 최신, 기본 매입처 우선)가 현재 단가입니다.")));

        _components.Columns.AddRange(Ui.Col("name", "구성성분(전성분명)", 220), Ui.Col("cas", "CAS", 110), Ui.Col("ratio", "구성비율(%)", 90, right: true));
        _components.AllowUserToAddRows = true;
        _components.AllowUserToDeleteRows = true;
        var compPage = new TabPage("혼합물 구성");
        compPage.Controls.Add(_components);
        compPage.Controls.Add(Ui.Bar(Ui.Button("구성 저장", (_, _) => SaveComponents()), Ui.Label("전성분 표기용. 비율 합계 100%.")));

        var detailTabs = new TabControl { Dock = DockStyle.Fill };
        detailTabs.TabPages.AddRange([infoPage, pricePage, compPage]);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 760 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(detailTabs);
        Controls.Add(split);
        Load += (_, _) => split.SplitterDistance = Math.Max(300, Width - 520);

        _state.VatModeChanged += () => { if (Visible) RefreshData(); };
    }

    public void RefreshData()
    {
        var keep = _current?.Id;
        _suppliers = new SupplierRepository().GetAll();
        _materials = _repo.GetAll();
        var usage = _repo.GetUsage();
        var supplierNames = _suppliers.ToDictionary(s => s.ChannelCode, s => s.Name);

        _defaultSupplier.Items.Clear();
        _defaultSupplier.Items.Add("(지정 안 함)");
        foreach (var s in _suppliers) _defaultSupplier.Items.Add(s);

        _grid.Columns["price"]!.HeaderText = $"현재단가(원/kg, {_state.VatLabel})";
        _filling = true;
        _grid.Rows.Clear();
        foreach (var m in _materials.Where(m => _showInactive.Checked || m.IsActive))
        {
            var p = m.CurrentPrice;
            var i = _grid.Rows.Add(m.Code, m.Name, m.Inci, m.Cas, m.Density is { } d ? Fmt.Num(d) : "",
                p is null ? "(없음)" : Fmt.Won(Vat.ToView(p.PricePerKg, _state.ShowVatIncluded, m.IsTaxFree)),
                p?.EffectiveDate ?? "", p?.SupplierCode is { } sc ? supplierNames.GetValueOrDefault(sc, sc) : "",
                p?.IsUncertain == true ? "불확실" : "", usage.TryGetValue(m.Id, out var u) ? u.Count : 0, FirstLine(m.Note));
            var row = _grid.Rows[i];
            row.Tag = m.Id;
            if (!m.IsActive) row.DefaultCellStyle.ForeColor = Theme.MutedFore;
            if (p is null) row.Cells["price"].Style.ForeColor = Theme.WarnFore;
            if (p?.IsUncertain == true) row.Cells["uncertain"].Style.ForeColor = Theme.WarnFore;
        }
        Ui.ApplyFilter(_grid, _search.Text);
        Ui.Reselect(_grid, keep);
        _filling = false;
        LoadDetail(Ui.SelectedId(_grid));
    }

    private static string FirstLine(string s) => s.Split('\n')[0];

    private void LoadDetail(long? id)
    {
        _current = id is null ? null : _materials.FirstOrDefault(m => m.Id == id);
        var m = _current ?? new Material();
        _code.Text = _current is null ? "(신규 — 저장 시 자동 부여)" : m.Code;
        _name.Text = m.Name;
        _aliases.Text = m.Aliases.Replace("\n", Environment.NewLine);
        _inci.Text = m.Inci;
        _cas.Text = m.Cas;
        _role.Text = m.Role;
        _density.Text = m.Density is { } d ? Fmt.Num(d) : "";
        _isMixture.Checked = m.IsMixture;
        _taxFree.Checked = m.IsTaxFree;
        _active.Checked = m.IsActive;
        _note.Text = m.Note.Replace("\n", Environment.NewLine);
        _defaultSupplier.SelectedIndex = 0;
        for (var i = 1; i < _defaultSupplier.Items.Count; i++)
        {
            if (((Supplier)_defaultSupplier.Items[i]!).ChannelCode == m.DefaultSupplierCode) _defaultSupplier.SelectedIndex = i;
        }

        _prices.Columns["price"]!.HeaderText = $"단가(원/kg, {_state.VatLabel})";
        _prices.Rows.Clear();
        _components.Rows.Clear();
        _usage.Text = "";
        if (_current is null) return;

        var names = _suppliers.ToDictionary(s => s.ChannelCode, s => s.Name);
        var currentId = _current.CurrentPrice?.Id;
        foreach (var p in _repo.GetPrices(_current.Id))
        {
            var i = _prices.Rows.Add(p.EffectiveDate.Length == 0 ? "(날짜 미상)" : p.EffectiveDate,
                Fmt.Won(Vat.ToView(p.PricePerKg, _state.ShowVatIncluded, m.IsTaxFree)),
                p.SupplierCode is { } sc ? names.GetValueOrDefault(sc, sc) : "", p.Source, p.IsUncertain ? "불확실" : "", p.Note.Replace("\n", " / "));
            _prices.Rows[i].Tag = p.Id;
            if (p.Id == currentId) _prices.Rows[i].DefaultCellStyle.Font = new Font(_prices.Font, FontStyle.Bold);
        }
        foreach (var comp in _repo.GetComponents(_current.Id))
        {
            _components.Rows.Add(comp.ComponentName, comp.Cas, Fmt.Num(comp.Ratio * 100));
        }
        var usage = _repo.GetUsage();
        _usage.Text = usage.TryGetValue(_current.Id, out var codes) ? "사용 제조법: " + string.Join(", ", codes) : "사용 제조법: 없음";
    }

    private void NewMaterial()
    {
        _grid.ClearSelection();
        _grid.CurrentCell = null;
        LoadDetail(null);
        _name.Focus();
    }

    private void SaveCurrent()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0) { Ui.Warn(this, "원료명을 입력하세요."); return; }
        double? density = null;
        if (_density.Text.Trim().Length > 0)
        {
            if (!Fmt.TryParse(_density.Text, out var d) || d <= 0) { Ui.Warn(this, "비중은 0보다 큰 숫자로 입력하세요."); return; }
            density = d;
        }

        var m = _current ?? new Material();
        var dupes = _repo.FindByNameOrAlias(name, m.Id);
        if (dupes.Count > 0 && !Ui.Confirm(this, $"같은 이름/별칭의 원료가 이미 있습니다:\n{string.Join("\n", dupes.Select(x => x.ToString()))}\n\n그래도 저장할까요?"))
        {
            return;
        }

        m.Name = name;
        m.Aliases = string.Join("\n", _aliases.Lines.Select(l => l.Trim()).Where(l => l.Length > 0));
        m.Inci = _inci.Text.Trim();
        m.Cas = _cas.Text.Trim();
        m.Role = _role.Text.Trim();
        m.Density = density;
        m.IsMixture = _isMixture.Checked;
        m.IsTaxFree = _taxFree.Checked;
        m.IsActive = _active.Checked;
        m.DefaultSupplierCode = _defaultSupplier.SelectedItem is Supplier s ? s.ChannelCode : null;
        m.Note = _note.Text.Replace("\r\n", "\n").Trim();
        var isNew = m.Id == 0;
        _repo.Save(m);
        _current = m;
        _state.NotifyDataChanged();
        RefreshData();
        Ui.Reselect(_grid, m.Id);
        if (isNew && Ui.Confirm(this, $"원료 {m.Code} {m.Name}을(를) 등록했습니다. 지금 단가를 입력할까요?")) AddPrice();
    }

    private void ToggleActive()
    {
        if (_current is null) return;
        _current.IsActive = !_current.IsActive;
        _repo.Save(_current);
        _state.NotifyDataChanged();
        RefreshData();
    }

    private void AddPrice()
    {
        if (_current is null) { Ui.Warn(this, "먼저 원료를 선택하거나 저장하세요."); return; }
        using var dlg = new PriceDialog($"단가 추가 — {_current}", "원/kg", _suppliers, _current.DefaultSupplierCode, _state.ShowVatIncluded, _current.IsTaxFree, allowUncertain: true);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _repo.AddPrice(new MaterialPrice
        {
            MaterialId = _current.Id, SupplierCode = dlg.SupplierCode, PricePerKg = dlg.PriceIncl, EffectiveDate = dlg.EffectiveDate,
            Source = dlg.Source, IsUncertain = dlg.IsUncertain, Note = dlg.Note,
        });
        _state.NotifyDataChanged();
        RefreshData();
    }

    private void DeletePrice()
    {
        if (_prices.CurrentRow?.Tag is not long priceId) return;
        if (!Ui.Confirm(this, "선택한 단가 기록을 삭제할까요? (잘못 입력한 경우에만 삭제하세요. 단가 변동은 새 단가를 추가하는 방식으로 기록합니다.)")) return;
        _repo.DeletePrice(priceId);
        _state.NotifyDataChanged();
        RefreshData();
    }

    private void SaveComponents()
    {
        if (_current is null) return;
        var list = new List<MaterialComponent>();
        foreach (DataGridViewRow r in _components.Rows)
        {
            if (r.IsNewRow) continue;
            var name = r.Cells[0].Value?.ToString()?.Trim() ?? "";
            if (name.Length == 0) continue;
            Fmt.TryParse(r.Cells[2].Value?.ToString(), out var pct);
            list.Add(new MaterialComponent { ComponentName = name, Cas = r.Cells[1].Value?.ToString()?.Trim() ?? "", Ratio = pct / 100 });
        }
        var sum = list.Sum(x => x.Ratio);
        if (list.Count > 0 && Math.Abs(sum - 1) > 0.0001 && !Ui.Confirm(this, $"구성비율 합계가 {Fmt.Pct(sum)}입니다(100%가 아님). 그래도 저장할까요?")) return;
        _repo.ReplaceComponents(_current.Id, list);
        if (list.Count > 0 && !_current.IsMixture) { _current.IsMixture = true; _repo.Save(_current); }
        _state.NotifyDataChanged();
        RefreshData();
    }
}

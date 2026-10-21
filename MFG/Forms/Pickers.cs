using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Forms;

/// <summary>
/// 원료 선택 창. 원료명·별칭(구 표기명)·코드·전성분명·CAS로 검색하고, 목록에 없으면 그 자리에서 신규 등록한다
/// (코드 자동 채번 — 엑셀에서 신규 원료마다 코드를 매기던 불편 해소).
/// </summary>
public class MaterialPicker : Form
{
    private readonly DataGridView _grid = Ui.Grid();
    private readonly TextBox _search = Ui.Text(300, input: false);
    private readonly AppState _state;
    private List<Material> _all = [];

    public Material? Selected { get; private set; }

    public MaterialPicker(AppState state, string initialSearch = "")
    {
        _state = state;
        Text = "원료 선택";
        StartPosition = FormStartPosition.CenterParent;
        Width = 980;
        Height = 620;

        _grid.MultiSelect = false;
        _grid.Columns.AddRange(Ui.Col("code", "코드", 70), Ui.Col("name", "원료명", 200), Ui.Col("alias", "별칭", 180), Ui.Col("inci", "전성분명", 200),
            Ui.Col("cas", "CAS", 100), Ui.Col("price", $"현재단가(원/kg, {state.VatLabel})", 120, right: true), Ui.Col("density", "비중", 60, right: true));
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Accept(); };
        _grid.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; Accept(); } };
        _search.Text = initialSearch;
        _search.TextChanged += (_, _) => Ui.ApplyFilter(_grid, _search.Text);
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down) { _grid.Focus(); e.Handled = true; }
            if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; SelectFirstVisible(); Accept(); }
        };

        var ok = Ui.Button("선택", (_, _) => Accept(), 90);
        var add = Ui.Button("목록에 없음 → 신규 원료 등록", (_, _) => QuickAdd());
        Controls.Add(_grid);
        Controls.Add(Ui.Bar(Ui.Label("검색(이름·별칭·코드·전성분·CAS)"), _search, ok, add));
        Load += (_, _) => { Reload(); _search.Focus(); };
    }

    private void Reload(long? select = null)
    {
        _all = new MaterialRepository().GetAll(includeInactive: false);
        _grid.Rows.Clear();
        foreach (var m in _all)
        {
            var i = _grid.Rows.Add(m.Code, m.Name, string.Join(", ", m.AliasList), m.Inci, m.Cas,
                m.CurrentPrice is { } p ? Fmt.Won(Vat.ToView(p.PricePerKg, _state.ShowVatIncluded, m.IsTaxFree)) : "(없음)", m.Density is { } d ? Fmt.Num(d) : "");
            _grid.Rows[i].Tag = m.Id;
        }
        Ui.ApplyFilter(_grid, _search.Text);
        if (select is not null) Ui.Reselect(_grid, select);
        else SelectFirstVisible();
    }

    private void SelectFirstVisible()
    {
        var row = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Visible);
        if (row is not null) _grid.CurrentCell = row.Cells[1];
    }

    private void Accept()
    {
        if (Ui.SelectedId(_grid) is not { } id || !_grid.CurrentRow!.Visible) return;
        Selected = _all.First(m => m.Id == id);
        DialogResult = DialogResult.OK;
    }

    private void QuickAdd()
    {
        using var dlg = new QuickMaterialDialog(_state, _search.Text.Trim());
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.CreatedMaterial is null) return;
        _search.Text = "";
        Reload(dlg.CreatedMaterial.Id);
        Accept();
    }
}

/// <summary>배합 작성 중 신규 원료 빠른 등록(원료명·비중·단가). 나머지 정보는 원료 탭에서 보충한다.</summary>
public class QuickMaterialDialog : Form
{
    private readonly TextBox _name = Ui.Text(260);
    private readonly TextBox _inci = Ui.Text(260);
    private readonly TextBox _density = Ui.Text(100);
    private readonly TextBox _price = Ui.Text(140);
    private readonly ComboBox _supplier = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly AppState _state;

    public Material? CreatedMaterial { get; private set; }

    public QuickMaterialDialog(AppState state, string name)
    {
        _state = state;
        Text = "신규 원료 등록 (코드 자동 부여)";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        Width = 480;
        Height = 330;
        _name.Text = name;
        _supplier.Items.Add("(매입처 없음)");
        foreach (var s in new SupplierRepository().GetAll()) _supplier.Items.Add(s);
        _supplier.SelectedIndex = 0;

        Controls.Add(Ui.Form(("원료명", _name), ("전성분명", _inci), ("비중(g/ml)", _density), ($"단가(원/kg, {state.VatLabel})", _price), ("매입처", _supplier)));
        var ok = new Button { Text = "등록", Width = 90 };
        ok.Click += (_, _) => Save();
        var cancel = new Button { Text = "취소", Width = 90, DialogResult = DialogResult.Cancel };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 42, Padding = new Padding(6) };
        bar.Controls.AddRange([cancel, ok]);
        Controls.Add(bar);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void Save()
    {
        var repo = new MaterialRepository();
        var name = _name.Text.Trim();
        if (name.Length == 0) { Ui.Warn(this, "원료명을 입력하세요."); return; }
        var dupes = repo.FindByNameOrAlias(name);
        if (dupes.Count > 0 && !Ui.Confirm(this, $"같은 이름/별칭의 원료가 이미 있습니다:\n{string.Join("\n", dupes.Select(d => d.ToString()))}\n\n그래도 새로 등록할까요?")) return;
        double? density = Fmt.TryParse(_density.Text, out var d) && d > 0 ? d : null;
        double? price = Fmt.TryParse(_price.Text, out var p) && p >= 0 ? p : null;

        var m = new Material { Name = name, Inci = _inci.Text.Trim(), Density = density, DefaultSupplierCode = (_supplier.SelectedItem as Supplier)?.ChannelCode };
        repo.Save(m);
        if (price is not null)
        {
            repo.AddPrice(new MaterialPrice
            {
                MaterialId = m.Id, PricePerKg = Vat.FromView(price.Value, _state.ShowVatIncluded), EffectiveDate = MfgDb.Today(),
                SupplierCode = m.DefaultSupplierCode, Source = "배합 작성 중 신규 등록",
            });
        }
        CreatedMaterial = m;
        _state.NotifyDataChanged();
        DialogResult = DialogResult.OK;
    }
}

/// <summary>부자재 선택 창(제조법 용량표의 용기·캡·라벨·박스 칸에 연결).</summary>
public class PackPicker : Form
{
    private readonly DataGridView _grid = Ui.Grid();
    private readonly TextBox _search = Ui.Text(260, input: false);
    private readonly TextBox _qty = Ui.Text(60);
    private List<PackMaterial> _all = [];

    public PackMaterial? Selected { get; private set; }
    public double Quantity { get; private set; } = 1;

    public PackPicker(AppState state, string category, double quantity)
    {
        Text = $"부자재 선택 — {category}";
        StartPosition = FormStartPosition.CenterParent;
        Width = 860;
        Height = 560;
        _qty.Text = Fmt.Num(quantity);
        _grid.MultiSelect = false;
        _grid.Columns.AddRange(Ui.Col("code", "코드", 70), Ui.Col("cat", "분류", 110), Ui.Col("name", "품명·규격", 320),
            Ui.Col("price", $"현재단가(원/개, {state.VatLabel})", 130, right: true));
        _all = new PackMaterialRepository().GetAll(includeInactive: false);
        foreach (var p in _all.OrderBy(p => p.Category == category ? 0 : 1).ThenBy(p => p.Name))
        {
            var i = _grid.Rows.Add(p.Code, p.Category, p.Name, p.CurrentPrice is { } cp ? Fmt.Won(Vat.ToView(cp.UnitPrice, state.ShowVatIncluded, p.IsTaxFree)) : "(없음)");
            _grid.Rows[i].Tag = p.Id;
            if (p.Category != category) _grid.Rows[i].DefaultCellStyle.ForeColor = Theme.MutedFore;
        }
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Accept(); };
        _search.TextChanged += (_, _) => Ui.ApplyFilter(_grid, _search.Text);
        Controls.Add(_grid);
        Controls.Add(Ui.Bar(Ui.Label("검색"), _search, Ui.Label("수량"), _qty, Ui.Button("선택", (_, _) => Accept(), 90),
            Ui.Label("목록에 없으면 [부자재] 탭에서 먼저 등록하세요.")));
    }

    private void Accept()
    {
        if (Ui.SelectedId(_grid) is not { } id) return;
        if (!Fmt.TryParse(_qty.Text, out var q) || q <= 0) { Ui.Warn(this, "수량을 0보다 큰 숫자로 입력하세요."); return; }
        Selected = _all.First(p => p.Id == id);
        Quantity = q;
        DialogResult = DialogResult.OK;
    }
}

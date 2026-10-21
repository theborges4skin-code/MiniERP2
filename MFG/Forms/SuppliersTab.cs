using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Forms;

/// <summary>
/// 매입처 = MiniERP2 매입처 채널(SalesChannelTable.IsPurchase=1). MiniERP2에서 읽어 와 동기화만 한다 —
/// 신규 매입처 등록·이름 변경은 MiniERP2 채널 설정에서 한다(docs/MFG_PLAN.md §2).
/// </summary>
public class SuppliersTab : UserControl, IMfgTab
{
    private readonly AppState _state;
    private readonly SupplierRepository _repo = new();
    private readonly DataGridView _grid = Ui.Grid(readOnly: false);
    private readonly DataGridView _items = Ui.Grid();
    private readonly Label _dbInfo = Ui.Label("");
    private bool _filling;

    public SuppliersTab(AppState state)
    {
        _state = state;
        Dock = DockStyle.Fill;

        _grid.Columns.AddRange(
            Ui.Col("code", "채널코드", 100, readOnly: true), Ui.Col("name", "매입처명(MiniERP2)", 200, readOnly: true),
            Ui.Col("mat", "원료 수", 70, right: true, readOnly: true), Ui.Col("pack", "부자재 수", 70, right: true, readOnly: true),
            Ui.Col("synced", "마지막 동기화", 140, readOnly: true), Ui.Col("memo", "MFG 메모(직접 입력)", 300));
        Theme.ApplyInput(_grid.Columns["memo"]!.DefaultCellStyle);
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.CellEndEdit += (_, e) =>
        {
            if (_grid.Columns[e.ColumnIndex].Name != "memo") return;
            var code = _grid.Rows[e.RowIndex].Cells["code"].Value?.ToString();
            if (code is not null) _repo.UpdateMemo(code, _grid.Rows[e.RowIndex].Cells["memo"].Value?.ToString() ?? "");
        };
        _grid.SelectionChanged += (_, _) => { if (!_filling) ShowItems(); };

        _items.Columns.AddRange(Ui.Col("kind", "구분", 70), Ui.Col("code", "코드", 70), Ui.Col("name", "품명", 260),
            Ui.Col("price", "현재단가", 110, right: true), Ui.Col("unit", "단위", 60), Ui.Col("date", "적용일", 90));

        var top = new Panel { Dock = DockStyle.Fill };
        top.Controls.Add(_grid);
        top.Controls.Add(Ui.Bar(Ui.Button("MiniERP2에서 매입처 불러오기", (_, _) => Sync()), _dbInfo));
        var bottom = new GroupBox { Text = "선택 매입처의 원료·부자재(기본 매입처 또는 현재 단가의 매입처 기준)", Dock = DockStyle.Fill };
        bottom.Controls.Add(_items);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(top);
        split.Panel2.Controls.Add(bottom);
        Controls.Add(split);
        Load += (_, _) => split.SplitterDistance = Math.Max(200, Height / 2);
    }

    public void RefreshData()
    {
        var bridge = _state.Bridge;
        _dbInfo.Text = bridge.IsAvailable ? $"MiniERP2 DB: {bridge.DatabasePath}" : $"MiniERP2 DB 없음: {bridge.DatabasePath} (설정 탭에서 경로 지정)";
        _dbInfo.ForeColor = bridge.IsAvailable ? Theme.MutedFore : Theme.WarnFore;

        var mats = new MaterialRepository().GetAll();
        var packs = new PackMaterialRepository().GetAll();
        _filling = true;
        _grid.Rows.Clear();
        foreach (var s in _repo.GetAll())
        {
            var i = _grid.Rows.Add(s.ChannelCode, s.Name, mats.Count(m => Belongs(m.DefaultSupplierCode, m.CurrentPrice?.SupplierCode, s.ChannelCode)),
                packs.Count(p => Belongs(p.DefaultSupplierCode, p.CurrentPrice?.SupplierCode, s.ChannelCode)), s.SyncedAt ?? "", s.Memo);
            _grid.Rows[i].Tag = s.ChannelCode;
        }
        _filling = false;
        ShowItems();
    }

    private static bool Belongs(string? defaultCode, string? priceCode, string code) => defaultCode == code || priceCode == code;

    private void ShowItems()
    {
        _items.Rows.Clear();
        if (_grid.CurrentRow?.Tag is not string code) return;
        _items.Columns["price"]!.HeaderText = $"현재단가({_state.VatLabel})";
        foreach (var m in new MaterialRepository().GetAll().Where(m => Belongs(m.DefaultSupplierCode, m.CurrentPrice?.SupplierCode, code)))
        {
            _items.Rows.Add("원료", m.Code, m.Name, m.CurrentPrice is { } p ? Fmt.Won(Vat.ToView(p.PricePerKg, _state.ShowVatIncluded, m.IsTaxFree)) : "", "원/kg", m.CurrentPrice?.EffectiveDate ?? "");
        }
        foreach (var pk in new PackMaterialRepository().GetAll().Where(p => Belongs(p.DefaultSupplierCode, p.CurrentPrice?.SupplierCode, code)))
        {
            _items.Rows.Add("부자재", pk.Code, pk.Name, pk.CurrentPrice is { } p ? Fmt.Won(Vat.ToView(p.UnitPrice, _state.ShowVatIncluded, pk.IsTaxFree)) : "", "원/개", pk.CurrentPrice?.EffectiveDate ?? "");
        }
    }

    private void Sync()
    {
        var bridge = _state.Bridge;
        if (!bridge.IsAvailable) { Ui.Warn(this, $"MiniERP2 DB를 찾을 수 없습니다.\n{bridge.DatabasePath}\n\n설정 탭에서 경로를 지정하세요."); return; }
        var channels = bridge.GetPurchaseChannels();
        var (added, updated) = _repo.Sync(channels);
        _state.NotifyDataChanged();
        RefreshData();
        Ui.Info(this, $"MiniERP2 매입처 채널 {channels.Count}곳을 불러왔습니다. (신규 {added}, 이름 변경 {updated})\n신규 매입처는 MiniERP2 [채널 설정]에서 '매입' 채널로 등록한 뒤 다시 불러오세요.");
    }
}

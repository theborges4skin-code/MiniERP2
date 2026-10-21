using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Forms;

/// <summary>
/// MiniERP2 원가 전송(docs/MFG_PLAN.md §2). 확정된 완제품 중 SKU가 MiniERP2에 있고 원가가 다른 것만 보여 주고,
/// 체크한 것만 ItemTable.CostPrice(VAT포함)로 보낸다. 전송 직전 MiniERP2 DB를 자동 백업한다.
/// 보내는 값 = 확정 원가(확정 시점 합계, PM과 같은 구성: 원료+용기+패키지+스티커 등+기타+택배비).
/// </summary>
public class PushTab : UserControl, IMfgTab
{
    private readonly AppState _state;
    private readonly DataGridView _grid = Ui.Grid(readOnly: false);
    private readonly CheckBox _onlyDiff = new() { Text = "원가가 다른 것만", AutoSize = true, Checked = true, Margin = new Padding(6, 8, 4, 4) };
    private readonly Label _info = Ui.Label("");

    public PushTab(AppState state)
    {
        _state = state;
        Dock = DockStyle.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "check", HeaderText = "전송", Width = 45 });
        _grid.Columns.AddRange(
            Ui.Col("sku", "SKU", 120, readOnly: true), Ui.Col("name", "상품명(MFG)", 260, readOnly: true), Ui.Col("erpName", "품목명(MiniERP2)", 220, readOnly: true),
            Ui.Col("erp", "MiniERP2 현재 원가", 120, right: true, readOnly: true), Ui.Col("mfg", "MFG 확정 원가", 120, right: true, readOnly: true),
            Ui.Col("diff", "차이", 90, right: true, readOnly: true), Ui.Col("rate", "차이율", 70, right: true, readOnly: true),
            Ui.Col("pushed", "마지막 전송", 140, readOnly: true), Ui.Col("note", "확인", 260, readOnly: true));
        _onlyDiff.CheckedChanged += (_, _) => RefreshData();

        Controls.Add(_grid);
        Controls.Add(Ui.Bar(Ui.Button("비교 새로고침", (_, _) => RefreshData()), _onlyDiff, Ui.Button("전체 선택", (_, _) => SetAll(true)),
            Ui.Button("선택 해제", (_, _) => SetAll(false)), Ui.Button("체크한 항목 MiniERP2로 전송", (_, _) => Push()), _info));
    }

    public void RefreshData()
    {
        _grid.Rows.Clear();
        var bridge = _state.Bridge;
        if (!bridge.IsAvailable)
        {
            _info.Text = $"MiniERP2 DB를 찾을 수 없습니다: {bridge.DatabasePath} (설정 탭에서 경로 지정)";
            _info.ForeColor = Ui.WarnFore;
            return;
        }

        Dictionary<string, (string Name, double Cost)> erp;
        try { erp = bridge.GetItemCosts(); }
        catch (Exception ex) { _info.Text = "MiniERP2 DB 읽기 실패: " + ex.Message; _info.ForeColor = Ui.WarnFore; return; }

        var snap = CostSnapshot.Load();
        var inclCalc = snap.ProductCalculator(true);
        int confirmed = 0, missing = 0, same = 0;
        foreach (var p in snap.Products.Values.Where(p => p.IsConfirmed && p.ConfirmedTotal is not null).OrderBy(p => p.SortOrder))
        {
            confirmed++;
            var mfgCost = p.ConfirmedTotal!.Value;
            var notes = new List<string>();
            if (snap.ProductNeedsReconfirm(p, inclCalc)) notes.Add("확정 후 원가 변동 — 재확인 권장");
            if (string.IsNullOrEmpty(p.Sku)) { missing++; continue; }
            if (!erp.TryGetValue(p.Sku, out var item))
            {
                missing++;
                if (_onlyDiff.Checked) continue;
                AddRow(p, null, mfgCost, "MiniERP2에 없는 SKU — 전송 불가(MiniERP2에서 품목 먼저 등록)", false);
                continue;
            }
            var diff = mfgCost - item.Cost;
            if (Math.Abs(diff) < 0.005) { same++; if (_onlyDiff.Checked) continue; }
            AddRow(p, item, mfgCost, string.Join(" ", notes), Math.Abs(diff) >= 0.005);
        }
        _info.Text = $"확정 품목 {confirmed} / 원가 같음 {same} / MiniERP2에 없음·SKU 없음 {missing} / 표시 {_grid.Rows.Count}";
        _info.ForeColor = Ui.MutedFore;
    }

    private void AddRow(FinishedProduct p, (string Name, double Cost)? erp, double mfgCost, string note, bool canPush)
    {
        var diff = erp is { } e ? mfgCost - e.Cost : (double?)null;
        var i = _grid.Rows.Add(false, p.Sku ?? "", p.Name, erp?.Name ?? "", erp is { } e1 ? Fmt.Won(e1.Cost) : "", Fmt.Won(mfgCost),
            diff is { } d ? (d > 0 ? "+" : "") + Fmt.Won(d) : "", diff is { } d2 && erp!.Value.Cost > 0 ? Fmt.Pct(d2 / erp.Value.Cost) : "",
            p.LastPushedAt is null ? "" : $"{p.LastPushedAt} ({Fmt.Won(p.LastPushedCost)})", note);
        var row = _grid.Rows[i];
        row.Tag = p.Id;
        if (!canPush) { row.Cells["check"].ReadOnly = true; row.DefaultCellStyle.ForeColor = Ui.MutedFore; }
        if (note.Length > 0) row.Cells["note"].Style.ForeColor = Ui.WarnFore;
    }

    private void SetAll(bool value)
    {
        foreach (DataGridViewRow r in _grid.Rows) if (!r.Cells["check"].ReadOnly) r.Cells["check"].Value = value;
    }

    private void Push()
    {
        _grid.EndEdit();
        var rows = _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Cells["check"].Value is true && !r.Cells["check"].ReadOnly).ToList();
        if (rows.Count == 0) { Ui.Info(this, "전송할 항목을 체크하세요."); return; }
        var snap = CostSnapshot.Load();
        var items = rows.Select(r => snap.Products[(long)r.Tag!]).Select(p => (Product: p, Cost: p.ConfirmedTotal!.Value)).ToList();
        var preview = string.Join("\n", items.Take(15).Select(x => $"{x.Product.Sku}: {Fmt.Won(x.Cost)}")) + (items.Count > 15 ? $"\n… 외 {items.Count - 15}건" : "");
        if (!Ui.Confirm(this, $"{items.Count}개 품목의 원가를 MiniERP2 마스터(ItemTable.CostPrice, VAT포함)에 반영합니다.\n전송 전에 MiniERP2 DB를 자동 백업합니다.\n\n{preview}\n\n진행할까요?")) return;

        try
        {
            var (updated, skipped, backupPath) = _state.Bridge.PushCosts(items.Select(x => (x.Product.Sku!, x.Cost)).ToList());
            new ProductRepository().MarkPushed(items.Where(x => !skipped.Contains(x.Product.Sku!)).Select(x => (x.Product.Id, x.Cost)));
            Utils.Log.Write($"[Push] MiniERP2 원가 반영 {updated}건, 건너뜀 {skipped.Count}건, 백업 {backupPath}");
            Ui.Info(this, $"MiniERP2에 {updated}건 반영했습니다." + (skipped.Count > 0 ? $"\nMiniERP2에 없는 SKU {skipped.Count}건은 건너뜀: {string.Join(", ", skipped.Take(10))}" : "") +
                          $"\n\nMiniERP2 백업: {backupPath}\nMiniERP2가 열려 있으면 해당 화면을 새로고침하세요.");
        }
        catch (Exception ex)
        {
            Utils.Log.Write($"[Push] 실패: {ex}");
            Ui.Warn(this, "전송 실패: " + ex.Message + "\n\nMiniERP2가 DB를 쓰는 중이면 잠시 후 다시 시도하세요.");
        }
        _state.NotifyDataChanged();
        RefreshData();
    }
}

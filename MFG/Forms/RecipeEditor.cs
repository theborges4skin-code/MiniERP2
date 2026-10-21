using System.Text;
using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Forms;

/// <summary>
/// 제조법 편집 화면 — 제조법관련 엑셀 배합 시트의 틀을 옮긴 것(docs/MFG_PLAN.md §4-4).
/// 배합표(원료·투입량) → 비중 단계(계산/실측) → kg·L당 원료비 → 용량별 완제품 단가표 → 확정/미확정.
/// 편집은 작업 사본(_work)에 하고 [저장]할 때 DB에 쓴다. 확정된 제조법을 고쳐 저장하면 미확정으로 돌아간다.
/// </summary>
public class RecipeEditor : UserControl
{
    private const string RowVolume = "vol", RowFill = "fill", RowRaw = "raw", RowSub = "sub", RowUnit = "unit", RowPerMl = "perml";

    private readonly AppState _state;
    private readonly RecipeRepository _repo = new();

    private readonly Label _codeLabel = Ui.Label("", bold: true);
    private readonly TextBox _name = Ui.Text(260);
    private readonly TextBox _version = Ui.Text(200);
    private readonly Label _source = Ui.Label("");
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(8, 6, 4, 4), Font = new Font(Control.DefaultFont, FontStyle.Bold) };
    private readonly TextBox _measuredDensity = Ui.Text(70);
    private readonly TextBox _batchKg = Ui.Text(70);
    private readonly CheckBox _archived = new() { Text = "보관(목록에서 숨김)", AutoSize = true, Margin = new Padding(8, 8, 4, 4) };
    private readonly TextBox _memo = Ui.Text(300, multiline: true);
    private readonly DataGridView _lines = Ui.Grid(readOnly: false);
    private readonly DataGridView _sizes = Ui.Grid(readOnly: false);
    private readonly Label _summary = new() { AutoSize = true, Padding = new Padding(6), Font = new Font(Control.DefaultFont.FontFamily, 9.5f) };
    private readonly Label _dirtyLabel = Ui.Label("");
    private readonly ToolTip _tip = new();

    private Recipe? _work;
    private Dictionary<long, Material> _materials = [];
    private Dictionary<long, PackMaterial> _packs = [];
    private bool _dirty;
    private bool _loading;

    /// <summary>저장·확정 등으로 DB가 바뀌었을 때(목록 새로고침용).</summary>
    public event Action? Saved;

    public bool IsDirty => _dirty;

    public RecipeEditor(AppState state)
    {
        _state = state;
        Dock = DockStyle.Fill;
        Enabled = false;

        // ---- 헤더 ----
        var header1 = Ui.Bar(_codeLabel, Ui.Label("제품명"), _name, Ui.Label("버전"), _version, _status,
            Ui.Button("저장", (_, _) => Save()), Ui.Button("되돌리기", (_, _) => Reload()),
            Ui.Button("확정", (_, _) => Confirm()), Ui.Button("미확정으로", (_, _) => SetDraft()), _dirtyLabel);
        var header2 = Ui.Bar(Ui.Label("실측 비중"), _measuredDensity, Ui.Label("배치(kg)"), _batchKg, _archived, _source,
            Ui.Button("배치 계량표", (_, _) => ShowBatch()), Ui.Button("전성분(함량순)", (_, _) => ShowIngredients()), Ui.Button("확정 이력", (_, _) => ShowConfirmLogs()));
        _name.TextChanged += (_, _) => MarkDirty();
        _version.TextChanged += (_, _) => MarkDirty();
        _archived.CheckedChanged += (_, _) => MarkDirty();
        _memo.TextChanged += (_, _) => MarkDirty();
        _measuredDensity.Leave += (_, _) => { if (_work is null || _loading) return; ParseMeasuredDensity(); Recalc(); };
        _batchKg.Leave += (_, _) => { if (_work is null || _loading) return; if (Fmt.TryParse(_batchKg.Text, out var b) && b > 0) { _work.BatchKg = b; MarkDirty(); } };
        _tip.SetToolTip(_measuredDensity, "실측 비중을 넣으면 계산 비중 대신 이 값으로 충진중량·L당 원료비를 계산합니다. 비우면 계산 비중을 씁니다.");

        // ---- 배합표 ----
        _lines.Columns.AddRange(
            Ui.Col("no", "No", 36, readOnly: true), Ui.Col("stage", "단계", 60), Ui.Col("material", "원료명 (더블클릭/Enter로 선택)", 220, readOnly: true),
            Ui.Col("code", "코드", 60, readOnly: true), Ui.Col("cas", "CAS", 90, readOnly: true), Ui.Col("pct", "투입량(%)", 80, right: true),
            Ui.Col("price", "단가(원/kg)", 90, right: true, readOnly: true), Ui.Col("cost", "원료비(원/kg 제품)", 110, right: true, readOnly: true),
            Ui.Col("density", "비중", 55, right: true, readOnly: true), Ui.Col("memo", "공정 메모", 220));
        foreach (var c in new[] { "stage", "pct", "memo" }) _lines.Columns[c]!.DefaultCellStyle.BackColor = Ui.InputBack;
        _lines.Columns["material"]!.DefaultCellStyle.BackColor = Ui.InputBack;
        foreach (DataGridViewColumn c in _lines.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _lines.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && _lines.Columns[e.ColumnIndex].Name == "material") PickMaterial(e.RowIndex); };
        _lines.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _lines.CurrentCell is { } cell && _lines.Columns[cell.ColumnIndex].Name == "material")
            {
                e.Handled = true;
                PickMaterial(cell.RowIndex);
            }
        };
        _lines.CellEndEdit += (_, e) => OnLineEdited(e.RowIndex, e.ColumnIndex);

        var linesBox = new GroupBox { Text = "배합표", Dock = DockStyle.Fill };
        linesBox.Controls.Add(_lines);
        linesBox.Controls.Add(Ui.Bar(Ui.Button("행 추가", (_, _) => AddLine()), Ui.Button("행 삭제", (_, _) => RemoveLine()),
            Ui.Button("▲", (_, _) => MoveLine(-1), 36), Ui.Button("▼", (_, _) => MoveLine(1), 36),
            Ui.Label("투입량은 % 단위(합계 100%). 노란 칸 = 입력.")));

        // ---- 요약(비중 단계 포함) ----
        var summaryBox = new GroupBox { Text = "원가 요약 · 비중", Dock = DockStyle.Fill };
        summaryBox.Controls.Add(_summary);

        // ---- 용량별 완제품 단가표 ----
        _sizes.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _sizes.CellEndEdit += (_, e) => OnSizeEdited(e.RowIndex, e.ColumnIndex);
        _sizes.CellMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex > 0)
            {
                _sizes.CurrentCell = _sizes.Rows[e.RowIndex].Cells[e.ColumnIndex];
                ShowSizeCellMenu(e.RowIndex, e.ColumnIndex);
            }
        };
        _sizes.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex > 0 && IsPackRow(_sizes.Rows[e.RowIndex].Tag as string) && FindCost(e.RowIndex, e.ColumnIndex)?.PackMaterialId is not null)
            {
                ShowSizeCellMenu(e.RowIndex, e.ColumnIndex);
            }
        };
        var sizesBox = new GroupBox { Text = "용량별 완제품 단가", Dock = DockStyle.Fill };
        sizesBox.Controls.Add(_sizes);
        sizesBox.Controls.Add(Ui.Bar(Ui.Button("용량 추가", (_, _) => AddSize()), Ui.Button("선택 열 용량 삭제", (_, _) => RemoveSize()),
            Ui.Label("부자재 칸: 금액 직접 입력 또는 우클릭 → 부자재 목록에서 선택(파란 글씨 = 연결, 현재 단가 자동 반영).")));

        var memoBox = new GroupBox { Text = "제조 메모 / 공정", Dock = DockStyle.Fill };
        _memo.Dock = DockStyle.Fill;
        memoBox.Controls.Add(_memo);

        var middle = new SplitContainer { Dock = DockStyle.Fill };
        middle.Panel1.Controls.Add(linesBox);
        var sideSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        sideSplit.Panel1.Controls.Add(summaryBox);
        sideSplit.Panel2.Controls.Add(memoBox);
        middle.Panel2.Controls.Add(sideSplit);

        var main = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        main.Panel1.Controls.Add(middle);
        main.Panel2.Controls.Add(sizesBox);

        Controls.Add(main);
        Controls.Add(header2);
        Controls.Add(header1);
        Load += (_, _) =>
        {
            main.SplitterDistance = Math.Max(200, (int)(main.Height * 0.55));
            middle.SplitterDistance = Math.Max(400, middle.Width - 330);
            sideSplit.SplitterDistance = Math.Max(150, (int)(sideSplit.Height * 0.62));
        };
        _state.VatModeChanged += () => { if (_work is not null) Recalc(rebuildSizes: true); };
    }

    // ================= 로드 / 저장 =================

    public void LoadRecipe(long? recipeId)
    {
        _materials = new MaterialRepository().GetAll().ToDictionary(m => m.Id);
        _packs = new PackMaterialRepository().GetAll().ToDictionary(p => p.Id);
        var recipe = recipeId is null ? null : _repo.Get(recipeId.Value);
        _work = recipe;
        Enabled = recipe is not null;
        _loading = true;
        try
        {
            _lines.Rows.Clear();
            _sizes.Rows.Clear();
            _sizes.Columns.Clear();
            if (recipe is null)
            {
                _codeLabel.Text = "제조법을 선택하세요";
                _summary.Text = "";
                return;
            }
            _codeLabel.Text = recipe.Code;
            _name.Text = recipe.Name;
            _version.Text = recipe.Version;
            _source.Text = recipe.SourceSheet.Length > 0 ? $"원본: {recipe.SourceSheet}" : "";
            _measuredDensity.Text = recipe.MeasuredDensity is { } md ? Fmt.Num(md) : "";
            _batchKg.Text = Fmt.Num(recipe.BatchKg);
            _archived.Checked = recipe.IsArchived;
            _memo.Text = recipe.ProcessMemo.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            foreach (var line in recipe.Lines.OrderBy(l => l.SortOrder)) _lines.Rows.Add();
            UpdateStatusBadge();
        }
        finally
        {
            _loading = false;
        }
        _dirty = false;
        _dirtyLabel.Text = "";
        Recalc(rebuildSizes: true);
    }

    private void Reload()
    {
        if (_work is null) return;
        if (_dirty && !Ui.Confirm(this, "저장하지 않은 변경을 버리고 다시 불러올까요?")) return;
        LoadRecipe(_work.Id);
    }

    /// <summary>저장. 확정 상태에서 내용이 바뀌었으면 미확정으로 돌린다(사용자에게 알림). 저장했으면 true.</summary>
    public bool Save(bool silent = false)
    {
        if (_work is null) return false;
        if (!_dirty) return true;
        if (_name.Text.Trim().Length == 0) { Ui.Warn(this, "제품명을 입력하세요."); return false; }
        ParseMeasuredDensity();
        if (_work.IsConfirmed)
        {
            if (!Ui.Confirm(this, "확정된 제조법입니다. 변경 내용을 저장하면 '미확정'으로 돌아갑니다(이전 확정 내용은 확정 이력에 남아 있습니다).\n저장할까요?")) return false;
            _work.Status = RecipeStatus.Draft;
        }
        _work.Name = _name.Text.Trim();
        _work.Version = _version.Text.Trim();
        _work.IsArchived = _archived.Checked;
        _work.ProcessMemo = _memo.Text.Replace("\r\n", "\n").Trim();
        if (Fmt.TryParse(_batchKg.Text, out var b) && b > 0) _work.BatchKg = b;
        // 비어 있는 원료 행(원료 미선택)은 저장하지 않는다.
        _work.Lines = _work.Lines.Where(l => l.MaterialId != 0).ToList();
        _repo.Save(_work);
        _dirty = false;
        _dirtyLabel.Text = silent ? "" : "저장됨";
        _dirtyLabel.ForeColor = Ui.MutedFore;
        _state.NotifyDataChanged();
        Saved?.Invoke();
        LoadRecipe(_work.Id);
        return true;
    }

    private void Confirm()
    {
        if (_work is null) return;
        if (_dirty && !Save()) return;
        var calc = Calculator().CalculateRecipe(_work, true);
        var warnings = new List<string>();
        if (!calc.TotalIsOk) warnings.Add($"투입량 합계가 {Fmt.Pct(calc.TotalPercent)}입니다(100% 아님).");
        if (calc.MissingPriceCount > 0) warnings.Add($"단가 없는 원료 {calc.MissingPriceCount}개");
        if (calc.UncertainPriceCount > 0) warnings.Add($"'단가 확실치 않음' 원료 {calc.UncertainPriceCount}개");
        if (_work.Sizes.Count == 0) warnings.Add("용량별 단가가 없습니다.");
        var question = warnings.Count > 0 ? "확인할 점:\n- " + string.Join("\n- ", warnings) + "\n\n그래도 확정할까요? 확정 메모를 입력하세요." : "확정 메모(선택)를 입력하세요.";
        var note = Ui.Prompt(this, $"제조법 확정 — {_work.Code} {_work.Name}", question);
        if (note is null) return;

        var snapshot = new CostSnapshot(_materials.Values.ToList(), _packs.Values.ToList(), [_work], [], []);
        _repo.Confirm(_work.Id, note.Trim(), calc.KgCost, calc.AppliedDensity, snapshot.BuildConfirmSnapshot(_work));
        _state.NotifyDataChanged();
        Saved?.Invoke();
        LoadRecipe(_work.Id);
    }

    private void SetDraft()
    {
        if (_work is null || !_work.IsConfirmed) return;
        if (!Ui.Confirm(this, "이 제조법을 '미확정'으로 돌릴까요?")) return;
        _repo.SetDraft(_work.Id);
        _state.NotifyDataChanged();
        Saved?.Invoke();
        LoadRecipe(_work.Id);
    }

    private void UpdateStatusBadge()
    {
        if (_work is null) return;
        _status.Text = _work.IsConfirmed ? $"확정 {(_work.ConfirmedAt ?? "")[..Math.Min(10, (_work.ConfirmedAt ?? "").Length)]}" : "미확정";
        _status.BackColor = _work.IsConfirmed ? Ui.ConfirmedBack : Color.FromArgb(255, 235, 205);
    }

    private void MarkDirty()
    {
        if (_loading || _work is null) return;
        _dirty = true;
        _dirtyLabel.Text = "● 저장 안 됨";
        _dirtyLabel.ForeColor = Ui.WarnFore;
    }

    private void ParseMeasuredDensity()
    {
        if (_work is null) return;
        double? md = Fmt.TryParse(_measuredDensity.Text, out var v) && v > 0 ? v : null;
        if (md != _work.MeasuredDensity) { _work.MeasuredDensity = md; MarkDirty(); }
    }

    private CostCalculator Calculator() => new(id => _materials.GetValueOrDefault(id), id => _packs.GetValueOrDefault(id));

    // ================= 계산 / 표시 =================

    private void Recalc(bool rebuildSizes = false)
    {
        if (_work is null) return;
        var show = _state.ShowVatIncluded;
        var res = Calculator().CalculateRecipe(_work, show);
        var vat = Vat.ModeLabel(show);

        _lines.Columns["price"]!.HeaderText = $"단가(원/kg, {vat})";
        var lines = _work.Lines.OrderBy(l => l.SortOrder).ToList();
        _loading = true;
        for (var i = 0; i < lines.Count && i < _lines.Rows.Count; i++)
        {
            var row = _lines.Rows[i];
            var lc = res.Lines.FirstOrDefault(x => ReferenceEquals(x.Line, lines[i]));
            var m = lc?.Material;
            row.Tag = i;
            row.Cells["no"].Value = i + 1;
            row.Cells["stage"].Value = lines[i].Stage;
            row.Cells["material"].Value = m?.Name ?? (lines[i].MaterialId == 0 ? "(원료 선택)" : "(삭제된 원료)");
            row.Cells["code"].Value = m?.Code;
            row.Cells["cas"].Value = m?.Cas;
            row.Cells["pct"].Value = Fmt.Num(lines[i].Percent * 100);
            row.Cells["price"].Value = lc?.PricePerKg is { } p ? Fmt.Won(p) : (m is null ? "" : "단가 없음");
            row.Cells["price"].Style.ForeColor = lc?.PricePerKg is null ? Ui.WarnFore : (m?.CurrentPrice?.IsUncertain == true ? Ui.WarnFore : Color.Empty);
            row.Cells["price"].ToolTipText = m?.CurrentPrice is { } cp ? $"적용일 {cp.EffectiveDate} / {cp.Source}{(cp.IsUncertain ? " / 단가 확실치 않음" : "")}" : "";
            row.Cells["cost"].Value = lc?.CostPerKgProduct is { } c ? Fmt.Won(c) : "";
            row.Cells["density"].Value = m?.Density is { } d ? Fmt.Num(d) : "";
            row.Cells["memo"].Value = lines[i].Memo;
        }
        _loading = false;

        var sb = new StringBuilder();
        sb.AppendLine($"투입량 합계   {Fmt.Pct(res.TotalPercent)}  {(res.TotalIsOk ? "OK" : "← 100%가 아님!")}");
        sb.AppendLine($"kg당 원료비   {Fmt.Won(res.KgCost)} 원 ({vat})");
        sb.AppendLine($"L당 원료비    {Fmt.Won(res.LiterCost)} 원");
        sb.AppendLine();
        sb.AppendLine("[비중 단계]");
        sb.AppendLine($"계산 비중     {Fmt.Num(res.CalculatedDensity)}  (원료 비중으로 산출)");
        sb.AppendLine($"실측 비중     {(_work.MeasuredDensity is { } md ? Fmt.Num(md) : "(미입력)")}");
        sb.AppendLine($"적용 비중     {Fmt.Num(res.AppliedDensity)}  ← 충진중량·L당 계산에 사용");
        if (res.MissingDensityCount > 0) sb.AppendLine($"비중 미입력 원료 {res.MissingDensityCount}개(계산 비중에서 제외)");
        sb.AppendLine();
        if (res.MissingPriceCount > 0) sb.AppendLine($"⚠ 단가 없는 원료 {res.MissingPriceCount}개");
        if (res.UncertainPriceCount > 0) sb.AppendLine($"⚠ 단가 확실치 않음 {res.UncertainPriceCount}개");
        _summary.Text = sb.ToString();
        _summary.ForeColor = res.TotalIsOk && res.MissingPriceCount == 0 ? SystemColors.ControlText : Ui.WarnFore;

        if (rebuildSizes || _sizes.Columns.Count != _work.Sizes.Count + 1) BuildSizesGrid();
        FillSizes(res);
    }

    private static bool IsPackRow(string? key) => key is not null && SizeCostCategory.All.Contains(key);

    private void BuildSizesGrid()
    {
        _loading = true;
        _sizes.Rows.Clear();
        _sizes.Columns.Clear();
        _sizes.Columns.Add(Ui.Col("item", "항목", 150, readOnly: true));
        _sizes.Columns["item"]!.Frozen = true;
        for (var i = 0; i < _work!.Sizes.Count; i++)
        {
            var c = Ui.Col("s" + i, $"용량 {i + 1}", 110, right: true);
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            _sizes.Columns.Add(c);
        }
        var vat = _state.VatLabel;
        void AddRow(string key, string label, bool editable)
        {
            var idx = _sizes.Rows.Add(label);
            var row = _sizes.Rows[idx];
            row.Tag = key;
            row.ReadOnly = !editable;
            if (editable) for (var c = 1; c < _sizes.Columns.Count; c++) row.Cells[c].Style.BackColor = Ui.InputBack;
            else row.DefaultCellStyle.BackColor = SystemColors.Control;
        }
        AddRow(RowVolume, "용량(ml)", true);
        AddRow(RowFill, "충진중량(g)", false);
        AddRow(RowRaw, $"원액비(원/개, {vat})", false);
        foreach (var cat in SizeCostCategory.All) AddRow(cat, cat, true);
        AddRow(RowSub, "부자재·가공비 소계", false);
        AddRow(RowUnit, $"완제품 단가({vat})", false);
        AddRow(RowPerMl, "ml당 단가", false);
        _sizes.Rows.Cast<DataGridViewRow>().First(r => (string)r.Tag! == RowUnit).DefaultCellStyle.Font = new Font(_sizes.Font, FontStyle.Bold);
        _loading = false;
    }

    private void FillSizes(RecipeCostResult res)
    {
        _loading = true;
        var calc = Calculator();
        var show = _state.ShowVatIncluded;
        for (var i = 0; i < _work!.Sizes.Count; i++)
        {
            var size = _work.Sizes[i];
            var sr = res.Sizes.First(s => ReferenceEquals(s.Size, size));
            var col = i + 1;
            foreach (DataGridViewRow row in _sizes.Rows)
            {
                var key = (string)row.Tag!;
                var cell = row.Cells[col];
                cell.ToolTipText = "";
                cell.Style.ForeColor = Color.Empty;
                switch (key)
                {
                    case RowVolume: cell.Value = Fmt.Num(size.VolumeMl); break;
                    case RowFill: cell.Value = Fmt.Num(sr.FillWeightG); break;
                    case RowRaw: cell.Value = Fmt.Won(sr.RawCost); break;
                    case RowSub: cell.Value = Fmt.Won(sr.PackSubtotal); break;
                    case RowUnit: cell.Value = Fmt.Won(sr.UnitCost); break;
                    case RowPerMl: cell.Value = Fmt.Won(sr.CostPerMl); break;
                    default:
                        var cost = size.Costs.FirstOrDefault(c => c.Category == key);
                        if (cost is null) { cell.Value = ""; break; }
                        cell.Value = Fmt.Won(calc.CostLineAmount(cost, show));
                        if (cost.PackMaterialId is { } pid)
                        {
                            cell.Style.ForeColor = Ui.LinkFore;
                            var pack = _packs.GetValueOrDefault(pid);
                            cell.ToolTipText = pack is null ? "(삭제된 부자재)" : $"{pack.Code} {pack.Name} × {Fmt.Num(cost.Quantity)}" +
                                (pack.CurrentPrice is null ? " — 단가 없음" : $" (단가 적용일 {pack.CurrentPrice.EffectiveDate})");
                        }
                        if (cost.Memo.Length > 0) cell.ToolTipText = (cell.ToolTipText + "\n" + cost.Memo).Trim();
                        break;
                }
            }
        }
        _loading = false;
    }

    // ================= 배합표 편집 =================

    private RecipeLine? LineAt(int rowIndex)
    {
        var lines = _work?.Lines.OrderBy(l => l.SortOrder).ToList();
        return lines is null || rowIndex < 0 || rowIndex >= lines.Count ? null : lines[rowIndex];
    }

    private void Renumber()
    {
        for (var i = 0; i < _work!.Lines.Count; i++) _work.Lines[i].SortOrder = i;
    }

    private void AddLine()
    {
        if (_work is null) return;
        _work.Lines = _work.Lines.OrderBy(l => l.SortOrder).ToList();
        var at = _lines.CurrentCell is { } c ? c.RowIndex + 1 : _work.Lines.Count;
        _work.Lines.Insert(Math.Min(at, _work.Lines.Count), new RecipeLine());
        Renumber();
        _lines.Rows.Add();
        MarkDirty();
        Recalc();
        PickMaterial(Math.Min(at, _work.Lines.Count - 1));
    }

    private void RemoveLine()
    {
        if (_work is null || _lines.CurrentCell is null) return;
        _work.Lines = _work.Lines.OrderBy(l => l.SortOrder).ToList();
        var idx = _lines.CurrentCell.RowIndex;
        if (idx < 0 || idx >= _work.Lines.Count) return;
        _work.Lines.RemoveAt(idx);
        Renumber();
        _lines.Rows.RemoveAt(idx);
        MarkDirty();
        Recalc();
    }

    private void MoveLine(int delta)
    {
        if (_work is null || _lines.CurrentCell is null) return;
        _work.Lines = _work.Lines.OrderBy(l => l.SortOrder).ToList();
        var idx = _lines.CurrentCell.RowIndex;
        var to = idx + delta;
        if (idx < 0 || to < 0 || to >= _work.Lines.Count) return;
        (_work.Lines[idx], _work.Lines[to]) = (_work.Lines[to], _work.Lines[idx]);
        Renumber();
        MarkDirty();
        Recalc();
        _lines.CurrentCell = _lines.Rows[to].Cells[_lines.CurrentCell.ColumnIndex];
    }

    private void PickMaterial(int rowIndex)
    {
        var line = LineAt(rowIndex);
        if (line is null) return;
        using var picker = new MaterialPicker(_state);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected is null) return;
        _materials = new MaterialRepository().GetAll().ToDictionary(m => m.Id); // 방금 신규 등록한 원료 반영
        line.MaterialId = picker.Selected.Id;
        MarkDirty();
        Recalc();
        _lines.CurrentCell = _lines.Rows[rowIndex].Cells["pct"];
    }

    private void OnLineEdited(int rowIndex, int colIndex)
    {
        if (_loading) return;
        var line = LineAt(rowIndex);
        if (line is null) return;
        var cell = _lines.Rows[rowIndex].Cells[colIndex];
        switch (_lines.Columns[colIndex].Name)
        {
            case "stage": line.Stage = cell.Value?.ToString()?.Trim() ?? ""; break;
            case "memo": line.Memo = cell.Value?.ToString()?.Trim() ?? ""; break;
            case "pct":
                if (Fmt.TryParse(cell.Value?.ToString(), out var pct) && pct >= 0) line.Percent = pct / 100;
                else Ui.Warn(this, "투입량은 숫자(%)로 입력하세요.");
                break;
            default: return;
        }
        MarkDirty();
        BeginInvoke(() => Recalc()); // CellEndEdit 안에서 같은 셀 값을 바꾸면 재진입 문제가 생겨 한 박자 뒤에 다시 그린다.
    }

    // ================= 용량표 편집 =================

    private RecipeSizeCost? FindCost(int rowIndex, int colIndex)
    {
        if (_work is null || colIndex < 1 || colIndex > _work.Sizes.Count) return null;
        var key = _sizes.Rows[rowIndex].Tag as string;
        return _work.Sizes[colIndex - 1].Costs.FirstOrDefault(c => c.Category == key);
    }

    private void AddSize()
    {
        if (_work is null) return;
        var text = Ui.Prompt(this, "용량 추가", "용량(ml)을 입력하세요. 예: 300", "");
        if (text is null) return;
        if (!Fmt.TryParse(text, out var ml) || ml <= 0) { Ui.Warn(this, "용량은 0보다 큰 숫자(ml)로 입력하세요."); return; }
        _work.Sizes.Add(new RecipeSize { VolumeMl = ml, SortOrder = _work.Sizes.Count });
        MarkDirty();
        Recalc(rebuildSizes: true);
    }

    private void RemoveSize()
    {
        if (_work is null || _sizes.CurrentCell is null || _sizes.CurrentCell.ColumnIndex < 1) { Ui.Info(this, "삭제할 용량 열의 칸을 선택하세요."); return; }
        var idx = _sizes.CurrentCell.ColumnIndex - 1;
        var size = _work.Sizes[idx];
        var linked = size.Id == 0 ? 0 : new ProductRepository().GetAll().Count(p => p.RecipeSizeId == size.Id);
        var msg = $"{Fmt.Num(size.VolumeMl)}ml 열을 삭제할까요?" + (linked > 0 ? $"\n\n이 용량에 연결된 완제품 {linked}개의 연결이 풀립니다(저장 시)." : "");
        if (!Ui.Confirm(this, msg)) return;
        _work.Sizes.RemoveAt(idx);
        MarkDirty();
        Recalc(rebuildSizes: true);
    }

    private void OnSizeEdited(int rowIndex, int colIndex)
    {
        if (_loading || _work is null || colIndex < 1) return;
        var key = _sizes.Rows[rowIndex].Tag as string;
        var size = _work.Sizes[colIndex - 1];
        var text = _sizes.Rows[rowIndex].Cells[colIndex].Value?.ToString();
        if (key == RowVolume)
        {
            if (Fmt.TryParse(text, out var ml) && ml > 0) size.VolumeMl = ml;
            else Ui.Warn(this, "용량은 0보다 큰 숫자(ml)로 입력하세요.");
        }
        else if (IsPackRow(key))
        {
            var cost = size.Costs.FirstOrDefault(c => c.Category == key);
            if (string.IsNullOrWhiteSpace(text))
            {
                if (cost is not null) size.Costs.Remove(cost);
            }
            else if (Fmt.TryParse(text, out var amount))
            {
                // 직접 금액을 입력하면 부자재 연결은 풀린다(입력값 = 화면 VAT 보기 기준 → VAT포함으로 저장).
                cost ??= AddCost(size, key!);
                cost.PackMaterialId = null;
                cost.Quantity = 1;
                cost.ManualAmount = Vat.FromView(amount, _state.ShowVatIncluded);
            }
            else
            {
                Ui.Warn(this, "금액은 숫자로 입력하세요.");
            }
        }
        else return;
        MarkDirty();
        BeginInvoke(() => Recalc());
    }

    private static RecipeSizeCost AddCost(RecipeSize size, string category)
    {
        var cost = new RecipeSizeCost { Category = category, Quantity = 1 };
        size.Costs.Add(cost);
        return cost;
    }

    private void ShowSizeCellMenu(int rowIndex, int colIndex)
    {
        var key = _sizes.Rows[rowIndex].Tag as string;
        if (!IsPackRow(key) || _work is null) return;
        var size = _work.Sizes[colIndex - 1];
        var cost = size.Costs.FirstOrDefault(c => c.Category == key);
        var menu = new ContextMenuStrip();
        menu.Items.Add("부자재 목록에서 선택…", null, (_, _) =>
        {
            using var picker = new PackPicker(_state, SizeCostCategory.PackCategories.Contains(key) ? key! : SizeCostCategory.EtcPack, cost?.Quantity ?? 1);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected is null) return;
            cost ??= AddCost(size, key!);
            cost.PackMaterialId = picker.Selected.Id;
            cost.Quantity = picker.Quantity;
            cost.ManualAmount = null;
            _packs = new PackMaterialRepository().GetAll().ToDictionary(p => p.Id);
            MarkDirty();
            Recalc();
        });
        if (cost?.PackMaterialId is not null)
        {
            menu.Items.Add("연결 해제(현재 금액을 직접 입력값으로)", null, (_, _) =>
            {
                cost.ManualAmount = Vat.FromView(Calculator().CostLineAmount(cost, _state.ShowVatIncluded), _state.ShowVatIncluded);
                cost.PackMaterialId = null;
                cost.Quantity = 1;
                MarkDirty();
                Recalc();
            });
        }
        if (cost is not null)
        {
            menu.Items.Add("메모…", null, (_, _) =>
            {
                var memo = Ui.Prompt(this, "메모", $"{key} 메모", cost.Memo);
                if (memo is null) return;
                cost.Memo = memo.Trim();
                MarkDirty();
                Recalc();
            });
            menu.Items.Add("비우기", null, (_, _) => { size.Costs.Remove(cost); MarkDirty(); Recalc(); });
        }
        menu.Show(_sizes, _sizes.PointToClient(Cursor.Position));
    }

    // ================= 보조 창 =================

    private void ShowBatch()
    {
        if (_work is null) return;
        ParseMeasuredDensity();
        var batch = Fmt.TryParse(_batchKg.Text, out var b) && b > 0 ? b : _work.BatchKg;
        var sb = new StringBuilder();
        sb.AppendLine($"{_work.Code} {_work.Name} — 배치 {Fmt.Num(batch)} kg 계량표");
        sb.AppendLine(new string('-', 60));
        var n = 1;
        foreach (var line in _work.Lines.OrderBy(l => l.SortOrder))
        {
            var m = _materials.GetValueOrDefault(line.MaterialId);
            sb.AppendLine($"{n++,3}. {(m?.Name ?? "(원료 없음)"),-30} {Fmt.Pct(line.Percent),10}   {Fmt.Num(line.Percent * batch * 1000),12} g");
        }
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"합계 {Fmt.Pct(_work.Lines.Sum(l => l.Percent))}   {Fmt.Num(_work.Lines.Sum(l => l.Percent) * batch * 1000)} g");
        Ui.ShowText(this, "배치 계량표", sb.ToString());
    }

    /// <summary>전성분(함량순): 혼합 원료는 구성성분 비율로 펼치고, 같은 성분은 합산해 함량 내림차순(신제조법 오른쪽 표와 같은 방식).</summary>
    private void ShowIngredients()
    {
        if (_work is null) return;
        List<MaterialComponent> all;
        using (var c = MfgDb.Open()) all = MaterialRepository.GetAllComponents(c);
        var components = all.GroupBy(x => x.MaterialId).ToDictionary(g => g.Key, g => g.ToList());
        var totals = new Dictionary<string, double>();
        foreach (var line in _work.Lines)
        {
            var m = _materials.GetValueOrDefault(line.MaterialId);
            if (m is null) continue;
            var comps = components.GetValueOrDefault(m.Id) ?? [];
            if (comps.Count > 0)
            {
                foreach (var c in comps) totals[c.ComponentName] = totals.GetValueOrDefault(c.ComponentName) + line.Percent * c.Ratio;
            }
            else
            {
                var name = m.Inci.Length > 0 ? m.Inci : m.Name;
                totals[name] = totals.GetValueOrDefault(name) + line.Percent;
            }
        }
        var ordered = totals.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("전성분 (함량순)");
        sb.AppendLine(string.Join(", ", ordered.Select(kv => kv.Key)));
        sb.AppendLine();
        foreach (var (name, pct) in ordered) sb.AppendLine($"{Fmt.Pct(pct),12}  {name}{(pct <= 0.01 ? "  (1% 이하)" : "")}");
        sb.AppendLine();
        sb.AppendLine("※ 전성분명은 대한화장품협회 성분사전 기준으로 최종 확인하세요. 세정제(H·D)는 참고용입니다.");
        Ui.ShowText(this, "전성분", sb.ToString());
    }

    private void ShowConfirmLogs()
    {
        if (_work is null) return;
        var logs = _repo.GetConfirmLogs(_work.Id);
        if (logs.Count == 0) { Ui.Info(this, "확정 이력이 없습니다."); return; }
        var sb = new StringBuilder();
        foreach (var l in logs)
        {
            sb.AppendLine($"[{l.ConfirmedAt}] kg당 원료비 {Fmt.Won(Vat.ToView(l.KgCost, _state.ShowVatIncluded))}원({_state.VatLabel}), 적용 비중 {Fmt.Num(l.Density)}  메모: {l.Note}");
            sb.AppendLine("  스냅샷(VAT포함): " + l.SnapshotJson);
            sb.AppendLine();
        }
        Ui.ShowText(this, $"확정 이력 — {_work.Code}", sb.ToString());
    }
}

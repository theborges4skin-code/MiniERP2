using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 간이 마진 계산기(간이마진계산기_개발기획서.md). 표 형식으로 수치를 입력하면 판매가·마진·금액을
/// 상호 역산해주는 사전 시뮬레이션 도구 — 마감/이익분석(사후 실적)과는 별개다(§1).
/// M2(폼 골격) + M3(SKU 불러오기/Reserve1 파싱/VAT 환산) 범위. DB 적용(M4)/엑셀 내보내기·설정
/// 저장(M5)은 아직 없다.
/// </summary>
public class MarginCalculatorForm : Form
{
    private static readonly (string Label, MarginCalcMode Mode)[] Modes =
    [
        ("판매가 산출", MarginCalcMode.SalePrice),
        ("마진 산출", MarginCalcMode.Margin),
        ("납품가 산출", MarginCalcMode.Supply),
    ];

    private static readonly int[] RoundingUnits = [1, 10, 100, 1000];

    private readonly BindingList<MarginCostItemDef> _costItems = new();
    private readonly BindingList<MarginCalcRow> _rows = new();

    private readonly ItemRepository _itemRepository = new();
    private readonly SalesChannelRepository _channelRepository = new();
    private readonly ChannelSkuRepository _cskuRepository = new();
    private readonly MarginCalculatorSettingsService _settingsService = new();
    private readonly MarginCalculatorSettings _settings;
    private readonly MarginCalculatorScenarioService _scenarioService = new();
    private readonly SettingsService _lastFolderSettingsService = new();

    private ComboBox _modeCombo = new();
    private RadioButton _vatIncludedRadio = new();
    private RadioButton _vatExcludedRadio = new();
    private CheckBox _qtyColumnCheckBox = new();
    private ComboBox _roundingCombo = new();

    private ComboBox _presetCombo = new();
    private ExcelLikeDataGridView _costItemGrid = new();
    private Label _rateSumWarningLabel = new();

    private ExcelLikeDataGridView _mainGrid = new();

    private MarginCalcMode CurrentMode => Modes[_modeCombo.SelectedIndex].Mode;

    public MarginCalculatorForm()
    {
        _settings = _settingsService.Load();
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
        RestoreSettings();
        AddRow();
        UpdateVatConversionHeaders();
        FormClosing += OnFormClosing;
    }

    /// <summary>§8: 창 크기/위치(FormManager)·그리드 열(ExcelLikeDataGridView)은 이미 자동 복원되고,
    /// 여기서는 비용 항목 정의·VAT 토글·반올림 단위·수량열 표시 여부를 복원한다.</summary>
    private void RestoreSettings()
    {
        if (_settings.CostItems.Count > 0)
        {
            foreach (var item in _settings.CostItems) _costItems.Add(item);
        }
        else
        {
            ApplyPreset("온라인", force: true);
        }

        _vatExcludedRadio.Checked = _settings.VatExcluded;
        _vatIncludedRadio.Checked = !_settings.VatExcluded;
        if (_settings.RoundingUnitIndex >= 0 && _settings.RoundingUnitIndex < RoundingUnits.Length)
            _roundingCombo.SelectedIndex = _settings.RoundingUnitIndex;
        _qtyColumnCheckBox.Checked = _settings.QtyColumnVisible;
        ApplyQuantityColumnVisibility();
        UpdateRateSumWarning();
    }

    /// <summary>§8: 계산 시나리오 자체는 저장하지 않으므로(v1 범위 외) 닫을 때 확인한다.
    /// 설정(비용 항목/VAT/반올림/수량열)은 실제로 닫을 때만 저장한다.</summary>
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_rows.Any(r => !IsBlankManualRow(r)))
        {
            var result = MessageBox.Show(
                "저장하지 않은 계산 내용이 있습니다. 계산 시나리오는 저장되지 않고 사라집니다.\n닫으시겠습니까?",
                "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) { e.Cancel = true; return; }
        }

        _settings.CostItems = _costItems.ToList();
        _settings.VatExcluded = _vatExcludedRadio.Checked;
        _settings.RoundingUnitIndex = _roundingCombo.SelectedIndex;
        _settings.QtyColumnVisible = _qtyColumnCheckBox.Checked;
        _settingsService.Save(_settings);
    }

    private void InitializeComponent()
    {
        Text = "간이 마진 계산기";
        Size = new Size(1400, 820);
        MinimumSize = new Size(1000, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        layout.Controls.Add(CreateSettingsBar(), 0, 0);
        layout.Controls.Add(CreateCostItemPanel(), 0, 1);
        layout.Controls.Add(CreateMainGrid(), 0, 2);
        layout.Controls.Add(CreateBottomBar(), 0, 3);

        Controls.Add(layout);
    }

    // ───────────────────────── 상단: 계산 설정 바 ─────────────────────────

    private Control CreateSettingsBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 0, 0) };

        bar.Controls.Add(new Label { Text = "계산모드", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 4, 0) });
        _modeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        _modeCombo.Items.AddRange(Modes.Select(m => m.Label).ToArray());
        _modeCombo.SelectedIndex = 1; // 기본값: 마진 산출
        _modeCombo.SelectedIndexChanged += OnModeChanged;
        bar.Controls.Add(_modeCombo);

        bar.Controls.Add(new Label { Text = "   VAT:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 4, 0) });
        _vatIncludedRadio = new RadioButton { Text = "포함", AutoSize = true, Checked = true, Padding = new Padding(0, 4, 0, 0) };
        _vatExcludedRadio = new RadioButton { Text = "별도", AutoSize = true, Padding = new Padding(5, 4, 0, 0) };
        _vatIncludedRadio.CheckedChanged += (s, e) => UpdateVatConversionHeaders();
        bar.Controls.Add(_vatIncludedRadio);
        bar.Controls.Add(_vatExcludedRadio);

        _qtyColumnCheckBox = new CheckBox { Text = "수량열", AutoSize = true, Checked = true, Padding = new Padding(14, 4, 0, 0) };
        _qtyColumnCheckBox.CheckedChanged += (s, e) => ApplyQuantityColumnVisibility();
        bar.Controls.Add(_qtyColumnCheckBox);

        bar.Controls.Add(new Label { Text = "   반올림", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 4, 0) });
        _roundingCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        _roundingCombo.Items.AddRange(RoundingUnits.Select(u => $"{u}원").ToArray());
        _roundingCombo.SelectedIndex = 1; // 기본 10원
        _roundingCombo.SelectedIndexChanged += (s, e) => RecalculateAll();
        bar.Controls.Add(_roundingCombo);

        return bar;
    }

    private void OnModeChanged(object? sender, EventArgs e)
    {
        ApplyModeToColumns();

        // §3.1: 모드 C 선택 시 프리셋을 자동으로 "거래처"로 전환하되, 이미 수동 입력한 값은 유지한다.
        if (CurrentMode == MarginCalcMode.Supply)
        {
            ApplyPreset("거래처", force: false);
        }

        RecalculateAll();
    }

    // ───────────────────────── 비용 항목 설정 패널 ─────────────────────────

    private Control CreateCostItemPanel()
    {
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));

        _costItemGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            DataSource = _costItems,
        };
        BuildCostItemColumns();
        _costItemGrid.CellEndEdit += OnCostItemCellEndEdit;
        _costItemGrid.CellValueChanged += OnCostItemCellValueChanged;
        _costItemGrid.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (_costItemGrid.IsCurrentCellDirty) _costItemGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        var sidePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(6) };
        sidePanel.Controls.Add(new Label { Text = "프리셋", AutoSize = true });
        _presetCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        _presetCombo.Items.AddRange(["온라인", "거래처"]);
        _presetCombo.SelectedIndexChanged += (s, e) => ApplyPreset((string)_presetCombo.SelectedItem!, force: true);
        sidePanel.Controls.Add(_presetCombo);

        var btnAdd = new Button { Text = "+ 항목 추가", Width = 110, Margin = new Padding(0, 8, 0, 0) };
        btnAdd.Click += (s, e) =>
        {
            _costItems.Add(new MarginCostItemDef { Name = $"사용자정의{_costItems.Count + 1}", IsRate = true, Value = 0m });
            RecalculateAll();
        };
        sidePanel.Controls.Add(btnAdd);

        var btnRemove = new Button { Text = "선택 항목 삭제", Width = 110 };
        btnRemove.Click += (s, e) =>
        {
            if (_costItemGrid.CurrentRow?.DataBoundItem is not MarginCostItemDef item) return;
            _costItems.Remove(item);
            foreach (var row in _rows) row.CostOverrides.Remove(item.Id);
            RecalculateAll();
        };
        sidePanel.Controls.Add(btnRemove);

        _rateSumWarningLabel = new Label { AutoSize = true, MaximumSize = new Size(120, 0), Font = new Font(Font, FontStyle.Bold) };
        sidePanel.Controls.Add(_rateSumWarningLabel);

        outer.Controls.Add(_costItemGrid, 0, 0);
        outer.Controls.Add(sidePanel, 1, 0);
        return outer;
    }

    private void BuildCostItemColumns()
    {
        _costItemGrid.Columns.Clear();
        _costItemGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "항목명", DataPropertyName = "Name", Width = 100 });

        var methodCol = new DataGridViewComboBoxColumn { Name = "Method", HeaderText = "방식", Width = 60, FlatStyle = FlatStyle.Flat };
        methodCol.Items.AddRange("율", "정액");
        _costItemGrid.Columns.Add(methodCol);

        _costItemGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "값", Width = 70, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });

        var unitCol = new DataGridViewComboBoxColumn { Name = "Unit", HeaderText = "적용단위", Width = 70, FlatStyle = FlatStyle.Flat };
        unitCol.Items.AddRange("건당", "총액");
        _costItemGrid.Columns.Add(unitCol);

        _costItemGrid.CellFormatting += OnCostItemCellFormatting;
    }

    private void OnCostItemCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _costItemGrid.Rows.Count) return;
        if (_costItemGrid.Rows[e.RowIndex].DataBoundItem is not MarginCostItemDef item) return;
        var columnName = _costItemGrid.Columns[e.ColumnIndex].Name;

        switch (columnName)
        {
            case "Method":
                e.Value = item.IsRate ? "율" : "정액";
                e.FormattingApplied = true;
                break;
            case "Value":
                e.Value = item.IsRate ? $"{item.Value * 100:0.#}" : item.Value.ToString("0.####");
                e.FormattingApplied = true;
                break;
            case "Unit":
                e.Value = item.IsRate ? "" : (item.Unit == CostApplyUnit.PerUnit ? "건당" : "총액");
                e.FormattingApplied = true;
                if (e.CellStyle != null) e.CellStyle.ForeColor = item.IsRate ? SystemColors.GrayText : SystemColors.ControlText;
                break;
        }
    }

    private void OnCostItemCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _costItemGrid.Rows.Count) return;
        if (_costItemGrid.Rows[e.RowIndex].DataBoundItem is not MarginCostItemDef item) return;
        var columnName = _costItemGrid.Columns[e.ColumnIndex].Name;
        var raw = _costItemGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();

        switch (columnName)
        {
            case "Method":
                item.IsRate = raw == "율";
                item.IsManuallyEdited = true;
                _costItemGrid.InvalidateRow(e.RowIndex);
                RecalculateAll();
                break;
            case "Unit":
                item.Unit = raw == "총액" ? CostApplyUnit.Total : CostApplyUnit.PerUnit;
                item.IsManuallyEdited = true;
                RecalculateAll();
                break;
        }
    }

    private void OnCostItemCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _costItemGrid.Rows.Count) return;
        if (_costItemGrid.Rows[e.RowIndex].DataBoundItem is not MarginCostItemDef item) return;
        if (_costItemGrid.Columns[e.ColumnIndex].Name != "Value") return;

        var raw = _costItemGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
        if (!decimal.TryParse(raw, out var parsed))
        {
            _costItemGrid.InvalidateRow(e.RowIndex);
            return;
        }

        item.Value = item.IsRate ? parsed / 100m : parsed;
        item.IsManuallyEdited = true;
        _costItemGrid.InvalidateRow(e.RowIndex);
        RecalculateAll();
    }

    /// <summary>§3.1 비용 프리셋. force=false면 이미 수동 입력된(IsManuallyEdited) 항목은 건드리지 않는다
    /// (모드 C 진입 시 자동 전환용). force=true면 프리셋 콤보에서 직접 선택한 경우로, 전부 덮어쓴다.</summary>
    private void ApplyPreset(string presetName, bool force)
    {
        if (presetName == "온라인")
        {
            EnsurePresetItem("광고비", 0.20m, force);
            EnsurePresetItem("물류비", 0.03m, force);
            EnsurePresetItem("기타비용", 0.03m, force);
            EnsurePresetItem("수수료", 0.13m, force);
        }
        else if (presetName == "거래처")
        {
            foreach (var item in _costItems)
            {
                if (!force && item.IsManuallyEdited) continue;
                item.Value = 0m;
            }
        }

        if (!_presetCombo.Items.Contains(presetName)) return;
        _presetCombo.SelectedIndexChanged -= OnPresetComboChanged;
        _presetCombo.SelectedItem = presetName;
        _presetCombo.SelectedIndexChanged += OnPresetComboChanged;

        RecalculateAll();
    }

    private void OnPresetComboChanged(object? sender, EventArgs e) => ApplyPreset((string)_presetCombo.SelectedItem!, force: true);

    private void EnsurePresetItem(string name, decimal rateValue, bool force)
    {
        var existing = _costItems.FirstOrDefault(i => i.Name == name);
        if (existing == null)
        {
            _costItems.Add(new MarginCostItemDef { Name = name, IsRate = true, Value = rateValue });
            return;
        }

        if (force || !existing.IsManuallyEdited)
        {
            existing.IsRate = true;
            existing.Value = rateValue;
        }
    }

    private void UpdateRateSumWarning()
    {
        var sumRates = _costItems.Where(i => i.IsRate).Sum(i => i.Value);
        _rateSumWarningLabel.Text = sumRates >= 1m ? $"⚠ 비용율 합계 {sumRates:P0}\n(100% 이상)" : "";
    }

    // ───────────────────────── 메인 그리드 ─────────────────────────

    private Control CreateMainGrid()
    {
        _mainGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            PersistenceKey = "MarginCalculatorForm.MainGrid",
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            DataSource = _rows,
        };
        BuildMainGridColumns();
        _mainGrid.CellFormatting += OnMainGridCellFormatting;
        _mainGrid.CellEndEdit += OnMainGridCellEndEdit;
        _mainGrid.RowPrePaint += OnMainGridRowPrePaint;
        ApplyModeToColumns();
        ApplyQuantityColumnVisibility();
        return _mainGrid;
    }

    private void BuildMainGridColumns()
    {
        _mainGrid.Columns.Clear();
        _mainGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "ChannelName", HeaderText = "채널", DataPropertyName = "ChannelName", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "CskuCode", HeaderText = "CSKU", DataPropertyName = "CskuCode", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "Msku", HeaderText = "MSKU", DataPropertyName = "Msku", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "상품명", DataPropertyName = "ItemName", Width = 130 },
            new DataGridViewTextBoxColumn { Name = "CostPriceDb", HeaderText = "제조원가(DB)", DataPropertyName = "CostPriceDb", ReadOnly = true, Width = 90, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, ForeColor = SystemColors.GrayText } },
            new DataGridViewTextBoxColumn { Name = "CostPriceApplied", HeaderText = "제조원가(적용)", DataPropertyName = "CostPriceApplied", Width = 100, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "RetailPrice", HeaderText = "권장소비자가", DataPropertyName = "RetailPrice", Width = 100, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "SupplyPriceDb", HeaderText = "납품가(DB)", DataPropertyName = "SupplyPriceDb", ReadOnly = true, Width = 90, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, ForeColor = SystemColors.GrayText } },
            new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "수량", DataPropertyName = "Quantity", Width = 70, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "SalePrice", HeaderText = "판매가", Width = 90, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "TargetMarginRate", HeaderText = "목표마진", Width = 70, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "DiscountRate", HeaderText = "할인율", Width = 70, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } }
        );

        RebuildCostItemColumns();

        _mainGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "CostOfGoods", HeaderText = "판매원가", ReadOnly = true, Width = 90, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, BackColor = SystemColors.ControlLight } },
            new DataGridViewTextBoxColumn { Name = "ProfitAmount", HeaderText = "이익액", ReadOnly = true, Width = 90, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, BackColor = SystemColors.ControlLight } },
            new DataGridViewTextBoxColumn { Name = "MarginRate", HeaderText = "마진율", ReadOnly = true, Width = 70, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, BackColor = SystemColors.ControlLight } },
            new DataGridViewTextBoxColumn { Name = "SaleAmountTotal", HeaderText = "판매금액", ReadOnly = true, Width = 100, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, BackColor = SystemColors.ControlLight } },
            new DataGridViewTextBoxColumn { Name = "ProfitAmountTotal", HeaderText = "이익금액", ReadOnly = true, Width = 100, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, BackColor = SystemColors.ControlLight } }
        );
    }

    /// <summary>비용 항목이 추가/삭제/이름변경될 때마다 그리드의 항목별 열을 다시 만든다.
    /// "할인율" 열과 "판매원가" 열 사이에 삽입한다(§3 그리드 레이아웃).</summary>
    private void RebuildCostItemColumns()
    {
        foreach (DataGridViewColumn col in _mainGrid.Columns.Cast<DataGridViewColumn>().Where(c => c.Tag is MarginCostItemDef).ToList())
        {
            _mainGrid.Columns.Remove(col);
        }

        var insertAt = _mainGrid.Columns["DiscountRate"]!.Index + 1;
        foreach (var item in _costItems)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name = $"Cost_{item.Id}",
                HeaderText = item.Name,
                Tag = item,
                Width = 70,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight },
            };
            _mainGrid.Columns.Insert(insertAt, col);
            insertAt++;
        }
    }

    private void ApplyQuantityColumnVisibility()
    {
        var show = _qtyColumnCheckBox.Checked;
        if (_mainGrid.Columns["Quantity"] is { } qty) qty.Visible = show;
        if (_mainGrid.Columns["SaleAmountTotal"] is { } sale) sale.Visible = show;
        if (_mainGrid.Columns["ProfitAmountTotal"] is { } profit) profit.Visible = show;
    }

    /// <summary>모드에 따라 목표마진/판매가/할인율 열의 표시·편집 가능 여부를 바꾼다(§3.1).</summary>
    private void ApplyModeToColumns()
    {
        var mode = CurrentMode;
        if (_mainGrid.Columns["TargetMarginRate"] is { } target) target.Visible = mode == MarginCalcMode.SalePrice;
        if (_mainGrid.Columns["DiscountRate"] is { } discount) discount.Visible = mode == MarginCalcMode.Supply;
        if (_mainGrid.Columns["SalePrice"] is { } sale) sale.ReadOnly = mode == MarginCalcMode.SalePrice;
        _roundingCombo.Enabled = mode == MarginCalcMode.SalePrice;
    }

    private void OnMainGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _mainGrid.Rows.Count) return;
        if (_mainGrid.Rows[e.RowIndex].DataBoundItem is not MarginCalcRow row) return;
        var column = _mainGrid.Columns[e.ColumnIndex];

        if (column.Name == "CostPriceApplied")
        {
            if (e.CellStyle != null && row.CostPriceApplied != row.CostPriceDb) e.CellStyle.Font = new Font(_mainGrid.Font, FontStyle.Bold);
            e.Value = row.CostPriceApplied.ToString("N0") + (row.CostPriceApplied != row.CostPriceDb ? " *" : "");
            e.FormattingApplied = true;
            return;
        }

        if (column.Name == "SalePrice")
        {
            if (!row.IsComputable) { e.Value = "산출불가"; e.FormattingApplied = true; return; }
            e.Value = row.SalePrice?.ToString("N0") ?? "";
            e.FormattingApplied = true;
            if (e.CellStyle != null && CurrentMode == MarginCalcMode.Supply && row.SupplyInputSource == MarginSupplyInputSource.Discount)
                e.CellStyle.ForeColor = SystemColors.GrayText;
            return;
        }

        if (column.Name == "DiscountRate")
        {
            if (!row.IsComputable) { e.Value = ""; e.FormattingApplied = true; return; }
            e.Value = row.DiscountRate is { } d ? $"{d * 100:0.#}" : "";
            e.FormattingApplied = true;
            if (e.CellStyle != null && row.SupplyInputSource == MarginSupplyInputSource.Price)
                e.CellStyle.ForeColor = SystemColors.GrayText;
            return;
        }

        if (column.Name == "TargetMarginRate")
        {
            e.Value = row.TargetMarginRate is { } m ? $"{m * 100:0.#}" : "";
            e.FormattingApplied = true;
            return;
        }

        if (column.Tag is MarginCostItemDef costItem)
        {
            var overrideVal = row.CostOverrides.GetValueOrDefault(costItem.Id);
            if (overrideVal is { } v)
            {
                e.Value = costItem.IsRate ? $"{v * 100:0.#}" : v.ToString("0.####");
                if (e.CellStyle != null) e.CellStyle.Font = new Font(_mainGrid.Font, FontStyle.Bold);
            }
            else
            {
                e.Value = "";
                if (e.CellStyle != null) e.CellStyle.ForeColor = SystemColors.GrayText;
            }
            e.FormattingApplied = true;
            return;
        }

        if (!row.IsComputable && column.Name is "CostOfGoods" or "ProfitAmount" or "MarginRate" or "SaleAmountTotal" or "ProfitAmountTotal")
        {
            e.Value = row.ComputeReason ?? "";
            e.FormattingApplied = true;
            return;
        }

        switch (column.Name)
        {
            case "CostOfGoods": e.Value = row.CostOfGoods?.ToString("N0") ?? ""; e.FormattingApplied = true; break;
            case "ProfitAmount": e.Value = row.ProfitAmount?.ToString("N0") ?? ""; e.FormattingApplied = true; break;
            case "MarginRate": e.Value = row.MarginRate is { } mr ? $"{mr:P1}" : ""; e.FormattingApplied = true; break;
            case "SaleAmountTotal": e.Value = row.SaleAmountTotal?.ToString("N0") ?? ""; e.FormattingApplied = true; break;
            case "ProfitAmountTotal": e.Value = row.ProfitAmountTotal?.ToString("N0") ?? ""; e.FormattingApplied = true; break;
        }
    }

    /// <summary>§3: 역마진(이익액 &lt; 0) 행 배경 강조. SystemColors 기반(하드코딩 금지).</summary>
    private void OnMainGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _mainGrid.Rows.Count) return;
        if (_mainGrid.Rows[e.RowIndex].DataBoundItem is not MarginCalcRow row) return;

        _mainGrid.Rows[e.RowIndex].DefaultCellStyle.BackColor =
            row.IsComputable && row.ProfitAmount is < 0 ? SystemColors.Info : _mainGrid.DefaultCellStyle.BackColor;
    }

    private void OnMainGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _mainGrid.Rows.Count) return;
        if (_mainGrid.Rows[e.RowIndex].DataBoundItem is not MarginCalcRow row) return;
        var column = _mainGrid.Columns[e.ColumnIndex];
        var raw = _mainGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();

        switch (column.Name)
        {
            case "SalePrice":
                row.SalePrice = decimal.TryParse(raw, out var p) ? p : null;
                if (CurrentMode == MarginCalcMode.Supply) row.SupplyInputSource = MarginSupplyInputSource.Price;
                break;
            case "TargetMarginRate":
                row.TargetMarginRate = decimal.TryParse(raw, out var m) ? m / 100m : null;
                break;
            case "DiscountRate":
                row.DiscountRate = decimal.TryParse(raw, out var d) ? d / 100m : null;
                row.SupplyInputSource = MarginSupplyInputSource.Discount;
                break;
            default:
                if (column.Tag is MarginCostItemDef costItem)
                {
                    if (string.IsNullOrWhiteSpace(raw)) { row.CostOverrides.Remove(costItem.Id); break; }
                    if (!decimal.TryParse(raw, out var v)) break;
                    row.CostOverrides[costItem.Id] = costItem.IsRate ? v / 100m : v;
                }
                break;
        }

        RecalculateRow(row);
        _mainGrid.InvalidateRow(e.RowIndex);
    }

    // ───────────────────────── 계산 ─────────────────────────

    private void RecalculateAll()
    {
        UpdateRateSumWarning();
        foreach (var row in _rows) RecalculateRow(row);
        _mainGrid.Invalidate();
    }

    private void RecalculateRow(MarginCalcRow row)
    {
        var mode = CurrentMode;
        var costItems = _costItems.Select(item => new MarginCostItemInput
        {
            Name = item.Name,
            IsRate = item.IsRate,
            Value = row.CostOverrides.TryGetValue(item.Id, out var v) && v.HasValue ? v.Value : item.Value,
            Unit = item.Unit,
        }).ToArray();

        var input = new MarginCalcInput
        {
            Mode = mode,
            CostPrice = row.CostPriceApplied,
            CostItems = costItems,
            Quantity = row.Quantity,
            TargetMarginRate = row.TargetMarginRate,
            RoundingUnit = RoundingUnits[_roundingCombo.SelectedIndex < 0 ? 1 : _roundingCombo.SelectedIndex],
            SalePrice = row.SalePrice,
            RetailPrice = row.RetailPrice,
            DiscountRate = row.SupplyInputSource == MarginSupplyInputSource.Discount ? row.DiscountRate : null,
            SupplyPriceInput = row.SupplyInputSource == MarginSupplyInputSource.Price ? row.SalePrice : null,
        };

        var result = MarginCalculator.Calculate(input);

        row.IsComputable = result.IsComputable;
        row.ComputeReason = result.Reason;
        if (mode != MarginCalcMode.Margin) row.SalePrice = result.SalePrice ?? row.SalePrice;
        if (mode == MarginCalcMode.Supply) row.DiscountRate = result.DiscountRate ?? row.DiscountRate;
        row.CostOfGoods = result.CostOfGoods;
        row.ProfitAmount = result.ProfitAmount;
        row.MarginRate = result.MarginRate;
        row.SaleAmountTotal = result.SaleAmountTotal;
        row.ProfitAmountTotal = result.ProfitAmountTotal;
    }

    // ───────────────────────── 하단 버튼 바 ─────────────────────────

    private Control CreateBottomBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6) };

        var btnLoadSku = new Button { Text = "SKU 불러오기", Width = 110 };
        btnLoadSku.Click += (s, e) => OnLoadSkuClick();
        bar.Controls.Add(btnLoadSku);

        var btnAddRow = new Button { Text = "행 추가", Width = 90 };
        btnAddRow.Click += (s, e) => AddRow();
        bar.Controls.Add(btnAddRow);

        var btnRemoveRow = new Button { Text = "행 삭제", Width = 90 };
        btnRemoveRow.Click += (s, e) =>
        {
            var selected = _mainGrid.SelectedRows.Cast<DataGridViewRow>()
                .Select(r => r.DataBoundItem as MarginCalcRow).Where(r => r != null).ToList();
            foreach (var row in selected) _rows.Remove(row!);
        };
        bar.Controls.Add(btnRemoveRow);

        var applyMenu = new ContextMenuStrip();
        applyMenu.Items.Add("제조원가 적용", null, (s, e) => OnApplyCostPriceClick());
        applyMenu.Items.Add("판매가/납품가 적용", null, (s, e) => OnApplyQuoteClick());
        applyMenu.Items.Add("권장소비자가 적용", null, (s, e) => OnApplyRetailPriceClick());
        var btnApply = new Button { Text = "DB 적용 ▼", Width = 100 };
        btnApply.Click += (s, e) => applyMenu.Show(btnApply, new Point(0, btnApply.Height));
        bar.Controls.Add(btnApply);

        var newSkuMenu = new ContextMenuStrip();
        newSkuMenu.Items.Add("마스터SKU 추가", null, (s, e) => OnNewMasterSkuClick());
        newSkuMenu.Items.Add("CSKU 추가", null, (s, e) => OnNewCskuClick());
        var btnNewSku = new Button { Text = "신규 SKU 등록 ▼", Width = 120 };
        btnNewSku.Click += (s, e) => newSkuMenu.Show(btnNewSku, new Point(0, btnNewSku.Height));
        bar.Controls.Add(btnNewSku);

        var btnExport = new Button { Text = "엑셀 내보내기", Width = 110 };
        btnExport.Click += (s, e) => OnExportClick();
        bar.Controls.Add(btnExport);

        var btnSaveScenario = new Button { Text = "임시저장", Width = 90 };
        btnSaveScenario.Click += (s, e) => OnSaveScenarioClick();
        bar.Controls.Add(btnSaveScenario);

        var btnLoadScenario = new Button { Text = "임시저장 불러오기", Width = 130 };
        btnLoadScenario.Click += (s, e) => OnLoadScenarioClick();
        bar.Controls.Add(btnLoadScenario);

        return bar;
    }

    // ───────────────────────── 엑셀 내보내기 (§8) ─────────────────────────

    private void OnExportClick()
    {
        if (_rows.Count == 0) { MessageBox.Show("내보낼 데이터가 없습니다.", "알림"); return; }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"간이마진계산_{DateTime.Now:yyyyMMdd}.xlsx",
            _lastFolderSettingsService.GetLastFolder("MarginCalculatorExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;

        _lastFolderSettingsService.SetLastFolder("MarginCalculatorExport", Path.GetDirectoryName(filePath)!);

        try
        {
            ExcelLicense.Ensure();

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("간이마진계산");

            var visibleColumns = _mainGrid.Columns.Cast<DataGridViewColumn>()
                .Where(c => c.Visible)
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            for (var i = 0; i < visibleColumns.Count; i++)
                worksheet.Cells[1, i + 1].Value = visibleColumns[i].HeaderText;

            for (var rowIndex = 0; rowIndex < _mainGrid.Rows.Count; rowIndex++)
            {
                for (var colIndex = 0; colIndex < visibleColumns.Count; colIndex++)
                {
                    // 그리드에 실제로 표시 중인 문자열을 그대로 내보낸다(계산결과/산출불가 표시 등
                    // CellFormatting에서 만든 값과 화면이 항상 일치하도록).
                    var cell = _mainGrid.Rows[rowIndex].Cells[visibleColumns[colIndex].Index];
                    worksheet.Cells[rowIndex + 2, colIndex + 1].Value = cell.FormattedValue?.ToString() ?? "";
                }
            }

            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            ExportHelper.SaveExcel(package, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀 내보내기 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private List<MarginCalcRow> GetSelectedRows() =>
        _mainGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as MarginCalcRow).Where(r => r != null).Cast<MarginCalcRow>()
            .Reverse().ToList();

    private string ResolveChannelName(string channelCode) =>
        _channelRepository.GetAll().FirstOrDefault(c => c.ChannelCode == channelCode)?.ChannelName ?? channelCode;

    // ───────────────────────── DB 적용 (§6) ─────────────────────────

    /// <summary>§6.1 제조원가 적용 — 마스터/CSKU 중 하나를 반드시 선택, 일괄 확인 다이얼로그 후 적용.</summary>
    private void OnApplyCostPriceClick()
    {
        var selected = GetSelectedRows();
        if (selected.Count == 0) { MessageBox.Show("적용할 행을 선택하세요.", "알림"); return; }

        using var targetDialog = new MarginCostApplyTargetDialog();
        if (FormManager.ShowDialogSafe(targetDialog, this) != DialogResult.OK || targetDialog.SelectedTarget == null) return;
        var target = targetDialog.SelectedTarget.Value;

        if (target == MarginCostApplyTargetDialog.Target.Csku)
        {
            selected = selected.Where(r => r.ChannelCode != null && r.CskuCode != null).ToList();
            if (selected.Count == 0) { MessageBox.Show("CSKU 개별원가 적용 대상은 CSKU가 있는 행만 가능합니다.", "알림"); return; }
        }
        else
        {
            selected = selected.Where(r => r.Msku != null).ToList();
            if (selected.Count == 0) { MessageBox.Show("MSKU가 있는 행만 마스터 원가 적용이 가능합니다.", "알림"); return; }
        }

        // §4.6.1: ItemTable.CostPrice/CostPriceOverride 모두 VAT포함 고정으로 저장한다(확정).
        var preview = new List<(string Label, string Before, string After)>();
        foreach (var row in selected)
        {
            var newDbValue = ConvertToStorageBasis(row.CostPriceApplied, storageIsVatExcl: false);

            var beforeText = target == MarginCostApplyTargetDialog.Target.Master
                ? _itemRepository.GetBySku(row.Msku!)?.CostPrice.ToString("N0") ?? "-"
                : GetCurrentCskuCostOverride(row)?.ToString("N0") ?? "(연동)";

            var label = target == MarginCostApplyTargetDialog.Target.Master ? $"{row.Msku} {row.ItemName}" : $"{row.ChannelCode} {row.CskuCode}";
            preview.Add((label, beforeText, newDbValue.ToString("N0")));
        }

        var warningText = target == MarginCostApplyTargetDialog.Target.Master
            ? "마스터 대표원가를 적용하면 해당 SKU를 쓰는 전 채널의 원가가 함께 바뀝니다."
            : "선택한 CSKU의 개별원가만 바뀝니다.";

        if (!MarginApplyConfirmDialog.Confirm(this, "제조원가 적용 확인", warningText, preview)) return;

        foreach (var row in selected)
        {
            if (target == MarginCostApplyTargetDialog.Target.Master)
            {
                var item = _itemRepository.GetBySku(row.Msku!);
                if (item == null) continue;
                item.CostPrice = ConvertToStorageBasis(row.CostPriceApplied, storageIsVatExcl: false);
                _itemRepository.Upsert(item);
            }
            else
            {
                var csku = _cskuRepository.GetByChannelAndCskuCode(row.ChannelCode!, row.CskuCode!);
                if (csku == null) continue;
                csku.CostPriceOverride = ConvertToStorageBasis(row.CostPriceApplied, storageIsVatExcl: false);
                _cskuRepository.Upsert(csku);
            }
        }

        MessageBox.Show($"{selected.Count}건 적용 완료.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private decimal? GetCurrentCskuCostOverride(MarginCalcRow row) =>
        row.ChannelCode != null && row.CskuCode != null
            ? _cskuRepository.GetByChannelAndCskuCode(row.ChannelCode, row.CskuCode)?.CostPriceOverride
            : null;

    /// <summary>표시 중인 값(현재 VAT 토글 기준)을 DB 저장 기준(<paramref name="storageIsVatExcl"/>)으로
    /// 역환산한다(§4.6). <see cref="ConvertBasis"/>의 반대 방향 — 같은 2단(포함/별도) 변환식이라
    /// ConvertBasis(value, toIsVatExcl: storageIsVatExcl)로 표현할 수 있다.</summary>
    private decimal ConvertToStorageBasis(decimal displayValue, bool storageIsVatExcl) =>
        ConvertBasis(displayValue, sourceIsVatExcl: _vatExcludedRadio.Checked, toIsVatExcl: storageIsVatExcl);

    /// <summary>§6.3 권장소비자가 적용 — Reserve1이 공란이거나 숫자로 파싱되는 행만 허용.</summary>
    private void OnApplyRetailPriceClick()
    {
        var selected = GetSelectedRows().Where(r => r.Msku != null && r.RetailPrice.HasValue).ToList();
        if (selected.Count == 0) { MessageBox.Show("권장소비자가가 입력된 행을 선택하세요.", "알림"); return; }

        var eligible = new List<MarginCalcRow>();
        var excludedNames = new List<string>();
        foreach (var row in selected)
        {
            var item = _itemRepository.GetBySku(row.Msku!);
            if (item == null) continue;
            var isEligible = string.IsNullOrWhiteSpace(item.Reserve1) || ReserveTextPriceParser.Parse(item.Reserve1) != null;
            if (isEligible) eligible.Add(row);
            else excludedNames.Add($"{row.Msku} {row.ItemName}");
        }

        if (excludedNames.Count > 0)
        {
            MessageBox.Show(
                "다음 SKU는 권장소비자가(Reserve1)에 규격 등 비숫자 값이 들어있어 적용 대상에서 제외합니다:\n" + string.Join("\n", excludedNames),
                "일부 제외", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        if (eligible.Count == 0) return;

        var preview = eligible.Select(row =>
        {
            var item = _itemRepository.GetBySku(row.Msku!)!;
            var newDbValue = ConvertToStorageBasis(row.RetailPrice!.Value, storageIsVatExcl: false); // Reserve1은 VAT포함 고정(§4.6)
            return ($"{row.Msku} {row.ItemName}", item.Reserve1 ?? "(공란)", newDbValue.ToString("N0"));
        }).ToList();

        if (!MarginApplyConfirmDialog.Confirm(this, "권장소비자가 적용 확인", "선택한 SKU의 권장소비자가(Reserve1)를 아래 값으로 덮어씁니다.", preview)) return;

        foreach (var row in eligible)
        {
            var item = _itemRepository.GetBySku(row.Msku!)!;
            item.Reserve1 = ConvertToStorageBasis(row.RetailPrice!.Value, storageIsVatExcl: false).ToString("0.####");
            _itemRepository.Upsert(item);
        }

        MessageBox.Show($"{eligible.Count}건 적용 완료.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>§6.2 판매가/납품가 적용 — 목록창(채널별 그룹) → 편집창 순차 처리(결정사항 2).</summary>
    private void OnApplyQuoteClick()
    {
        var selected = GetSelectedRows().Where(r => r.ChannelCode != null && r.CskuCode != null).ToList();
        if (selected.Count == 0) { MessageBox.Show("적용할 행을 선택하세요(채널·CSKU가 있는 행만 가능).", "알림"); return; }

        using var listForm = new MarginQuoteApplyListForm(selected, ResolveChannelName,
            toDbBasis: v => ConvertToStorageBasis(v, storageIsVatExcl: false)); // SupplyPrice는 VAT포함 고정(§4.6)
        FormManager.ShowDialogSafe(listForm, this);

        if (listForm.AnyApplied)
        {
            var open = MessageBox.Show("견적·단가 관리 창을 열어 확인하시겠습니까?", "완료", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (open == DialogResult.Yes) FormManager.Show<PriceQuoteForm>();
        }
    }

    // ───────────────────────── 신규 SKU 등록 연결 (§6.4) ─────────────────────────

    private void OnNewMasterSkuClick()
    {
        var current = _mainGrid.CurrentRow?.DataBoundItem as MarginCalcRow;
        using var dialog = new NewMasterSkuDialog(current?.ItemName, current?.CostPriceApplied);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK || dialog.ResultSku == null) return;

        var row = current != null && IsBlankManualRow(current) ? current : new MarginCalcRow();
        if (!_rows.Contains(row)) _rows.Add(row);
        row.Msku = dialog.ResultSku;
        row.ItemName = dialog.ResultItemName;
        row.CostPriceDb = _itemRepository.GetBySku(dialog.ResultSku)?.CostPrice ?? 0m;
        row.CostPriceApplied = row.CostPriceDb;
        RecalculateRow(row);
        _mainGrid.Invalidate();
    }

    private void OnNewCskuClick()
    {
        var current = _mainGrid.CurrentRow?.DataBoundItem as MarginCalcRow;
        using var dialog = new NewCskuRegistrationDialog(current?.ChannelCode, current?.ItemName);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK || dialog.ResultCskuCode == null) return;

        var row = current != null && IsBlankManualRow(current) ? current : new MarginCalcRow();
        if (!_rows.Contains(row)) _rows.Add(row);
        row.ChannelCode = dialog.ResultChannelCode;
        row.ChannelName = dialog.ResultChannelCode == null ? null : ResolveChannelName(dialog.ResultChannelCode);
        row.CskuCode = dialog.ResultCskuCode;
        row.Msku = dialog.ResultMsku;
        row.ItemName = dialog.ResultItemName;
        row.CostPriceDb = dialog.ResultCostPrice;
        row.CostPriceApplied = row.CostPriceDb;
        row.SupplyPriceDb = dialog.ResultUnitPrice == 0m ? null : dialog.ResultUnitPrice;
        RecalculateRow(row);
        _mainGrid.Invalidate();
    }

    // ───────────────────────── SKU 불러오기 (§5) ─────────────────────────

    /// <summary>§5: CskuPickerDialog(다중선택 가능, allowMultiSelect=true) — 채널 미지정 마스터SKU
    /// 단독 행도 허용(§5 전제조건, allowMasterSkuOnly=true로 다이얼로그 확장분 사용). 선택한 항목 수만큼
    /// 그리드에 행을 채워 넣는다(단건 선택 시에도 SelectedItems에 1개만 담겨 동일 경로로 처리됨).</summary>
    private void OnLoadSkuClick()
    {
        using var dialog = new CskuPickerDialog(allowMasterSkuOnly: true, allowMultiSelect: true);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK || dialog.SelectedItems.Count == 0) return;

        foreach (var selected in dialog.SelectedItems)
        {
            // 첫 로딩이라 아직 아무것도 입력하지 않은 기본 빈 행이 있으면 그 행을 채우고,
            // 아니면 새 행을 추가한다(§5 — 반복 호출/다중선택으로 다행 구성).
            var row = _rows.Count == 1 && IsBlankManualRow(_rows[0]) ? _rows[0] : new MarginCalcRow();
            if (!_rows.Contains(row)) _rows.Add(row);

            row.ChannelCode = selected.ChannelCode;
            row.ChannelName = selected.ChannelCode == null
                ? null
                : _channelRepository.GetAll().FirstOrDefault(c => c.ChannelCode == selected.ChannelCode)?.ChannelName ?? selected.ChannelCode;
            row.CskuCode = selected.CskuCode;
            row.Msku = selected.Msku;
            row.ItemName = selected.ItemName;

            // §4.6.1: ItemTable.CostPrice(마스터)/CostPriceOverride 모두 VAT포함 고정으로 저장된다(확정).
            // CskuPickerDialog가 둘을 이미 한 값으로 합쳐 반환하므로 출처를 구분할 필요 없이 그대로 환산한다.
            row.CostPriceDb = ConvertFromVatIncludedDb(selected.CostPrice);
            row.CostPriceApplied = row.CostPriceDb;

            // §5.1: Reserve1 파싱. §4.6: DB는 VAT포함 고정 — 표시기준이 별도면 환산.
            var item = selected.Msku == null ? null : _itemRepository.GetBySku(selected.Msku);
            var retailPriceRaw = ReserveTextPriceParser.Parse(item?.Reserve1);
            row.RetailPrice = retailPriceRaw is { } r ? ConvertFromVatIncludedDb(r) : null;

            // §5.2: SupplyPrice는 NOT NULL이라 0은 "미설정"으로 간주해 공란 처리.
            var supplyPriceRaw = selected.ChannelCode == null || selected.UnitPrice == 0m ? (decimal?)null : selected.UnitPrice;
            row.SupplyPriceDb = supplyPriceRaw is { } sp ? ConvertFromVatIncludedDb(sp) : null;

            RecalculateRow(row);
        }

        _mainGrid.Invalidate();
    }

    // ───────────────────────── 임시저장 (§9) ─────────────────────────

    /// <summary>§9: 현재 화면(비용 항목/행/계산 설정) 전체를 스냅샷으로 저장한다. 네고 문의 등으로
    /// 같은 계산을 반복할 때 재세팅 없이 다시 불러오도록 하기 위함 — 최근 5개까지만 보관된다
    /// (MarginCalculatorScenarioService, 6번째부터 가장 오래된 것을 자동으로 버림).</summary>
    private void OnSaveScenarioClick()
    {
        if (_rows.All(IsBlankManualRow)) { MessageBox.Show("저장할 계산 내용이 없습니다.", "알림"); return; }

        var defaultLabel = BuildScenarioDefaultLabel();
        using var promptDialog = new SimpleTextPromptDialog("임시저장", "저장 이름", defaultLabel);
        if (FormManager.ShowDialogSafe(promptDialog, this) != DialogResult.OK) return;

        var scenario = new MarginCalcScenario
        {
            Label = string.IsNullOrWhiteSpace(promptDialog.Value) ? defaultLabel : promptDialog.Value,
            Mode = CurrentMode,
            VatExcluded = _vatExcludedRadio.Checked,
            RoundingUnitIndex = _roundingCombo.SelectedIndex,
            QtyColumnVisible = _qtyColumnCheckBox.Checked,
            CostItems = _costItems.ToList(),
            Rows = _rows.Where(r => !IsBlankManualRow(r)).ToList(),
        };

        var all = _scenarioService.Save(scenario);
        MessageBox.Show($"임시저장했습니다. ({all.Count}/5)\n5개를 넘으면 가장 오래된 저장이 자동으로 지워집니다.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private string BuildScenarioDefaultLabel()
    {
        var names = _rows.Where(r => !string.IsNullOrWhiteSpace(r.ItemName)).Select(r => r.ItemName!).Distinct().ToList();
        if (names.Count == 0) return $"임시저장 {DateTime.Now:MM-dd HH:mm}";
        var summary = string.Join(", ", names.Take(2));
        return names.Count > 2 ? $"{summary} 외 {names.Count - 2}건" : summary;
    }

    /// <summary>§9: 임시저장 목록에서 하나를 골라 현재 화면을 통째로 덮어쓴다.</summary>
    private void OnLoadScenarioClick()
    {
        using var pickerDialog = new MarginScenarioPickerDialog(_scenarioService);
        if (FormManager.ShowDialogSafe(pickerDialog, this) != DialogResult.OK || pickerDialog.SelectedScenario == null) return;

        if (_rows.Any(r => !IsBlankManualRow(r)))
        {
            var result = MessageBox.Show(
                "현재 작성 중인 계산 내용이 있습니다. 불러오면 사라집니다.\n계속하시겠습니까?",
                "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
        }

        ApplyScenario(pickerDialog.SelectedScenario);
    }

    /// <summary>저장된 스냅샷으로 화면 전체(모드/VAT/반올림/수량열/비용항목/행)를 되돌린다.</summary>
    private void ApplyScenario(MarginCalcScenario scenario)
    {
        var modeIndex = Array.FindIndex(Modes, m => m.Mode == scenario.Mode);
        if (modeIndex >= 0) _modeCombo.SelectedIndex = modeIndex;

        _vatExcludedRadio.Checked = scenario.VatExcluded;
        _vatIncludedRadio.Checked = !scenario.VatExcluded;
        if (scenario.RoundingUnitIndex >= 0 && scenario.RoundingUnitIndex < RoundingUnits.Length)
            _roundingCombo.SelectedIndex = scenario.RoundingUnitIndex;
        _qtyColumnCheckBox.Checked = scenario.QtyColumnVisible;
        ApplyQuantityColumnVisibility();

        _costItems.Clear();
        foreach (var item in scenario.CostItems) _costItems.Add(item);
        RebuildCostItemColumns();
        UpdateRateSumWarning();

        _rows.Clear();
        foreach (var row in scenario.Rows) _rows.Add(row);
        if (_rows.Count == 0) AddRow();

        ApplyModeToColumns();
        UpdateVatConversionHeaders();
        RecalculateAll();
    }

    private static bool IsBlankManualRow(MarginCalcRow row) =>
        row.Msku == null && row.CostPriceApplied == 0m && row.SalePrice == null && row.Quantity == null;

    /// <summary>DB가 VAT포함 고정으로 저장하는 필드(권장소비자가/납품가, §4.6)를 현재 표시기준으로 환산.</summary>
    private decimal ConvertFromVatIncludedDb(decimal vatIncludedValue) =>
        ConvertBasis(vatIncludedValue, sourceIsVatExcl: false, toIsVatExcl: _vatExcludedRadio.Checked);

    /// <summary>VAT 포함/별도 2단 변환의 유일한 실제 구현. sourceIsVatExcl 기준의 값을
    /// toIsVatExcl 기준으로 바꾼다(§4.6). 로딩(DB→표시)·적용(표시→DB) 양방향에서 재사용한다.</summary>
    private static decimal ConvertBasis(decimal value, bool sourceIsVatExcl, bool toIsVatExcl)
    {
        if (sourceIsVatExcl == toIsVatExcl) return value;
        return sourceIsVatExcl ? value * VatCalculator.VatDivisor : value / VatCalculator.VatDivisor;
    }

    /// <summary>환산이 발생하는 열 헤더에 "(환산)" 표시를 붙인다(§4.6). DB 필드(제조원가/권장소비자가/납품가)는
    /// 모두 VAT포함 고정 저장이므로, 화면 표시기준이 "별도"일 때만 환산이 일어난다.</summary>
    private void UpdateVatConversionHeaders()
    {
        var displayIsVatExcl = _vatExcludedRadio.Checked;

        SetHeaderSuffix("CostPriceDb", "제조원가(DB)", displayIsVatExcl);
        SetHeaderSuffix("CostPriceApplied", "제조원가(적용)", displayIsVatExcl);
        SetHeaderSuffix("RetailPrice", "권장소비자가", displayIsVatExcl);
        SetHeaderSuffix("SupplyPriceDb", "납품가(DB)", displayIsVatExcl);
    }

    private void SetHeaderSuffix(string columnName, string baseText, bool showSuffix)
    {
        if (_mainGrid.Columns[columnName] is { } col) col.HeaderText = showSuffix ? $"{baseText} (환산)" : baseText;
    }

    private void AddRow()
    {
        var row = new MarginCalcRow { CostPriceApplied = 0m };
        _rows.Add(row);
        RecalculateRow(row);
    }
}

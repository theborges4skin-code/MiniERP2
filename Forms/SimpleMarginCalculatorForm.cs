using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 정산 마진 계산기 — "간이 마진 계산기"(<see cref="MarginCalculatorForm"/>)보다 더 단순한 계산기.
/// 비용 항목/모드 없이, CSKU를 불러오거나 직접 입력해 제조원가를 조회하고 수량·판매금액·정산금액·
/// 수수료만 입력하면 이익액을 계산한다(<see cref="SimpleMarginCalculator"/>).
/// </summary>
public class SimpleMarginCalculatorForm : Form
{
    private readonly BindingList<SimpleMarginCalcRow> _rows = new();

    private readonly ItemRepository _itemRepository = new();
    private readonly SalesChannelRepository _channelRepository = new();
    private readonly ChannelSkuRepository _cskuRepository = new();
    private readonly SimpleMarginCalculatorScenarioService _scenarioService = new();

    private ExcelLikeDataGridView _mainGrid = new();
    private Label _totalLabel = new();

    public SimpleMarginCalculatorForm()
    {
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
        AddRow();
        FormClosing += OnFormClosing;
    }

    /// <summary>임시저장하지 않은 계산 내용이 있으면 닫기 전에 확인한다(간이 마진 계산기와 동일).</summary>
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_rows.Any(r => !IsBlankRow(r)))
        {
            var result = MessageBox.Show(
                "저장하지 않은 계산 내용이 있습니다. 임시저장하지 않으면 사라집니다.\n닫으시겠습니까?",
                "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) e.Cancel = true;
        }
    }

    private void InitializeComponent()
    {
        Text = "정산 마진 계산기";
        Size = new Size(1100, 650);
        MinimumSize = new Size(800, 450);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        layout.Controls.Add(CreateMainGrid(), 0, 0);
        layout.Controls.Add(CreateBottomBar(), 0, 1);

        Controls.Add(layout);
    }

    // ───────────────────────── 메인 그리드 ─────────────────────────

    private Control CreateMainGrid()
    {
        _mainGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            PersistenceKey = "SimpleMarginCalculatorForm.MainGrid",
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            DataSource = _rows,
        };
        BuildMainGridColumns();
        _mainGrid.CellFormatting += OnMainGridCellFormatting;
        _mainGrid.CellEndEdit += OnMainGridCellEndEdit;
        _mainGrid.RowPrePaint += OnMainGridRowPrePaint;
        _mainGrid.CellPainting += OnMainGridCellPainting;
        return _mainGrid;
    }

    /// <summary>입력 칸(수량~수수료)과 계산 결과 칸(마진율~1개당 판매금액) 사이에 굵은 세로선을
    /// 그어 입력/결과 영역을 시각적으로 구분한다.</summary>
    private void OnMainGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.ColumnIndex < 0 || _mainGrid.Columns[e.ColumnIndex].Name != "MarginRate") return;

        e.Paint(e.CellBounds, e.PaintParts);
        using var pen = new Pen(SystemColors.ControlDarkDark, 2);
        e.Graphics!.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Left, e.CellBounds.Bottom);
        e.Handled = true;
    }

    private void BuildMainGridColumns()
    {
        _mainGrid.Columns.Clear();
        _mainGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "ChannelName", HeaderText = "채널", DataPropertyName = "ChannelName", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "CskuCode", HeaderText = "CSKU", DataPropertyName = "CskuCode", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "Msku", HeaderText = "MSKU", DataPropertyName = "Msku", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "품목명", DataPropertyName = "ItemName", Width = 140 },
            new DataGridViewTextBoxColumn { Name = "CostPrice", HeaderText = "제조원가", DataPropertyName = "CostPrice", ReadOnly = true, Width = 90, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, ForeColor = SystemColors.GrayText } },
            new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "수량", DataPropertyName = "Quantity", Width = 70, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "SaleAmount", HeaderText = "판매금액", DataPropertyName = "SaleAmount", Width = 100, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "SettlementAmount", HeaderText = "정산금액", DataPropertyName = "SettlementAmount", Width = 100, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "FeeRate", HeaderText = "수수료(%)", Width = 80, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "MarginRate", HeaderText = "마진율", ReadOnly = true, Width = 80, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, BackColor = SystemColors.ControlLight } },
            new DataGridViewTextBoxColumn { Name = "ProfitAmount", HeaderText = "이익액", ReadOnly = true, Width = 100, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, BackColor = SystemColors.ControlLight } },
            new DataGridViewTextBoxColumn { Name = "ProfitPerUnit", HeaderText = "1개당 이익액", ReadOnly = true, Width = 100, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, BackColor = SystemColors.ControlLight } },
            new DataGridViewTextBoxColumn { Name = "SalePerUnit", HeaderText = "1개당 판매금액", ReadOnly = true, Width = 110, DefaultCellStyle = { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight, BackColor = SystemColors.ControlLight } }
        );
    }

    private void OnMainGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _mainGrid.Rows.Count) return;
        if (_mainGrid.Rows[e.RowIndex].DataBoundItem is not SimpleMarginCalcRow row) return;
        var column = _mainGrid.Columns[e.ColumnIndex];

        if (column.Name == "FeeRate")
        {
            e.Value = row.FeeRate is { } f ? $"{f * 100:0.##}" : "";
            e.FormattingApplied = true;
            return;
        }

        if (!row.IsComputable && column.Name is "MarginRate" or "ProfitAmount" or "ProfitPerUnit" or "SalePerUnit")
        {
            e.Value = row.ComputeReason ?? "";
            e.FormattingApplied = true;
            return;
        }

        switch (column.Name)
        {
            case "MarginRate": e.Value = row.MarginRate is { } mr ? mr.ToString("P1") : ""; e.FormattingApplied = true; break;
            case "ProfitAmount": e.Value = row.ProfitAmount?.ToString("N0") ?? ""; e.FormattingApplied = true; break;
            case "ProfitPerUnit": e.Value = row.ProfitPerUnit?.ToString("N0") ?? ""; e.FormattingApplied = true; break;
            case "SalePerUnit": e.Value = row.SalePerUnit?.ToString("N0") ?? ""; e.FormattingApplied = true; break;
        }
    }

    /// <summary>역마진(이익액 &lt; 0) 행 배경 강조.</summary>
    private void OnMainGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _mainGrid.Rows.Count) return;
        if (_mainGrid.Rows[e.RowIndex].DataBoundItem is not SimpleMarginCalcRow row) return;

        _mainGrid.Rows[e.RowIndex].DefaultCellStyle.BackColor =
            row.IsComputable && row.ProfitAmount is < 0 ? SystemColors.Info : _mainGrid.DefaultCellStyle.BackColor;
    }

    private void OnMainGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _mainGrid.Rows.Count) return;
        if (_mainGrid.Rows[e.RowIndex].DataBoundItem is not SimpleMarginCalcRow row) return;
        var column = _mainGrid.Columns[e.ColumnIndex];

        if (column.Name == "FeeRate")
        {
            var raw = _mainGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();
            row.FeeRate = string.IsNullOrWhiteSpace(raw) ? null : (decimal.TryParse(raw, out var f) ? f / 100m : row.FeeRate);
        }

        RecalculateRow(row);
        _mainGrid.InvalidateRow(e.RowIndex);
        UpdateTotalLabel();
    }

    // ───────────────────────── 계산 ─────────────────────────

    private void RecalculateAll()
    {
        foreach (var row in _rows) RecalculateRow(row);
        UpdateTotalLabel();
        _mainGrid.Invalidate();
    }

    private static void RecalculateRow(SimpleMarginCalcRow row)
    {
        var result = SimpleMarginCalculator.Calculate(new SimpleMarginCalcInput
        {
            CostPrice = row.CostPrice,
            Quantity = row.Quantity,
            SaleAmount = row.SaleAmount,
            SettlementAmount = row.SettlementAmount,
            FeeRate = row.FeeRate,
        });

        row.IsComputable = result.IsComputable;
        row.ComputeReason = result.Reason;
        row.ProfitAmount = result.ProfitAmount;
        row.ProfitPerUnit = result.ProfitPerUnit;
        row.SalePerUnit = result.SalePerUnit;
        row.RevenueBasis = result.RevenueBasis;
        row.MarginRate = result.MarginRate;
    }

    /// <summary>합계 마진율은 각 행 마진율의 단순평균이 아니라, 합계 이익액 ÷ 합계 매출기준액인
    /// 가중평균이다 — 금액이 큰 품목이 전체 비율에 더 크게 반영되는 "전체 블렌드 마진율".</summary>
    private void UpdateTotalLabel()
    {
        var computable = _rows.Where(r => r.IsComputable).ToList();
        var totalProfit = computable.Sum(r => r.ProfitAmount ?? 0m);
        var totalRevenue = computable.Sum(r => r.RevenueBasis ?? 0m);

        var marginText = totalRevenue == 0 ? "-" : (totalProfit / totalRevenue).ToString("P1");
        _totalLabel.Text = $"합계 이익액: {totalProfit:N0}원   합계 마진율: {marginText}";
    }

    // ───────────────────────── 하단 버튼 바 ─────────────────────────

    private Control CreateBottomBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6) };

        var btnLoadCsku = new Button { Text = "CSKU 불러오기", Width = 110 };
        btnLoadCsku.Click += (s, e) => OnLoadCskuClick();
        bar.Controls.Add(btnLoadCsku);

        var btnLookup = new Button { Text = "조회", Width = 70 };
        btnLookup.Click += (s, e) => OnLookupClick();
        bar.Controls.Add(btnLookup);

        var btnAddRow = new Button { Text = "행 추가", Width = 90 };
        btnAddRow.Click += (s, e) => AddRow();
        bar.Controls.Add(btnAddRow);

        var btnRemoveRow = new Button { Text = "행 삭제", Width = 90 };
        btnRemoveRow.Click += (s, e) =>
        {
            var selected = _mainGrid.SelectedRows.Cast<DataGridViewRow>()
                .Select(r => r.DataBoundItem as SimpleMarginCalcRow).Where(r => r != null).ToList();
            foreach (var row in selected) _rows.Remove(row!);
            UpdateTotalLabel();
        };
        bar.Controls.Add(btnRemoveRow);

        var btnSaveScenario = new Button { Text = "임시저장", Width = 90 };
        btnSaveScenario.Click += (s, e) => OnSaveScenarioClick();
        bar.Controls.Add(btnSaveScenario);

        var btnLoadScenario = new Button { Text = "임시저장 불러오기", Width = 130 };
        btnLoadScenario.Click += (s, e) => OnLoadScenarioClick();
        bar.Controls.Add(btnLoadScenario);

        _totalLabel = new Label { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 8, 0, 0), Font = new Font(Font, FontStyle.Bold) };
        bar.Controls.Add(_totalLabel);

        return bar;
    }

    // ───────────────────────── 임시저장 ─────────────────────────

    /// <summary>현재 화면(CSKU 목록·입력값 전체)을 스냅샷으로 저장한다 — 최근 5개까지만 보관되며
    /// 6번째부터 가장 오래된 것을 자동으로 버린다(<see cref="SimpleMarginCalculatorScenarioService"/>).</summary>
    private void OnSaveScenarioClick()
    {
        if (_rows.All(IsBlankRow)) { MessageBox.Show("저장할 계산 내용이 없습니다.", "알림"); return; }

        var defaultLabel = BuildScenarioDefaultLabel();
        using var promptDialog = new SimpleTextPromptDialog("임시저장", "저장 이름", defaultLabel);
        if (FormManager.ShowDialogSafe(promptDialog, this) != DialogResult.OK) return;

        var scenario = new SimpleMarginCalcScenario
        {
            Label = string.IsNullOrWhiteSpace(promptDialog.Value) ? defaultLabel : promptDialog.Value,
            Rows = _rows.Where(r => !IsBlankRow(r)).ToList(),
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

    /// <summary>임시저장 목록에서 하나를 골라 현재 화면을 통째로 덮어쓴다.</summary>
    private void OnLoadScenarioClick()
    {
        using var pickerDialog = new SimpleMarginScenarioPickerDialog(_scenarioService);
        if (FormManager.ShowDialogSafe(pickerDialog, this) != DialogResult.OK || pickerDialog.SelectedScenario == null) return;

        if (_rows.Any(r => !IsBlankRow(r)))
        {
            var result = MessageBox.Show(
                "현재 작성 중인 계산 내용이 있습니다. 불러오면 사라집니다.\n계속하시겠습니까?",
                "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
        }

        ApplyScenario(pickerDialog.SelectedScenario);
    }

    private void ApplyScenario(SimpleMarginCalcScenario scenario)
    {
        _rows.Clear();
        foreach (var row in scenario.Rows) _rows.Add(row);
        if (_rows.Count == 0) AddRow();

        RecalculateAll();
    }

    // ───────────────────────── CSKU 불러오기 ─────────────────────────

    private void OnLoadCskuClick()
    {
        using var dialog = new CskuPickerDialog(allowMasterSkuOnly: true, allowMultiSelect: true);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK || dialog.SelectedItems.Count == 0) return;

        foreach (var selected in dialog.SelectedItems)
        {
            var row = _rows.Count == 1 && IsBlankRow(_rows[0]) ? _rows[0] : new SimpleMarginCalcRow();
            if (!_rows.Contains(row)) _rows.Add(row);

            row.ChannelCode = selected.ChannelCode;
            row.ChannelName = selected.ChannelCode == null
                ? null
                : _channelRepository.GetAll().FirstOrDefault(c => c.ChannelCode == selected.ChannelCode)?.ChannelName ?? selected.ChannelCode;
            row.CskuCode = selected.CskuCode;
            row.Msku = selected.Msku;
            row.ItemName = selected.ItemName;
            row.CostPrice = selected.CostPrice;

            RecalculateRow(row);
        }

        UpdateTotalLabel();
        _mainGrid.Invalidate();
    }

    // ───────────────────────── 직접 입력 후 조회 ─────────────────────────

    /// <summary>선택한 행(없으면 현재 행)에 대해, 채널+CSKU가 입력됐으면 CSKU 개별원가를,
    /// 아니면 MSKU 마스터원가를 DB에서 조회해 채운다.</summary>
    private void OnLookupClick()
    {
        var targets = GetSelectedRows();
        if (targets.Count == 0 && _mainGrid.CurrentRow?.DataBoundItem is SimpleMarginCalcRow current) targets = new() { current };
        if (targets.Count == 0) { MessageBox.Show("조회할 행을 선택하세요.", "알림"); return; }

        var found = 0;
        var notFound = 0;
        foreach (var row in targets)
        {
            if (TryLookup(row)) found++; else notFound++;
            RecalculateRow(row);
        }

        UpdateTotalLabel();
        _mainGrid.Invalidate();

        if (notFound > 0)
        {
            MessageBox.Show($"조회 완료: 성공 {found}건, 실패 {notFound}건.\n실패한 행은 채널·CSKU 또는 MSKU 입력을 확인하세요.", "조회 결과", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private bool TryLookup(SimpleMarginCalcRow row)
    {
        var cskuCode = row.CskuCode?.Trim();
        var channelName = row.ChannelName?.Trim();

        if (!string.IsNullOrEmpty(cskuCode) && !string.IsNullOrEmpty(channelName))
        {
            var channel = _channelRepository.GetAll().FirstOrDefault(c => c.ChannelName.Equals(channelName, StringComparison.OrdinalIgnoreCase));
            if (channel != null)
            {
                var csku = _cskuRepository.GetByChannelAndCskuCode(channel.ChannelCode, cskuCode);
                if (csku != null)
                {
                    var item = _itemRepository.GetBySku(csku.Msku);
                    row.ChannelCode = channel.ChannelCode;
                    row.ChannelName = channel.ChannelName;
                    row.CskuCode = csku.CskuCode;
                    row.Msku = csku.Msku;
                    row.ItemName = !string.IsNullOrWhiteSpace(csku.InvoiceDisplayName) ? csku.InvoiceDisplayName! : (item?.ItemName ?? csku.Msku);
                    row.CostPrice = csku.CostPriceOverride ?? item?.CostPrice ?? 0m;
                    return true;
                }
            }
        }

        var msku = row.Msku?.Trim();
        if (!string.IsNullOrEmpty(msku))
        {
            var item = _itemRepository.GetBySku(msku);
            if (item != null)
            {
                row.Msku = item.Sku;
                row.ItemName = item.ItemName;
                row.CostPrice = item.CostPrice;
                return true;
            }
        }

        return false;
    }

    private List<SimpleMarginCalcRow> GetSelectedRows() =>
        _mainGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as SimpleMarginCalcRow).Where(r => r != null).Cast<SimpleMarginCalcRow>()
            .Reverse().ToList();

    private static bool IsBlankRow(SimpleMarginCalcRow row) =>
        row.Msku == null && row.CskuCode == null && row.Quantity == null && row.SaleAmount == null && row.SettlementAmount == null;

    private void AddRow()
    {
        var row = new SimpleMarginCalcRow();
        _rows.Add(row);
        RecalculateAll();
    }
}

using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 마감 처리 후 남은 미매핑 항목을 SKU에 연결하고 해당 채널을 재계산하는 창.
/// 매핑시스템 통합개편 기획서 §4.5 진입점 3: 행을 선택해 통합 매핑창(<see cref="MappingWorkbenchDialog"/>)을
/// 열어 1:1/조건부/제외/신규등록 중 골라 처리하면, 그 채널만 바로 재계산하고 다음 미매핑 행으로
/// 넘어간다(대량 처리이므로 리스트→선택→창 열기→다음 항목 반복 흐름은 유지, 창 자체는 공용).
/// </summary>
public class UnmappedQueueForm : Form
{
    private readonly long _runId;
    private readonly string _period;
    private readonly ClosingOrchestrator _orchestrator;
    private readonly ClosingRunRepository _runRepo = new();

    private List<ClosingUnmappedItem> _items = [];
    private DataGridView _grid = new();
    private Label _statusLabel = new();

    public UnmappedQueueForm(long runId, string period, ClosingOrchestrator orchestrator)
    {
        _runId = runId;
        _period = period;
        _orchestrator = orchestrator;
        InitializeComponent();
        LoadData();
    }

    private void InitializeComponent()
    {
        Text = $"미매핑 큐 — {_period}";
        Size = new Size(900, 580);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 400);

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var header = new Label
        {
            Text = "행을 선택하고 '매핑하기'(또는 더블클릭)를 누르세요. 처리하면 해당 채널만 자동으로 재계산됩니다.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
        };

        _grid = BuildGrid();
        _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenMappingWorkbenchForSelectedRow(); };

        var footer = BuildFooter();

        outer.Controls.Add(header, 0, 0);
        outer.Controls.Add(_grid, 0, 1);
        outer.Controls.Add(footer, 0, 2);
        Controls.Add(outer);
    }

    private DataGridView BuildGrid()
    {
        var grid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "채널", HeaderText = "채널", Width = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "상품명", HeaderText = "상품명", Width = 220 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "옵션명", HeaderText = "옵션명", Width = 160 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "횟수", HeaderText = "횟수", Width = 60, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "금액", HeaderText = "금액", Width = 90, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });

        return grid;
    }

    private Panel BuildFooter()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };

        var mapBtn = new Button { Text = "매핑하기", Width = 90, Top = 4, Left = 0 };
        mapBtn.Click += (s, e) => OpenMappingWorkbenchForSelectedRow();

        var closeBtn = new Button { Text = "닫기", Width = 70, Top = 4, Left = 100 };
        closeBtn.Click += (_, _) => Close();

        _statusLabel = new Label { AutoSize = true, Top = 10, Left = 190, ForeColor = Color.Gray };

        panel.Controls.AddRange([mapBtn, closeBtn, _statusLabel]);
        return panel;
    }

    // ─── 데이터 로드 ─────────────────────────────────────────────────────────

    private void LoadData()
    {
        _items = _runRepo.GetUnmapped(_runId);

        _grid.Rows.Clear();
        foreach (var item in _items)
        {
            _grid.Rows.Add(
                item.ChannelName,
                item.ProductName,
                item.OptionName,
                item.OccurrenceCount,
                item.SampleAmount.ToString("N0")
            );
        }

        _statusLabel.Text = $"미매핑 {_items.Count}건";
    }

    // ─── 매핑하기(통합 매핑창) ───────────────────────────────────────────────

    private async void OpenMappingWorkbenchForSelectedRow()
    {
        var rowIndex = _grid.CurrentRow?.Index ?? -1;
        if (rowIndex < 0 || rowIndex >= _items.Count)
        {
            MessageBox.Show("매핑할 행을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var item = _items[rowIndex];
        var syntheticItem = new OfsOrderItem
        {
            ProductName = item.ProductName,
            OptionName = item.OptionName,
            Quantity = item.Quantity ?? 0,
            Revenue = item.SampleRevenue,
        };

        using var dialog = new MappingWorkbenchDialog(syntheticItem, item.ChannelCode, settlementMode: true);
        FormManager.ApplyBoundsTracking(dialog);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) return;

        _statusLabel.Text = "재계산 중...";
        var progress = new Progress<ClosingOrchestrator.ProcessProgress>(p =>
        {
            _statusLabel.Text = $"재계산: {p.FileName} — {p.Message}";
        });

        try
        {
            await _orchestrator.RecalcChannelAsync(_runId, item.ChannelCode, _period, progress);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"재계산 오류 ({item.ChannelCode}): {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        LoadData();

        // 처리된 행이 목록에서 빠진 자리(또는 다음 행)를 그대로 선택해 "다음 항목" 흐름을 이어간다.
        if (_grid.Rows.Count > 0)
        {
            var nextIndex = Math.Min(rowIndex, _grid.Rows.Count - 1);
            _grid.ClearSelection();
            _grid.Rows[nextIndex].Selected = true;
            _grid.CurrentCell = _grid.Rows[nextIndex].Cells[0];
        }
    }
}

using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Services;

namespace MiniERP2.Forms;

/// <summary>
/// 마감 처리 후 남은 미매핑 항목을 SKU에 연결하고 해당 채널을 재계산하는 창.
/// </summary>
public class UnmappedQueueForm : Form
{
    private readonly long _runId;
    private readonly string _period;
    private readonly ClosingOrchestrator _orchestrator;
    private readonly ClosingRunRepository _runRepo = new();
    private readonly MappingRepository _mappingRepo = new();
    private readonly ItemRepository _itemRepo = new();

    private List<ClosingUnmappedItem> _items = [];
    private List<string> _allSkus = [];
    private DataGridView _grid = new();
    private Label _statusLabel = new();

    private const int ColChannel = 0;
    private const int ColProduct = 1;
    private const int ColOption = 2;
    private const int ColCount = 3;
    private const int ColAmount = 4;
    private const int ColSku = 5;

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
            Text = "각 항목에 SKU를 지정하고 '저장 후 재계산'을 누르세요.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
        };

        _grid = BuildGrid();

        var footer = BuildFooter();

        outer.Controls.Add(header, 0, 0);
        outer.Controls.Add(_grid, 0, 1);
        outer.Controls.Add(footer, 0, 2);
        Controls.Add(outer);
    }

    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EditMode = DataGridViewEditMode.EditOnEnter,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "채널", HeaderText = "채널", Width = 110, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "상품명", HeaderText = "상품명", Width = 200, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "옵션명", HeaderText = "옵션명", Width = 150, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "횟수", HeaderText = "횟수", Width = 60, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "금액", HeaderText = "금액", Width = 80, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });

        // SKU 선택 ComboBox — 로드 후 DataSource 교체
        var skuCol = new DataGridViewComboBoxColumn
        {
            Name = "SKU", HeaderText = "SKU", Width = 160,
            DataSource = new List<string>(),
        };
        grid.Columns.Add(skuCol);

        return grid;
    }

    private Panel BuildFooter()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4) };

        var saveBtn = new Button { Text = "저장 후 재계산", Width = 110, Top = 4, Left = 0 };
        saveBtn.Click += OnSaveAndRecalcClick;

        var closeBtn = new Button { Text = "닫기", Width = 70, Top = 4, Left = 120 };
        closeBtn.Click += (_, _) => Close();

        _statusLabel = new Label { AutoSize = true, Top = 10, Left = 210, ForeColor = Color.Gray };

        panel.Controls.AddRange([saveBtn, closeBtn, _statusLabel]);
        return panel;
    }

    // ─── 데이터 로드 ─────────────────────────────────────────────────────────

    private void LoadData()
    {
        _items = _runRepo.GetUnmapped(_runId);
        _allSkus = _itemRepo.GetAll().Select(i => i.Sku).OrderBy(s => s).ToList();

        // SKU ComboBox DataSource 갱신
        if (_grid.Columns[ColSku] is DataGridViewComboBoxColumn skuCol)
            skuCol.DataSource = _allSkus.Prepend("").ToList();

        _grid.Rows.Clear();
        foreach (var item in _items)
        {
            _grid.Rows.Add(
                item.ChannelName,
                item.ProductName,
                item.OptionName,
                item.OccurrenceCount,
                item.SampleAmount.ToString("N0"),
                item.MappedSku ?? ""
            );
        }

        _statusLabel.Text = $"미매핑 {_items.Count}건";
    }

    // ─── 저장 후 재계산 ──────────────────────────────────────────────────────

    private async void OnSaveAndRecalcClick(object? s, EventArgs e)
    {
        _grid.EndEdit();

        var affectedChannels = new HashSet<string>();
        var savedCount = 0;

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var sku = _grid.Rows[i].Cells[ColSku].Value as string ?? "";
            if (string.IsNullOrEmpty(sku)) continue;
            if (sku == (item.MappedSku ?? "")) continue;

            // DB에 매핑 저장 (ClosingUnmapped.MappedSku)
            _runRepo.SaveUnmappedMapping(item.Id, sku);
            item.MappedSku = sku;

            // 정식 매핑 규칙에도 추가 (RuleExact) — 다음 정산 로드부터 자동 매핑
            AddExactRule(item.ChannelCode, item.SourceKey, sku);

            affectedChannels.Add(item.ChannelCode);
            savedCount++;
        }

        if (savedCount == 0)
        {
            _statusLabel.Text = "변경된 매핑이 없습니다.";
            return;
        }

        _statusLabel.Text = $"저장 완료 ({savedCount}건). 재계산 중...";

        var progress = new Progress<ClosingOrchestrator.ProcessProgress>(p =>
        {
            _statusLabel.Text = $"재계산: {p.FileName} — {p.Message}";
        });

        foreach (var channelCode in affectedChannels)
        {
            try
            {
                await _orchestrator.RecalcChannelAsync(_runId, channelCode, _period, progress);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"재계산 오류 ({channelCode}): {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        _statusLabel.Text = $"재계산 완료. 미매핑 {_items.Count(i => i.MappedSku == null)}건 남음.";
        LoadData();
    }

    private void AddExactRule(string channelCode, string sourceKey, string targetSku)
    {
        try
        {
            var existing = _mappingRepo.GetRules(MappingRuleType.Exact, channelCode);
            if (existing.Any(r => r.Key == sourceKey)) return;

            existing.Add(new MappingRule
            {
                ChannelCode = channelCode,
                RuleType = MappingRuleType.Exact,
                Key = sourceKey,
                TargetSku = targetSku,
            });
            _mappingRepo.SaveRules(MappingRuleType.Exact, channelCode, existing);
        }
        catch
        {
            // 규칙 추가 실패는 치명적이지 않음 — ClosingUnmapped에는 저장됨
        }
    }
}

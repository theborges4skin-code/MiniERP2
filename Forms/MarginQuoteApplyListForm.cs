using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 판매가/납품가 적용(§6.2) — 목록창. 선택한 행을 채널별로 묶어 보여주고, 채널 그룹 하나씩
/// 편집창(<see cref="MarginQuoteApplyEditDialog"/>)으로 순차 처리한다(결정사항 2 — 다중 채널을
/// 한 번에 일괄 확인하지 않는다).
/// </summary>
public class MarginQuoteApplyListForm : Form
{
    private sealed class GroupRow
    {
        public required string ChannelCode { get; init; }
        public required string ChannelName { get; init; }
        public required List<MarginCalcRow> Rows { get; init; }
        public string Status { get; set; } = "대기";
    }

    private readonly PriceQuoteRepository _quoteRepository = new();
    private readonly ChannelSkuRepository _cskuRepository = new();
    private readonly DataGridView _grid = new();
    private readonly List<GroupRow> _groups;
    private readonly Func<decimal, decimal> _toDbBasis;

    /// <summary>편집창에서 하나라도 저장에 성공했으면 true — 호출부가 "견적·단가 관리 창을 열어
    /// 확인하시겠습니까?" 안내를 띄울지 판단하는 데 쓴다.</summary>
    public bool AnyApplied { get; private set; }

    public MarginQuoteApplyListForm(IEnumerable<MarginCalcRow> eligibleRows, Func<string, string> channelNameResolver, Func<decimal, decimal>? toDbBasis = null)
    {
        _toDbBasis = toDbBasis ?? (v => v);
        _groups = eligibleRows
            .GroupBy(r => r.ChannelCode!)
            .Select(g => new GroupRow { ChannelCode = g.Key, ChannelName = channelNameResolver(g.Key), Rows = g.ToList() })
            .OrderBy(g => g.ChannelName)
            .ToList();

        InitializeComponent();
        RefreshGrid();
    }

    private void InitializeComponent()
    {
        Text = "판매가/납품가 적용 — 채널별 목록";
        Size = new Size(560, 440);
        MinimumSize = new Size(420, 300);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var hint = new Label
        {
            Text = "채널을 더블클릭(또는 [편집])하면 해당 채널 견적을 만들 수 있습니다. 채널마다 견적 1건으로 저장됩니다.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 40,
            Padding = new Padding(8),
        };
        layout.Controls.Add(hint, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add("ChannelName", "채널");
        _grid.Columns.Add("Count", "건수");
        _grid.Columns.Add("Status", "상태");
        _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenEditor(e.RowIndex); };
        layout.Controls.Add(_grid, 0, 1);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var btnClose = new Button { Text = "닫기", Width = 90 };
        var btnEdit = new Button { Text = "편집", Width = 90 };
        btnClose.Click += (s, e) => Close();
        btnEdit.Click += (s, e) => { if (_grid.CurrentRow != null) OpenEditor(_grid.CurrentRow.Index); };
        buttonPanel.Controls.Add(btnClose);
        buttonPanel.Controls.Add(btnEdit);
        layout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(layout);
    }

    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        foreach (var g in _groups) _grid.Rows.Add(g.ChannelName, g.Rows.Count, g.Status);
    }

    private void OpenEditor(int index)
    {
        var group = _groups[index];
        using var dialog = new MarginQuoteApplyEditDialog(group.ChannelCode, group.ChannelName, group.Rows, _quoteRepository, _cskuRepository, _toDbBasis);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) return;

        group.Status = $"완료 (견적 {dialog.SavedQuoteNo})";
        AnyApplied = true;
        RefreshGrid();
    }
}

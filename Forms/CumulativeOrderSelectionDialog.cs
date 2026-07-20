using System.ComponentModel;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 누적발주서 채널(과거 이력까지 누적해서 담긴 발주서 파일)에서 파일을 불러왔을 때, 발주일 기준
/// 최근 N일 이내로 이미 걸러진 목록을 보여주고 실제로 발주 처리할 건만 사용자가 직접 고르게 한다.
/// 여기서 선택하지 않은 건은 OFS 그리드에 추가되지 않는다(처리되지 않음).
/// </summary>
public class CumulativeOrderSelectionDialog : Form
{
    private readonly DataGridView _grid = new();
    public List<OfsOrderItem> SelectedItems { get; private set; } = [];

    /// <param name="alreadyShippedOrderNos">이미 출고확정 이력이 있는 주문번호(채널 무관). 이 목록에
    /// 속한 건은 목록에 "출고확정"으로 표시되고 기본 선택에서 제외된다.</param>
    public CumulativeOrderSelectionDialog(List<OfsOrderItem> recentItems, int windowDays, ISet<string> alreadyShippedOrderNos)
    {
        InitializeComponent(recentItems, windowDays, alreadyShippedOrderNos);
    }

    private void InitializeComponent(List<OfsOrderItem> recentItems, int windowDays, ISet<string> alreadyShippedOrderNos)
    {
        Text = "누적발주서 — 발주 처리할 항목 선택";
        Size = new Size(880, 520);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

        var infoLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"발주일 기준 최근 {windowDays}일 이내 {recentItems.Count}건입니다(최신순). 출고미확정 건이 기본 선택되어 있습니다 — " +
                   "확인 후 바로 '선택 건 발주처리'를 누르거나, Ctrl/Shift+클릭으로 선택을 조정하세요.",
            AutoSize = false,
        };

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "OrderNo", HeaderText = "주문번호", DataPropertyName = "OrderNo", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "OrderDate", HeaderText = "발주일", DataPropertyName = "OrderDate", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "ProductName", HeaderText = "상품명", DataPropertyName = "ProductName", Width = 220 },
            new DataGridViewTextBoxColumn { Name = "OptionName", HeaderText = "옵션명", DataPropertyName = "OptionName", Width = 180 },
            new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "수량", DataPropertyName = "Quantity", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "Recipient", HeaderText = "수취인", DataPropertyName = "Recipient", Width = 100 },
            new DataGridViewTextBoxColumn { Name = "ShippedStatus", HeaderText = "출고확정 여부", Width = 90 }
        );
        _grid.Columns["OrderDate"]!.DefaultCellStyle.Format = "yyyy-MM-dd";

        var sortedItems = recentItems.OrderByDescending(o => o.OrderDate ?? DateTime.MinValue).ToList();
        _grid.DataSource = new BindingList<OfsOrderItem>(sortedItems);

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not OfsOrderItem item) continue;
            bool alreadyShipped = !string.IsNullOrWhiteSpace(item.OrderNo) && alreadyShippedOrderNos.Contains(item.OrderNo!);
            row.Cells["ShippedStatus"].Value = alreadyShipped ? "출고확정" : "";
            if (alreadyShipped)
                row.DefaultCellStyle.ForeColor = Color.Gray;
            else
                row.Selected = true;
        }

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnCancel = new Button { Text = "취소", Width = 90 };
        var btnApply = new Button { Text = "선택 건 발주처리", Width = 120 };
        var btnSelectAll = new Button { Text = "전체 선택", Width = 90 };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnApply.Click += OnApplyClick;
        btnSelectAll.Click += (s, e) => _grid.SelectAll();
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnApply);
        buttonPanel.Controls.Add(btnSelectAll);

        mainLayout.Controls.Add(infoLabel, 0, 0);
        mainLayout.Controls.Add(_grid, 0, 1);
        mainLayout.Controls.Add(buttonPanel, 0, 2);
        Controls.Add(mainLayout);

        CancelButton = btnCancel;
    }

    private void OnApplyClick(object? sender, EventArgs e)
    {
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem)
            .OfType<OfsOrderItem>()
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show("발주 처리할 건을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectedItems = selected;
        DialogResult = DialogResult.OK;
        Close();
    }
}

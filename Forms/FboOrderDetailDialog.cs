using MiniERP2.Controls;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 풀필먼트 발주 이력(FboHistoryForm)의 "발주번호별" 뷰에서 행을 더블클릭하면 뜨는 읽기전용
/// 세부내역 창. 발주 1건의 박스/품목 구성을 그대로 보여주기만 하고 수정은 지원하지 않는다
/// (수정이 필요하면 "복사하여 신규 발주"로 새 발주를 만들어야 한다).
/// </summary>
public class FboOrderDetailDialog : Form
{
    public FboOrderDetailDialog(FboOrder order, List<FboBox> boxes, List<FboBoxItem> items, string channelName)
    {
        InitializeComponent(order, boxes, items, channelName);
    }

    private void InitializeComponent(FboOrder order, List<FboBox> boxes, List<FboBoxItem> items, string channelName)
    {
        Text = $"발주 상세 - {order.FboNo}";
        Size = new Size(820, 520);
        MinimumSize = new Size(600, 360);
        StartPosition = FormStartPosition.CenterParent;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var infoLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            Font = new Font(Font, FontStyle.Bold),
            Text = $"발주번호: {order.FboNo}   발주일: {order.OrderDate:yyyy-MM-dd}   채널: {channelName}   상태: {order.Status}",
        };

        var grid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "박스", Name = "BoxSeq", Width = 50 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "반품부명", Name = "ReceiverDisplayName", Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "Csku", Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "품목명", Name = "ItemName", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", Width = 55, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "유통기한", Name = "ExpiryDate", Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "박스타입", Name = "BoxType", Width = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "이송장번호", Name = "TrackingNo", Width = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "상태", Name = "Status", Width = 80 });

        var boxesBySeq = boxes.ToDictionary(b => b.BoxSeq);
        foreach (var item in items.OrderBy(i => i.BoxSeq).ThenBy(i => i.ItemSeq))
        {
            if (!boxesBySeq.TryGetValue(item.BoxSeq, out var box)) continue;
            grid.Rows.Add(item.BoxSeq, box.ReceiverDisplayName, item.Csku, item.ItemName, item.Qty,
                item.ExpiryDate, box.BoxType, box.TrackingNo, box.Status);
        }

        mainLayout.Controls.Add(infoLabel, 0, 0);
        mainLayout.Controls.Add(grid, 0, 1);
        Controls.Add(mainLayout);
    }
}

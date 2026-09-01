using MiniERP2.Controls;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// FBA 발주 이력(FbaHistoryForm)에서 "발주 상세 열기"를 누르면 뜨는 읽기전용 세부내역 창
/// (FboOrderDetailDialog와 동일 목적). 발주 1건의 박스/품목 구성을 그대로 보여주기만 하고
/// 수정은 지원하지 않는다(수정이 필요하면 "복사하여 신규 발주"로 새 발주를 만들어야 한다).
/// </summary>
public class FbaOrderDetailDialog : Form
{
    public FbaOrderDetailDialog(FbaOrder order, List<FbaBox> boxes, List<FbaBoxItem> items)
    {
        InitializeComponent(order, boxes, items);
    }

    private void InitializeComponent(FbaOrder order, List<FbaBox> boxes, List<FbaBoxItem> items)
    {
        Text = $"발주 상세 - {order.FbaNo}";
        Size = new Size(900, 520);
        MinimumSize = new Size(640, 360);
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
            Text = $"발주번호: {order.FbaNo}   발주일: {order.OrderDate:yyyy-MM-dd}   Shipment ID: {order.ShipmentId}   상태: {order.Status}",
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
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "박스", Name = "BoxSeq", Width = 45 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "박스규격", Name = "BoxSpecName", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "치수(mm)", Name = "Size", Width = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "Csku", Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "품목명", Name = "ItemName", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", Width = 55, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "유통기한", Name = "ExpiryDate", Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "박스무게(g)", Name = "WeightG", Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "운송장번호", Name = "TrackingNo", Width = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "상태", Name = "Status", Width = 80 });

        var boxesBySeq = boxes.ToDictionary(b => b.BoxSeq);
        foreach (var item in items.OrderBy(i => i.BoxSeq).ThenBy(i => i.ItemSeq))
        {
            if (!boxesBySeq.TryGetValue(item.BoxSeq, out var box)) continue;
            grid.Rows.Add(item.BoxSeq, box.BoxSpecName, $"{box.WidthMm:0}×{box.DepthMm:0}×{box.HeightMm:0}", item.Csku, item.ItemName,
                item.Qty, item.ExpiryDate, box.WeightG, box.TrackingNo, box.Status);
        }

        mainLayout.Controls.Add(infoLabel, 0, 0);
        mainLayout.Controls.Add(grid, 0, 1);
        Controls.Add(mainLayout);
    }
}

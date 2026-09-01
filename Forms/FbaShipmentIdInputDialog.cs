namespace MiniERP2.Forms;

/// <summary>
/// FBA 발주 이력 조회창에서 선택한 발주(1건 또는 여러 건)에 Shipment ID를 일괄 입력할 때 쓰는
/// 단순 입력창. 발주 작성 화면(FbaOrderForm)에서 입력을 놓쳤거나, 아마존이 Shipment ID를 발주
/// 확정 이후에야 발급하는 경우 발주 전체를 다시 열지 않고 이 값만 갱신하기 위함이다.
/// </summary>
public class FbaShipmentIdInputDialog : Form
{
    public string ShipmentId => _shipmentIdBox.Text.Trim();

    private readonly TextBox _shipmentIdBox = new();

    public FbaShipmentIdInputDialog(int targetOrderCount)
    {
        InitializeComponent(targetOrderCount);
    }

    private void InitializeComponent(int targetOrderCount)
    {
        Text = "Shipment ID 입력";
        Size = new Size(360, 170);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var infoLabel = new Label
        {
            Text = $"선택한 발주 {targetOrderCount}건에 동일한 Shipment ID를 적용합니다.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        _shipmentIdBox.Width = 300;
        _shipmentIdBox.Margin = new Padding(0, 0, 0, 6);

        var buttonPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 40 };
        var btnCancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Size = new Size(72, 30) };
        var btnOk = new Button { Text = "확인", DialogResult = DialogResult.OK, Size = new Size(72, 30) };
        btnOk.Click += OnOkClick;
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        layout.Controls.Add(infoLabel);
        layout.Controls.Add(_shipmentIdBox);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_shipmentIdBox.Text))
        {
            MessageBox.Show("Shipment ID를 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        }
    }
}

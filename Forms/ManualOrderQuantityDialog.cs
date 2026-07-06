namespace MiniERP2.Forms;

/// <summary>
/// 수동 주문 추가 시 수량을 입력받는 소형 확인 다이얼로그.
/// CSKU 코드·상품명을 표시하고 수량을 NumericUpDown으로 입력받습니다.
/// </summary>
public class ManualOrderQuantityDialog : Form
{
    private readonly NumericUpDown _qtySpinner = new();
    public int Quantity => (int)_qtySpinner.Value;

    public ManualOrderQuantityDialog(string cskuCode, string productName)
    {
        InitializeComponent(cskuCode, productName);
    }

    private void InitializeComponent(string cskuCode, string productName)
    {
        Text = "수량 입력";
        Size = new Size(320, 165);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var lblInfo = new Label
        {
            Text = $"{cskuCode}  —  {productName}",
            Dock = DockStyle.Top,
            Height = 44,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Regular)
        };

        var qtyRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(12, 6, 0, 0),
            FlowDirection = FlowDirection.LeftToRight
        };
        var lblQty = new Label { Text = "수량:", AutoSize = true, Margin = new Padding(0, 3, 6, 0) };
        _qtySpinner.Minimum = 1;
        _qtySpinner.Maximum = 9999;
        _qtySpinner.Value = 1;
        _qtySpinner.Width = 80;
        _qtySpinner.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
        };
        qtyRow.Controls.AddRange([lblQty, _qtySpinner]);

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8)
        };
        var btnOk = new Button { Text = "추가", Width = 80 };
        var btnCancel = new Button { Text = "취소", Width = 80 };
        btnOk.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnPanel.Controls.AddRange([btnCancel, btnOk]);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.Add(qtyRow);
        Controls.Add(lblInfo);
        Controls.Add(btnPanel);

        ActiveControl = _qtySpinner;
    }
}

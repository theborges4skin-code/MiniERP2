namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감보드(거래처마감보드_개발기획서.md §8)의 수동 거래처 월별 금액 입력, 또는 CH 거래처의
/// 대조 비고 입력에 쓰는 공용 다이얼로그. IsManual=false면 금액 입력란은 숨기고 비고만 받는다.
/// </summary>
public class PartnerManualEntryDialog : Form
{
    private readonly bool _isManual;
    private readonly TextBox _qtyBox = new();
    private readonly TextBox _supplyBox = new();
    private readonly TextBox _profitBox = new();
    private readonly TextBox _noteBox = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };

    public decimal Qty { get; private set; }
    public decimal Supply { get; private set; }
    public decimal Profit { get; private set; }
    public string Note => _noteBox.Text.Trim();

    public PartnerManualEntryDialog(string partyName, bool isManual, decimal qty, decimal supply, decimal profit, string note)
    {
        _isManual = isManual;
        Qty = qty; Supply = supply; Profit = profit;
        InitializeComponent(partyName, isManual);
        _qtyBox.Text = qty.ToString("0.####");
        _supplyBox.Text = supply.ToString("0.####");
        _profitBox.Text = profit.ToString("0.####");
        _noteBox.Text = note;
    }

    private void InitializeComponent(string partyName, bool isManual)
    {
        Text = isManual ? $"수동 거래처 입력 — {partyName}" : $"대조 비고 — {partyName}";
        Size = new Size(380, isManual ? 320 : 220);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control, int height = 28)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var row = layout.RowCount++;
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(control, 1, row);
        }

        if (isManual)
        {
            AddRow("수량:", _qtyBox);
            AddRow("공급가:", _supplyBox);
            AddRow("이익:", _profitBox);
        }
        AddRow("비고:", _noteBox, 90);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40 };
        var btnOk = new Button { Text = "확인", Size = new Size(80, 30) };
        var btnCancel = new Button { Text = "취소", Size = new Size(80, 30) };
        btnOk.Click += OnOkClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (_isManual)
        {
            if (!TryParse(_qtyBox.Text, out var q) || !TryParse(_supplyBox.Text, out var s) || !TryParse(_profitBox.Text, out var p))
            {
                MessageBox.Show("수량/공급가/이익은 숫자로 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Qty = q; Supply = s; Profit = p;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private static bool TryParse(string text, out decimal value) =>
        decimal.TryParse(text.Trim(), out value);
}

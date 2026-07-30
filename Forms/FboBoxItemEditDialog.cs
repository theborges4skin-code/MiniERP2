namespace MiniERP2.Forms;

/// <summary>
/// 풀필먼트 발주 이력의 CSKU 라인 1건(FboBoxItem)을 수정하는 다이얼로그(발주/출고 이력 조회창
/// §라인 단위 관리). 지금까지는 발주(FboNo) 전체 단위로만 다룰 수 있어 라인 하나를 고치려면
/// 발주 전체를 다시 만들어야 했다.
/// </summary>
public class FboBoxItemEditDialog : Form
{
    private readonly TextBox _cskuBox = new();
    private readonly TextBox _itemNameBox = new();
    private readonly TextBox _qtyBox = new();
    private readonly TextBox _expiryDateBox = new();

    public string Csku => _cskuBox.Text.Trim();
    public string ItemName => _itemNameBox.Text.Trim();
    public int Qty { get; private set; }
    public string? ExpiryDate => string.IsNullOrWhiteSpace(_expiryDateBox.Text) ? null : _expiryDateBox.Text.Trim();

    public FboBoxItemEditDialog(string fboNo, int boxSeq, string csku, string itemName, int qty, string? expiryDate)
    {
        InitializeComponent(fboNo, boxSeq);
        _cskuBox.Text = csku;
        _itemNameBox.Text = itemName;
        _qtyBox.Text = qty.ToString();
        _expiryDateBox.Text = expiryDate ?? "";
    }

    private void InitializeComponent(string fboNo, int boxSeq)
    {
        Text = $"라인 수정 — {fboNo} / 박스{boxSeq}";
        Size = new Size(380, 260);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            var row = layout.RowCount++;
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(control, 1, row);
        }

        AddRow("CSKU:", _cskuBox);
        AddRow("품목명:", _itemNameBox);
        AddRow("수량:", _qtyBox);
        AddRow("유통기한:", _expiryDateBox);

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
        if (string.IsNullOrWhiteSpace(Csku))
        {
            MessageBox.Show("CSKU를 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!int.TryParse(_qtyBox.Text.Trim(), out var qty) || qty <= 0)
        {
            MessageBox.Show("수량은 0보다 큰 정수로 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Qty = qty;
        DialogResult = DialogResult.OK;
        Close();
    }
}

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감보드에서 MANUAL(미경유) 거래처에 품목별 라인을 추가하는 입력창. 채널 경유 거래처용
/// <see cref="PartnerManualOrderDialog"/>와 달리 등록된 CSKU가 없으므로(=연결된 채널이 없음) CSKU를
/// 자유 텍스트로 입력받고(비워두면 품목명으로 대체), 원가도 자동 계산 없이 직접 입력받는다.
/// </summary>
public class PartnerManualLineDialog : Form
{
    private readonly TextBox _cskuBox = new();
    private readonly TextBox _itemNameBox = new();
    private readonly TextBox _qtyBox = new() { Text = "1" };
    private readonly TextBox _unitPriceBox = new();
    private readonly TextBox _costPriceBox = new() { Text = "0" };
    private readonly DateTimePicker _datePicker = new() { Format = DateTimePickerFormat.Short, Value = DateTime.Today };
    private readonly TextBox _noteBox = new();

    public string CskuCode { get; private set; } = "";
    public string ItemName { get; private set; } = "";
    public decimal Qty { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal CostPrice { get; private set; }
    public DateTime OrderDate => _datePicker.Value.Date;
    public string Note => _noteBox.Text.Trim();

    public PartnerManualLineDialog(string partyName)
    {
        InitializeComponent(partyName);
    }

    private void InitializeComponent(string partyName)
    {
        Text = $"수동 주문 추가 — {partyName}";
        Size = new Size(420, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control, int height = 30)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var row = layout.RowCount++;
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(control, 1, row);
        }

        AddRow("품목명:", _itemNameBox);
        AddRow("CSKU(선택):", _cskuBox);
        AddRow("수량:", _qtyBox);
        AddRow("납품가(단가):", _unitPriceBox);
        AddRow("원가:", _costPriceBox);
        AddRow("발주일/출고일:", _datePicker);
        AddRow("비고:", _noteBox);

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
        var itemName = _itemNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(itemName))
        {
            MessageBox.Show("품목명을 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!decimal.TryParse(_qtyBox.Text.Trim(), out var qty) || qty <= 0)
        {
            MessageBox.Show("수량은 0보다 큰 숫자로 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!decimal.TryParse(_unitPriceBox.Text.Trim(), out var unitPrice) || unitPrice < 0)
        {
            MessageBox.Show("납품가는 0 이상 숫자로 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!decimal.TryParse(_costPriceBox.Text.Trim(), out var costPrice) || costPrice < 0)
        {
            MessageBox.Show("원가는 0 이상 숫자로 입력하세요(등록된 CSKU가 없어 자동 계산할 수 없습니다 — 직접 입력).", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ItemName = itemName;
        var typedCsku = _cskuBox.Text.Trim();
        CskuCode = string.IsNullOrEmpty(typedCsku) ? itemName : typedCsku;
        Qty = qty;
        UnitPrice = unitPrice;
        CostPrice = costPrice;

        DialogResult = DialogResult.OK;
        Close();
    }
}

using MiniERP2.Database;

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감보드(거래처마감보드_개발기획서.md §2)에서 MiniERP2(OFS)를 경유하지 않은 주문을 수동으로
/// 발주/출고 이력에 추가하기 위한 입력창. CSKU를 입력하면 채널SKU 등록 정보(납품가·송장표시명)를
/// 자동으로 채워주되, 값은 직접 덮어쓸 수 있다.
/// </summary>
public class PartnerManualOrderDialog : Form
{
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly string _channelCode;

    private readonly TextBox _cskuBox = new();
    private readonly TextBox _itemNameBox = new();
    private readonly TextBox _qtyBox = new() { Text = "1" };
    private readonly TextBox _unitPriceBox = new();
    private readonly TextBox _costPriceBox = new();
    private readonly DateTimePicker _datePicker = new() { Format = DateTimePickerFormat.Short, Value = DateTime.Today };
    private readonly TextBox _noteBox = new() { Text = "수동입력(거래처 마감보드)" };

    public string CskuCode => _cskuBox.Text.Trim();
    public string ItemName => _itemNameBox.Text.Trim();
    public decimal Qty { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal? CostPrice { get; private set; }
    public DateTime OrderDate => _datePicker.Value.Date;
    public string Note => _noteBox.Text.Trim();

    public PartnerManualOrderDialog(string channelCode, string channelName)
    {
        _channelCode = channelCode;
        InitializeComponent(channelName);
    }

    private void InitializeComponent(string channelName)
    {
        Text = $"수동 주문 추가 — {channelName}";
        Size = new Size(380, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            var row = layout.RowCount++;
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(control, 1, row);
        }

        AddRow("CSKU 코드:", _cskuBox);
        AddRow("품목명:", _itemNameBox);
        AddRow("수량:", _qtyBox);
        AddRow("납품가(단가):", _unitPriceBox);
        AddRow("원가(선택):", _costPriceBox);
        AddRow("발주일/출고일:", _datePicker);
        AddRow("비고:", _noteBox);

        _cskuBox.Leave += OnCskuBoxLeave;

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

    /// <summary>CSKU 칸을 벗어나면 이 채널에 등록된 납품가/송장표시명을 자동으로 채운다(직접 덮어쓰기 가능).</summary>
    private void OnCskuBoxLeave(object? sender, EventArgs e)
    {
        var csku = _cskuBox.Text.Trim();
        if (string.IsNullOrEmpty(csku)) return;

        var registered = _channelSkuRepository.GetByChannelAndCskuCode(_channelCode, csku);
        if (registered == null) return;

        if (string.IsNullOrWhiteSpace(_unitPriceBox.Text) || _unitPriceBox.Text == "0")
            _unitPriceBox.Text = registered.SupplyPrice.ToString("0");
        if (string.IsNullOrWhiteSpace(_itemNameBox.Text) && !string.IsNullOrWhiteSpace(registered.InvoiceDisplayName))
            _itemNameBox.Text = registered.InvoiceDisplayName;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CskuCode))
        {
            MessageBox.Show("CSKU 코드를 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        decimal? costPrice = null;
        if (!string.IsNullOrWhiteSpace(_costPriceBox.Text))
        {
            if (!decimal.TryParse(_costPriceBox.Text.Trim(), out var cost) || cost < 0)
            {
                MessageBox.Show("원가는 0 이상 숫자로 입력하세요(비워두면 대표원가로 자동 계산됩니다).", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            costPrice = cost;
        }

        Qty = qty;
        UnitPrice = unitPrice;
        CostPrice = costPrice;
        if (string.IsNullOrWhiteSpace(ItemName)) _itemNameBox.Text = CskuCode;

        DialogResult = DialogResult.OK;
        Close();
    }
}

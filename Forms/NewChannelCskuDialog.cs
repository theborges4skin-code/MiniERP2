using MiniERP2.Database;
using MiniERP2.Utils;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 거래처별 CSKU 관리 화면에서 CSKU 1건을 새로 등록하는 다이얼로그. 그리드 맨 아래 빈 행에
/// 직접 타이핑해도 등록은 되지만(AllowUserToAddRows), 마스터SKU를 검색하거나 그 자리에서 새로
/// 만들 수가 없어 매번 마스터SKU 관리창을 오가야 했다(마스터SKU 관리의 "새 마스터SKU 추가"와
/// 같은 이유의 요청). 여기서는 마스터SKU를 고르거나 바로 새로 만들고, CSKU 코드 기본값도
/// 자동 제안한다.
/// </summary>
public class NewChannelCskuDialog : Form
{
    private readonly ItemRepository _itemRepository = new();
    private readonly ChannelSkuRepository _cskuRepository = new();
    private readonly string _channelCode;
    private readonly string _channelName;

    private ComboBox _skuCombo = new();
    private TextBox _cskuCodeBox = new();
    private TextBox _invoiceDisplayNameBox = new();
    private TextBox _supplyPriceBox = new() { Text = "0" };
    private TextBox _unitBox = new() { Text = "kg" };
    private TextBox _packingBox = new();
    private TextBox _noteBox = new();

    public string? SelectedMasterSku { get; private set; }
    public string CskuCode => _cskuCodeBox.Text.Trim();
    public string InvoiceDisplayName => _invoiceDisplayNameBox.Text.Trim();
    public decimal SupplyPrice { get; private set; }
    public string Unit => string.IsNullOrWhiteSpace(_unitBox.Text) ? "kg" : _unitBox.Text.Trim();
    public string Packing => _packingBox.Text.Trim();
    public string Note => _noteBox.Text.Trim();

    public NewChannelCskuDialog(string channelCode, string channelName)
    {
        _channelCode = channelCode;
        _channelName = channelName;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = $"CSKU 추가 — {_channelName}";
        Size = new Size(440, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var items = _itemRepository.GetAll().OrderBy(i => i.Sku).ToList();
        _skuCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        _skuCombo.Items.AddRange(items.Select(i => $"{i.Sku} — {i.ItemName}").Cast<object>().ToArray());
        _skuCombo.Leave += OnSkuComboLeave;

        AddRow(layout, "마스터SKU:", _skuCombo);

        var btnNew = new Button { Text = "새 마스터SKU 만들기...", Dock = DockStyle.Fill, Height = 28 };
        btnNew.Click += (s, e) =>
        {
            using var dlg = new NewMasterSkuDialog();
            if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK || dlg.ResultSku == null) return;
            var label = $"{dlg.ResultSku} — {dlg.ResultItemName}";
            _skuCombo.Items.Add(label);
            _skuCombo.Text = dlg.ResultSku;
            SuggestCskuCode();
        };
        AddRow(layout, "", btnNew);

        AddRow(layout, "CSKU 코드:", _cskuCodeBox);
        AddRow(layout, "송장표시명:", _invoiceDisplayNameBox);
        AddRow(layout, "납품가:", _supplyPriceBox);
        AddRow(layout, "단위:", _unitBox);
        AddRow(layout, "포장단위:", _packingBox);
        AddRow(layout, "비고:", _noteBox);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40 };
        var btnOk = new Button { Text = "추가", Size = new Size(80, 30) };
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

    private static void AddRow(TableLayoutPanel layout, string label, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        var row = layout.RowCount++;
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = DockStyle.Fill;
        layout.Controls.Add(control, 1, row);
    }

    /// <summary>마스터SKU 콤보를 벗어나면(선택/직접입력 후) CSKU 코드가 비어있는 경우 "채널명 앞
    /// 3글자_마스터SKU" 기본값을 제안한다(ChannelCskuForm의 기존 관례와 동일).</summary>
    private void OnSkuComboLeave(object? sender, EventArgs e) => SuggestCskuCode();

    private void SuggestCskuCode()
    {
        if (!string.IsNullOrWhiteSpace(_cskuCodeBox.Text)) return;
        var sku = ParseSelectedSku();
        if (string.IsNullOrEmpty(sku)) return;
        _cskuCodeBox.Text = CskuCodeGenerator.BuildDefault(_channelName, sku);
    }

    private string? ParseSelectedSku()
    {
        var text = _skuCombo.Text.Trim();
        return text.Contains(" — ") ? text[..text.IndexOf(" — ", StringComparison.Ordinal)] : (string.IsNullOrEmpty(text) ? null : text);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var sku = ParseSelectedSku();
        if (string.IsNullOrEmpty(sku) || _itemRepository.GetBySku(sku) == null)
        {
            MessageBox.Show("등록된 마스터SKU를 선택하거나 [새 마스터SKU 만들기]로 먼저 등록하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(CskuCode))
        {
            MessageBox.Show("CSKU 코드를 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_cskuRepository.GetByChannelAndCskuCode(_channelCode, CskuCode) != null)
        {
            MessageBox.Show($"이미 존재하는 CSKU 코드 '{CskuCode}'입니다. 다른 코드를 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!decimal.TryParse(_supplyPriceBox.Text.Trim(), out var price) || price < 0)
        {
            MessageBox.Show("납품가는 0 이상 숫자로 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SelectedMasterSku = sku;
        SupplyPrice = price;
        DialogResult = DialogResult.OK;
        Close();
    }
}

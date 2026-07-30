using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감보드(거래처마감보드_개발기획서.md §2)에서 MiniERP2(OFS)를 경유하지 않은 주문을 수동으로
/// 발주/출고 이력에 추가하기 위한 입력창. CSKU는 자유 텍스트가 아니라 이 채널에 이미 등록된 CSKU를
/// 검색해서 고르는 것을 기본으로 한다(FboOrderForm의 CSKU 검색 콤보와 같은 패턴) — 오타로 잘못된
/// CSKU가 들어가는 걸 막고, 납품가·품목명이 자동으로 채워진다. 검색해도 없으면 [새 CSKU 추가]로
/// 거래처별 CSKU 관리창을 바로 열어 등록한 뒤 돌아와 고를 수 있다.
/// </summary>
public class PartnerManualOrderDialog : Form
{
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly string _channelCode;
    private readonly string _channelName;
    private List<ChannelSkuModel> _cskus = [];

    private readonly ComboBox _cskuCombo = new();
    private readonly TextBox _itemNameBox = new();
    private readonly TextBox _qtyBox = new() { Text = "1" };
    private readonly TextBox _unitPriceBox = new();
    private readonly TextBox _costPriceBox = new();
    // 발주일/출고일은 임의 날짜로 지정 가능해야 한다(사용자 명시 요청) — DateTimePicker는
    // Min/MaxDate를 따로 제한하지 않는 한 과거/미래 어떤 날짜든 자유롭게 고를 수 있다.
    private readonly DateTimePicker _datePicker = new() { Format = DateTimePickerFormat.Short, Value = DateTime.Today };
    private readonly TextBox _noteBox = new() { Text = "수동입력(거래처 마감보드)" };

    public string CskuCode { get; private set; } = "";
    public string ItemName => _itemNameBox.Text.Trim();
    public decimal Qty { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal? CostPrice { get; private set; }
    public DateTime OrderDate => _datePicker.Value.Date;
    public string Note => _noteBox.Text.Trim();

    public PartnerManualOrderDialog(string channelCode, string channelName)
    {
        _channelCode = channelCode;
        _channelName = channelName;
        InitializeComponent(channelName);
        LoadCskus();
    }

    private void InitializeComponent(string channelName)
    {
        Text = $"수동 주문 추가 — {channelName}";
        Size = new Size(420, 400);
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

        _cskuCombo.DropDownStyle = ComboBoxStyle.DropDown;
        _cskuCombo.MaxDropDownItems = 8;
        _cskuCombo.TextUpdate += OnCskuSearchTextUpdate;
        _cskuCombo.SelectedIndexChanged += OnCskuComboSelectedIndexChanged;
        AddRow("CSKU 검색:", _cskuCombo);

        var btnAddCsku = new Button { Text = "새 CSKU 추가...", Dock = DockStyle.Fill, Height = 28 };
        btnAddCsku.Click += OnAddCskuClick;
        AddRow("", btnAddCsku);

        AddRow("품목명:", _itemNameBox);
        AddRow("수량:", _qtyBox);
        AddRow("납품가(단가):", _unitPriceBox);
        AddRow("원가(선택):", _costPriceBox);
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

    private void LoadCskus()
    {
        _cskus = _channelSkuRepository.GetAllByChannel(_channelCode);
        RefreshDropdownItems(string.Empty);
    }

    private static string FormatCskuOption(ChannelSkuModel c) =>
        $"{c.CskuCode} - {(string.IsNullOrWhiteSpace(c.InvoiceDisplayName) ? "(품목명 없음)" : c.InvoiceDisplayName)} ({c.SupplyPrice:N0}원)";

    private void RefreshDropdownItems(string keepText)
    {
        _cskuCombo.BeginUpdate();
        _cskuCombo.Items.Clear();
        foreach (var c in _cskus) _cskuCombo.Items.Add(FormatCskuOption(c));
        _cskuCombo.EndUpdate();
        _cskuCombo.Text = keepText;
    }

    /// <summary>
    /// TextChanged 대신 TextUpdate를 쓴다(FboOrderForm의 CSKU 검색과 같은 이유 — 드롭다운에서
    /// 항목을 선택해 Text가 채워질 때는 발생하지 않아 선택 직후 드롭다운이 다시 열리지 않는다).
    /// CSKU 코드/품목명(송장표시명)으로 필터링해 드롭다운 목록을 갈아끼운다.
    /// </summary>
    private void OnCskuSearchTextUpdate(object? sender, EventArgs e)
    {
        var search = _cskuCombo.Text.Trim();
        var matches = string.IsNullOrEmpty(search)
            ? _cskus
            : _cskus.Where(c =>
                c.CskuCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (c.InvoiceDisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
              .ToList();

        var text = _cskuCombo.Text;
        var selStart = _cskuCombo.SelectionStart;
        var selLength = _cskuCombo.SelectionLength;

        _cskuCombo.BeginUpdate();
        _cskuCombo.Items.Clear();
        foreach (var c in matches) _cskuCombo.Items.Add(FormatCskuOption(c));
        _cskuCombo.EndUpdate();

        _cskuCombo.Text = text;
        _cskuCombo.SelectionStart = selStart;
        _cskuCombo.SelectionLength = selLength;

        _cskuCombo.DroppedDown = !string.IsNullOrEmpty(search) && matches.Count > 0;
    }

    /// <summary>드롭다운에서 CSKU를 고르면 납품가/품목명을 자동으로 채운다(직접 덮어쓰기 가능).</summary>
    private void OnCskuComboSelectedIndexChanged(object? sender, EventArgs e)
    {
        var selected = ParseSelectedCsku();
        if (selected == null) return;

        _unitPriceBox.Text = selected.SupplyPrice.ToString("0");
        if (!string.IsNullOrWhiteSpace(selected.InvoiceDisplayName))
            _itemNameBox.Text = selected.InvoiceDisplayName;
    }

    private ChannelSkuModel? ParseSelectedCsku()
    {
        var text = _cskuCombo.Text.Trim();
        if (text.Length == 0) return null;
        var code = text.Contains(" - ") ? text[..text.IndexOf(" - ", StringComparison.Ordinal)] : text;
        return _cskus.FirstOrDefault(c => c.CskuCode.Equals(code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>검색해도 원하는 CSKU가 없을 때, 거래처별 CSKU 관리창을 바로 열어 등록하고 돌아와
    /// 고를 수 있게 한다(사용자 요청). 모달로 열어 닫히면 바로 목록을 새로고침한다.</summary>
    private void OnAddCskuClick(object? sender, EventArgs e)
    {
        using var cskuForm = new ChannelCskuForm(_channelCode);
        FormManager.ApplyBoundsTracking(cskuForm);
        cskuForm.ShowDialog(this);

        var keepText = _cskuCombo.Text;
        LoadCskus();
        RefreshDropdownItems(keepText);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var selected = ParseSelectedCsku();
        var typedCode = _cskuCombo.Text.Trim().Contains(" - ")
            ? _cskuCombo.Text.Trim()[.._cskuCombo.Text.Trim().IndexOf(" - ", StringComparison.Ordinal)]
            : _cskuCombo.Text.Trim();

        if (string.IsNullOrWhiteSpace(typedCode))
        {
            MessageBox.Show("CSKU를 검색해서 선택하세요(없으면 [새 CSKU 추가]로 먼저 등록하세요).", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (selected == null)
        {
            MessageBox.Show($"'{typedCode}'는 이 채널에 등록된 CSKU가 아닙니다. 목록에서 검색해 고르거나 [새 CSKU 추가]로 먼저 등록하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        CskuCode = selected.CskuCode;
        Qty = qty;
        UnitPrice = unitPrice;
        CostPrice = costPrice;
        if (string.IsNullOrWhiteSpace(ItemName)) _itemNameBox.Text = CskuCode;

        DialogResult = DialogResult.OK;
        Close();
    }
}

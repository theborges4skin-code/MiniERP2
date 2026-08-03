using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// CskuPickerDialog에서 원하는 CSKU가 없을 때 바로 새로 등록할 수 있게 하는 다이얼로그입니다
/// (사용자 요청 — 견적관리의 "CSKU 불러오기"에서 CSKU가 없으면 새로 등록, 마스터SKU조차 없으면
/// 마스터SKU 등록부터 순차로 안내). 마스터SKU를 검색해서 고르거나, 없으면 "새 마스터SKU
/// 등록..."으로 NewMasterSkuDialog를 띄워 만든 뒤 이어서 CSKU를 등록한다.
/// </summary>
public class NewCskuRegistrationDialog : Form
{
    private readonly ItemRepository _itemRepository = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly SalesChannelRepository _channelRepository = new();

    private ComboBox _channelCombo = new();
    private TextBox _searchBox = new();
    private DataGridView _candidateGrid = new();
    private TextBox _selectedMskuText = new();
    private TextBox _cskuCodeText = new();
    private TextBox _unitText = new();
    private TextBox _packingText = new();
    private TextBox _supplyPriceText = new();
    private RadioButton _radioVatIncluded = new();
    private RadioButton _radioVatExcluded = new();
    private TextBox _invoiceDisplayNameText = new();

    private ItemModel? _selectedMaster;

    public string? ResultChannelCode { get; private set; }
    public string? ResultCskuCode { get; private set; }
    public string? ResultMsku { get; private set; }
    public string? ResultItemName { get; private set; }
    public decimal ResultUnitPrice { get; private set; }
    public decimal ResultCostPrice { get; private set; }
    public string? ResultUnit { get; private set; }
    public string? ResultPacking { get; private set; }

    /// <param name="priorityChannelCode">CSKU를 등록할 채널을 미리 선택해둔다(호출한
    /// CskuPickerDialog에서 필터 중이던 채널). null이면 사용자가 직접 고른다.</param>
    /// <param name="initialSearch">CskuPickerDialog의 검색창에 이미 입력해둔 검색어가 있으면
    /// 넘겨받아 마스터SKU 검색창과 새 마스터SKU 품명 기본값에 그대로 이어서 쓴다.</param>
    public NewCskuRegistrationDialog(string? priorityChannelCode, string? initialSearch)
    {
        InitializeComponent(priorityChannelCode, initialSearch);
    }

    private void InitializeComponent(string? priorityChannelCode, string? initialSearch)
    {
        Text = "새 CSKU 등록";
        Size = new Size(680, 600);
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterParent;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(10) };
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _channelCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        var channels = _channelRepository.GetAll().OrderBy(c => c.ChannelName).ToList();
        _channelCombo.DataSource = channels;
        _channelCombo.DisplayMember = nameof(SalesChannel.ChannelName);
        _channelCombo.ValueMember = nameof(SalesChannel.ChannelCode);
        if (priorityChannelCode != null)
        {
            var match = channels.FirstOrDefault(c => c.ChannelCode == priorityChannelCode);
            if (match != null) _channelCombo.SelectedItem = match;
        }
        _channelCombo.SelectedIndexChanged += (s, e) => SuggestCskuCode();

        AddRow(form, 0, "채널", _channelCombo, 30);

        var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        _searchBox = new TextBox { Width = 300, Text = initialSearch ?? string.Empty, PlaceholderText = "마스터SKU 검색(SKU/품명)" };
        _searchBox.TextChanged += (s, e) => RunSearch();
        var btnNewMaster = new Button { Text = "새 마스터SKU 등록...", Size = new Size(140, 26), Margin = new Padding(8, 0, 0, 0) };
        btnNewMaster.Click += OnNewMasterSkuClick;
        searchPanel.Controls.Add(_searchBox);
        searchPanel.Controls.Add(btnNewMaster);
        form.Controls.Add(new Label { Text = "마스터SKU", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 4, 0) }, 0, 1);
        form.Controls.Add(searchPanel, 1, 1);
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        _candidateGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _candidateGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Sku", HeaderText = "SKU", DataPropertyName = "Sku", Width = 130 },
            new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "품명", DataPropertyName = "ItemName", Width = 200 },
            new DataGridViewTextBoxColumn { Name = "CostPrice", HeaderText = "제조원가", DataPropertyName = "CostPrice", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _candidateGrid.SelectionChanged += (s, e) => OnMasterSelected();
        form.Controls.Add(_candidateGrid, 0, 2);
        form.SetColumnSpan(_candidateGrid, 2);
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));

        _selectedMskuText = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = SystemColors.Control };
        AddRow(form, 3, "선택된 SKU", _selectedMskuText, 26);

        _cskuCodeText = new TextBox { Dock = DockStyle.Fill };
        AddRow(form, 4, "CSKU 코드", _cskuCodeText, 26);

        var unitPackingPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        unitPackingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        unitPackingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        unitPackingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _unitText = new TextBox { Dock = DockStyle.Fill, Text = "kg" };
        _packingText = new TextBox { Dock = DockStyle.Fill };
        unitPackingPanel.Controls.Add(_unitText, 0, 0);
        unitPackingPanel.Controls.Add(new Label { Text = "포장단위", AutoSize = true, Padding = new Padding(6, 4, 4, 0) }, 1, 0);
        unitPackingPanel.Controls.Add(_packingText, 2, 0);
        AddRow(form, 5, "단위", unitPackingPanel, 26);

        var pricePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        _supplyPriceText = new TextBox { Width = 100 };
        _radioVatIncluded = new RadioButton { Text = "VAT포함", AutoSize = true, Checked = true, Padding = new Padding(10, 4, 0, 0) };
        _radioVatExcluded = new RadioButton { Text = "VAT별도", AutoSize = true, Padding = new Padding(5, 4, 0, 0) };
        pricePanel.Controls.Add(_supplyPriceText);
        pricePanel.Controls.Add(_radioVatIncluded);
        pricePanel.Controls.Add(_radioVatExcluded);
        AddRow(form, 6, "납품가", pricePanel, 30);

        _invoiceDisplayNameText = new TextBox { Dock = DockStyle.Fill };
        AddRow(form, 7, "송장표시명", _invoiceDisplayNameText, 26);

        outer.Controls.Add(form, 0, 0);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnCancel = new Button { Text = "취소", Size = new Size(80, 30) };
        var btnRegister = new Button { Text = "등록", Size = new Size(90, 30), Font = new Font(Font, FontStyle.Bold) };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnRegister.Click += OnRegisterClick;
        buttonPanel.Controls.Add(btnRegister);
        buttonPanel.Controls.Add(btnCancel);
        outer.Controls.Add(buttonPanel, 0, 1);

        Controls.Add(outer);
        CancelButton = btnCancel;

        RunSearch();
    }

    private static void AddRow(TableLayoutPanel form, int row, string label, Control control, int height)
    {
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        form.Controls.Add(new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 4, 0) }, 0, row);
        control.Margin = new Padding(3, 3, 3, 3);
        form.Controls.Add(control, 1, row);
    }

    private void RunSearch()
    {
        var query = _searchBox.Text.Trim();
        var allItems = _itemRepository.GetAll();
        var matches = string.IsNullOrEmpty(query)
            ? allItems
            : allItems.Where(i => i.Sku.Contains(query, StringComparison.OrdinalIgnoreCase) || i.ItemName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _candidateGrid.DataSource = new BindingList<ItemModel>(matches);
    }

    private void OnMasterSelected()
    {
        _selectedMaster = _candidateGrid.CurrentRow?.DataBoundItem as ItemModel;
        _selectedMskuText.Text = _selectedMaster?.Sku ?? string.Empty;
        if (_selectedMaster != null) _unitText.Text = _selectedMaster.Unit;
        SuggestCskuCode();
    }

    /// <summary>마스터SKU 없이 CSKU를 등록할 수는 없다(§3.2 — CSKU는 반드시 마스터SKU에 연결).
    /// 여기서 새로 만든 마스터SKU를 후보 목록/선택 상태에 바로 반영해 이어서 CSKU 등록을
    /// 계속할 수 있게 한다 — 사용자가 요청한 "순차적으로 자동 안내" 흐름.</summary>
    private void OnNewMasterSkuClick(object? sender, EventArgs e)
    {
        using var dialog = new NewMasterSkuDialog(_searchBox.Text.Trim());
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK || dialog.ResultSku == null) return;

        RunSearch();
        _selectedMaster = _itemRepository.GetBySku(dialog.ResultSku);
        _selectedMskuText.Text = _selectedMaster?.Sku ?? dialog.ResultSku;
        _unitText.Text = _selectedMaster?.Unit ?? dialog.ResultUnit ?? "kg";
        SuggestCskuCode();

        MessageBox.Show(
            $"마스터SKU '{dialog.ResultSku}'을(를) 등록했습니다. 이어서 CSKU 정보를 입력하고 등록하세요.",
            "마스터SKU 등록 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SuggestCskuCode()
    {
        if (_selectedMaster == null) return;
        if (!string.IsNullOrWhiteSpace(_cskuCodeText.Text)) return; // 사용자가 이미 손댔으면 덮어쓰지 않음
        var channelName = (_channelCombo.SelectedItem as SalesChannel)?.ChannelName ?? string.Empty;
        _cskuCodeText.Text = CskuCodeGenerator.BuildDefault(channelName, _selectedMaster.Sku);
    }

    private void OnRegisterClick(object? sender, EventArgs e)
    {
        if (_channelCombo.SelectedItem is not SalesChannel channel)
        {
            MessageBox.Show("채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_selectedMaster == null)
        {
            MessageBox.Show("마스터SKU를 목록에서 선택하거나 '새 마스터SKU 등록...'으로 먼저 만드세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var cskuCode = _cskuCodeText.Text.Trim();
        if (string.IsNullOrEmpty(cskuCode))
        {
            MessageBox.Show("CSKU 코드를 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_channelSkuRepository.GetByChannelAndCskuCode(channel.ChannelCode, cskuCode) != null)
        {
            MessageBox.Show($"채널 '{channel.ChannelName}'에 이미 CSKU '{cskuCode}'가 있습니다. 다른 코드를 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        decimal.TryParse(_supplyPriceText.Text, out var enteredPrice);
        var supplyPrice = enteredPrice > 0 && _radioVatExcluded.Checked ? Math.Round(enteredPrice * 1.1m, 0) : enteredPrice;

        _channelSkuRepository.Upsert(new ChannelSkuModel
        {
            ChannelCode = channel.ChannelCode,
            CskuCode = cskuCode,
            Msku = _selectedMaster.Sku,
            SupplyPrice = supplyPrice,
            InvoiceDisplayName = string.IsNullOrWhiteSpace(_invoiceDisplayNameText.Text) ? null : _invoiceDisplayNameText.Text.Trim(),
            Unit = string.IsNullOrWhiteSpace(_unitText.Text) ? "kg" : _unitText.Text.Trim(),
            Packing = string.IsNullOrWhiteSpace(_packingText.Text) ? null : _packingText.Text.Trim(),
        });

        ResultChannelCode = channel.ChannelCode;
        ResultCskuCode = cskuCode;
        ResultMsku = _selectedMaster.Sku;
        ResultItemName = string.IsNullOrWhiteSpace(_invoiceDisplayNameText.Text) ? _selectedMaster.ItemName : _invoiceDisplayNameText.Text.Trim();
        ResultUnitPrice = supplyPrice;
        ResultCostPrice = _selectedMaster.CostPrice;
        ResultUnit = _unitText.Text.Trim();
        ResultPacking = string.IsNullOrWhiteSpace(_packingText.Text) ? null : _packingText.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}

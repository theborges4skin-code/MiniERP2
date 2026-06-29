using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 발주서에서 읽어온 주문 1건(상품명/옵션명/수량)을 보여주면서 마스터DB의 SKU를 검색해
/// 매핑을 선택하게 하는 도우미 창입니다. 검색 결과에는 상품명/제조원가를 함께 보여줘
/// 어떤 품목인지 참고할 수 있게 합니다. 마스터DB에 없는 품목은 임시 SKU로 등록할 수 있습니다.
/// </summary>
public class OrderSkuMappingDialog : Form
{
    private readonly ItemRepository _itemRepository = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly MappingRepository _mappingRepository = new();
    private readonly OfsOrderItem _orderItem;
    private readonly string? _channelCode;

    private TextBox _searchBox = new();
    private DataGridView _candidateGrid = new();
    private TextBox _txtCskuCode = new();
    private TextBox _txtSupplyPrice = new();
    private RadioButton _radioVatIncluded = new();
    private RadioButton _radioVatExcluded = new();
    private TextBox _txtInvoiceDisplayName = new();
    private CheckBox _chkSaveAsExactRule = new();

    public string? ResultMappedSku { get; private set; }
    public string? ResultStatus { get; private set; }

    public OrderSkuMappingDialog(OfsOrderItem orderItem, string? channelCode)
    {
        _orderItem = orderItem;
        _channelCode = channelCode;
        InitializeComponent();
        RunSearch();
    }

    private void InitializeComponent()
    {
        Text = "SKU 매핑 도우미";
        Size = new Size(680, 620);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 8, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

        var infoPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        infoPanel.Controls.Add(new Label { Text = $"상품명: {_orderItem.ProductName}", AutoSize = true, Dock = DockStyle.Fill }, 0, 0);
        infoPanel.Controls.Add(new Label { Text = $"옵션명: {_orderItem.OptionName}", AutoSize = true, Dock = DockStyle.Fill }, 1, 0);
        infoPanel.Controls.Add(new Label { Text = $"수량: {_orderItem.Quantity}", AutoSize = true, Dock = DockStyle.Fill }, 2, 0);

        var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        searchPanel.Controls.Add(new Label { Text = "마스터DB 검색(SKU/상품명):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        _searchBox = new TextBox { Width = 300, Text = _orderItem.ProductName ?? string.Empty };
        _searchBox.TextChanged += (s, e) => RunSearch();
        searchPanel.Controls.Add(_searchBox);

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
            new DataGridViewTextBoxColumn { Name = "Sku", HeaderText = "SKU", DataPropertyName = "Sku", Width = 150 },
            new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "상품명", DataPropertyName = "ItemName", Width = 250 },
            new DataGridViewTextBoxColumn { Name = "CostPrice", HeaderText = "제조원가(VAT포함)", DataPropertyName = "CostPrice", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _candidateGrid.SelectionChanged += (s, e) => PrefillFromExistingChannelSku();

        var cskuCodePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        cskuCodePanel.Controls.Add(new Label { Text = "CSKU 코드(자동제안, 편집 가능):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        _txtCskuCode = new TextBox { Width = 220 };
        cskuCodePanel.Controls.Add(_txtCskuCode);
        cskuCodePanel.Controls.Add(new Label
        {
            Text = "같은 마스터SKU도 채널 옵션에 따라 CSKU 코드를 다르게 부여할 수 있습니다.",
            AutoSize = true,
            Padding = new Padding(10, 6, 0, 0),
            ForeColor = Color.DimGray,
        });

        var pricePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        pricePanel.Controls.Add(new Label { Text = "납품단가(선택):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        _txtSupplyPrice = new TextBox { Width = 100 };
        pricePanel.Controls.Add(_txtSupplyPrice);
        _radioVatIncluded = new RadioButton { Text = "VAT포함", AutoSize = true, Checked = true, Padding = new Padding(10, 4, 0, 0) };
        _radioVatExcluded = new RadioButton { Text = "VAT별도", AutoSize = true, Padding = new Padding(5, 4, 0, 0) };
        pricePanel.Controls.Add(_radioVatIncluded);
        pricePanel.Controls.Add(_radioVatExcluded);

        var invoiceNamePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        invoiceNamePanel.Controls.Add(new Label { Text = "송장표시명(선택, 채널별):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        // 후보가 하나도 없어 PrefillFromExistingChannelSku가 한 번도 안 불리는 경우에도 빈칸으로
        // 남지 않게, 여기서도 발주서 원본 상품명을 기본값으로 채워둔다.
        _txtInvoiceDisplayName = new TextBox { Width = 350, Text = _orderItem.ProductName ?? string.Empty };
        invoiceNamePanel.Controls.Add(_txtInvoiceDisplayName);

        var exactRulePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        _chkSaveAsExactRule = new CheckBox { Text = "다음에도 같은 상품명/옵션명은 자동으로 이 SKU로 매핑(1:1 규칙으로 저장)", AutoSize = true, Checked = true };
        exactRulePanel.Controls.Add(_chkSaveAsExactRule);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnClose = new Button { Text = "닫기", Width = 80 };
        var btnMap = new Button { Text = "이 SKU로 매핑", Width = 110 };
        var btnTemp = new Button { Text = "임시 SKU 등록", Width = 110 };
        btnClose.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnMap.Click += OnMapClick;
        btnTemp.Click += OnRegisterTempSkuClick;
        buttonPanel.Controls.Add(btnClose);
        buttonPanel.Controls.Add(btnMap);
        buttonPanel.Controls.Add(btnTemp);

        layout.Controls.Add(infoPanel, 0, 0);
        layout.Controls.Add(searchPanel, 0, 1);
        layout.Controls.Add(_candidateGrid, 0, 2);
        layout.Controls.Add(cskuCodePanel, 0, 3);
        layout.Controls.Add(pricePanel, 0, 4);
        layout.Controls.Add(invoiceNamePanel, 0, 5);
        layout.Controls.Add(exactRulePanel, 0, 6);
        layout.Controls.Add(buttonPanel, 0, 7);

        Controls.Add(layout);
        CancelButton = btnClose;
    }

    /// <summary>
    /// 후보 목록에서 SKU를 선택하면 "채널명 앞 3글자_마스터SKU" 형태의 CSKU 코드를 기본값으로
    /// 제안합니다(편집 가능). 그 기본 코드로 이미 저장된 CSKU(납품가/송장표시명)가 있으면 함께
    /// 미리 채워 보여줍니다. 매번 빈 칸에서 다시 입력하지 않고 기존 설정을 바로 확인/수정할 수 있게 합니다.
    /// 송장표시명은 아직 채널별로 설정된 적이 없으면(신규 매핑) 발주서의 원본 상품명을 기본값으로
    /// 채워서, 빈 칸으로 매핑하다가 송장에 상품명이 안 찍히는 실수를 줄인다.
    /// </summary>
    private void PrefillFromExistingChannelSku()
    {
        _txtCskuCode.Text = string.Empty;
        _txtSupplyPrice.Text = string.Empty;
        _txtInvoiceDisplayName.Text = _orderItem.ProductName ?? string.Empty;
        _radioVatIncluded.Checked = true;

        if (_candidateGrid.CurrentRow?.DataBoundItem is not ItemModel selected) return;

        var defaultCode = BuildDefaultCskuCode(selected.Sku);
        _txtCskuCode.Text = defaultCode;

        if (string.IsNullOrEmpty(_channelCode)) return;

        var existing = _channelSkuRepository.GetByChannelAndCskuCode(_channelCode, defaultCode);
        if (existing == null) return;

        _txtSupplyPrice.Text = existing.SupplyPrice.ToString();
        if (!string.IsNullOrWhiteSpace(existing.InvoiceDisplayName))
        {
            _txtInvoiceDisplayName.Text = existing.InvoiceDisplayName;
        }
    }

    private string BuildDefaultCskuCode(string masterSku)
    {
        if (string.IsNullOrEmpty(_channelCode)) return masterSku;

        var channelName = _salesChannelRepository.GetAll().FirstOrDefault(c => c.ChannelCode == _channelCode)?.ChannelName ?? _channelCode;
        return CskuCodeGenerator.BuildDefault(channelName, masterSku);
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

    private void OnMapClick(object? sender, EventArgs e)
    {
        if (_candidateGrid.CurrentRow?.DataBoundItem is not ItemModel selected)
        {
            MessageBox.Show("매핑할 SKU를 목록에서 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var cskuCode = ResolveCskuCodeOrShowError();
        if (cskuCode == null) return;

        SaveChannelSkuInfoIfEntered(cskuCode, selected.Sku);
        SaveAsExactRuleIfChecked(cskuCode);

        ResultMappedSku = cskuCode;
        ResultStatus = "수동 매핑";
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnRegisterTempSkuClick(object? sender, EventArgs e)
    {
        var existingSkus = _itemRepository.GetAll().Select(i => i.Sku);
        var tempSku = TempSkuGenerator.GenerateNext(existingSkus);

        var result = MessageBox.Show(
            $"임시 SKU '{tempSku}'를 마스터DB에 새로 등록하고 이 주문에 매핑하시겠습니까?\n상품명: {_orderItem.ProductName}\n\n등록 후 마스터SKU 관리창에서 원가 등 정보를 보완해주세요.",
            "임시 SKU 등록", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        _itemRepository.Upsert(new ItemModel
        {
            Sku = tempSku,
            ItemName = string.IsNullOrWhiteSpace(_orderItem.ProductName) ? tempSku : _orderItem.ProductName,
            CostPrice = 0m,
        });

        var cskuCode = string.IsNullOrWhiteSpace(_txtCskuCode.Text) ? BuildDefaultCskuCode(tempSku) : _txtCskuCode.Text.Trim();

        SaveChannelSkuInfoIfEntered(cskuCode, tempSku);
        SaveAsExactRuleIfChecked(cskuCode);

        ResultMappedSku = cskuCode;
        ResultStatus = "임시 매핑";
        DialogResult = DialogResult.OK;
        Close();
    }

    private string? ResolveCskuCodeOrShowError()
    {
        var code = _txtCskuCode.Text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            MessageBox.Show("CSKU 코드를 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        return code;
    }

    /// <summary>
    /// CSKU(채널+CSKU코드)가 이미 존재하면 "기존 CSKU에 조건을 추가"하는 것으로 간주해 그 CSKU의
    /// 납품가/송장표시명은 그대로 두고 매핑 규칙만 추가한다(아래 OnMapClick/OnRegisterTempSkuClick의
    /// SaveAsExactRuleIfChecked가 처리). 존재하지 않으면 입력된 납품단가/송장표시명으로 새로 만든다.
    /// 납품단가는 VAT별도로 선택했으면 1.1을 곱해 VAT포함 기준으로 변환한다.
    /// </summary>
    private void SaveChannelSkuInfoIfEntered(string cskuCode, string masterSku)
    {
        if (string.IsNullOrEmpty(_channelCode))
        {
            MessageBox.Show("채널 정보가 없어 CSKU 설정(납품단가/송장표시명)을 저장하지 못했습니다. SKU 매핑만 적용됩니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var existing = _channelSkuRepository.GetByChannelAndCskuCode(_channelCode, cskuCode);
        if (existing != null)
        {
            MessageBox.Show(
                $"기존 CSKU '{cskuCode}'가 이미 존재합니다. 이 상품명/옵션명 조합을 그 CSKU에 매핑하는 조건을 추가합니다.",
                "기존 CSKU 존재", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var hasPrice = decimal.TryParse(_txtSupplyPrice.Text, out var enteredPrice);
        var invoiceDisplayName = string.IsNullOrWhiteSpace(_txtInvoiceDisplayName.Text) ? null : _txtInvoiceDisplayName.Text.Trim();
        var supplyPrice = hasPrice
            ? (_radioVatExcluded.Checked ? Math.Round(enteredPrice * 1.1m, 0) : enteredPrice)
            : 0m;

        _channelSkuRepository.Upsert(new ChannelSkuModel
        {
            ChannelCode = _channelCode,
            CskuCode = cskuCode,
            Msku = masterSku,
            SupplyPrice = supplyPrice,
            InvoiceDisplayName = invoiceDisplayName,
        });
    }

    /// <summary>
    /// 체크박스가 켜져 있으면 이 주문의 (상품명+옵션명) 키를 1:1 매핑 규칙으로 저장해,
    /// 같은 조합의 다음 주문은 이 도우미를 다시 열지 않고도 자동으로 매핑되게 합니다.
    /// </summary>
    private void SaveAsExactRuleIfChecked(string targetSku)
    {
        if (!_chkSaveAsExactRule.Checked) return;
        if (string.IsNullOrEmpty(_channelCode)) return;

        var key = (_orderItem.ProductName ?? string.Empty) + (_orderItem.OptionName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(key)) return;

        _mappingRepository.UpsertExactRule(_channelCode, key, targetSku);
    }
}

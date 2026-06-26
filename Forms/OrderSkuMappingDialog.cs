using System.ComponentModel;
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
    private readonly OfsOrderItem _orderItem;
    private readonly string? _channelCode;

    private TextBox _searchBox = new();
    private DataGridView _candidateGrid = new();
    private TextBox _txtSupplyPrice = new();
    private RadioButton _radioVatIncluded = new();
    private RadioButton _radioVatExcluded = new();

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
        Size = new Size(640, 560);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
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

        _candidateGrid = new DataGridView
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

        var pricePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        pricePanel.Controls.Add(new Label { Text = "납품단가(선택):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        _txtSupplyPrice = new TextBox { Width = 100 };
        pricePanel.Controls.Add(_txtSupplyPrice);
        _radioVatIncluded = new RadioButton { Text = "VAT포함", AutoSize = true, Checked = true, Padding = new Padding(10, 4, 0, 0) };
        _radioVatExcluded = new RadioButton { Text = "VAT별도", AutoSize = true, Padding = new Padding(5, 4, 0, 0) };
        pricePanel.Controls.Add(_radioVatIncluded);
        pricePanel.Controls.Add(_radioVatExcluded);

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
        layout.Controls.Add(pricePanel, 0, 3);
        layout.Controls.Add(buttonPanel, 0, 4);

        Controls.Add(layout);
        CancelButton = btnClose;
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

        SaveSupplyPriceIfEntered(selected.Sku);

        ResultMappedSku = selected.Sku;
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

        SaveSupplyPriceIfEntered(tempSku);

        ResultMappedSku = tempSku;
        ResultStatus = "임시 매핑";
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// 납품단가가 입력된 경우, VAT별도로 선택했으면 1.1을 곱해 VAT포함 기준으로 변환한 뒤
    /// 채널-SKU 납품가로 저장합니다(마스터DB 제조원가와 동일하게 VAT포함 기준으로 통일).
    /// </summary>
    private void SaveSupplyPriceIfEntered(string msku)
    {
        if (string.IsNullOrWhiteSpace(_txtSupplyPrice.Text)) return;
        if (!decimal.TryParse(_txtSupplyPrice.Text, out var enteredPrice)) return;
        if (string.IsNullOrEmpty(_channelCode))
        {
            MessageBox.Show("채널 정보가 없어 납품단가를 저장하지 못했습니다. SKU 매핑만 적용됩니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var vatIncludedPrice = _radioVatExcluded.Checked ? Math.Round(enteredPrice * 1.1m, 0) : enteredPrice;
        _channelSkuRepository.Upsert(new ChannelSkuModel { ChannelCode = _channelCode, Msku = msku, SupplyPrice = vatIncludedPrice });
    }
}

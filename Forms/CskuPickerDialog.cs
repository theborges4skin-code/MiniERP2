using MiniERP2.Database;

namespace MiniERP2.Forms;

/// <summary>
/// 문서관리(DocsForm)의 품목 라인에 CSKU(채널별 SKU) 정보를 검색해 불러오기 위한 선택 다이얼로그.
/// 선택하면 품목명/공급가/마스터SKU/제조원가를 반환해 그리드 행에 채워 넣는다.
/// </summary>
public class CskuPickerDialog : Form
{
    public string? SelectedItemName { get; private set; }
    public decimal SelectedUnitPrice { get; private set; }
    public decimal SelectedCostPrice { get; private set; }
    public string? SelectedMsku { get; private set; }

    private readonly ChannelSkuRepository _cskuRepo = new();
    private readonly ItemRepository _itemRepo = new();
    private readonly SalesChannelRepository _channelRepo = new();

    private ComboBox _channelCombo = new();
    private TextBox _searchBox = new();
    private DataGridView _grid = new();
    private List<PickerRow> _allRows = new();
    private List<PickerRow> _filteredRows = new();

    public CskuPickerDialog(string? defaultChannelCode = null)
    {
        InitializeComponent();
        LoadData(defaultChannelCode);
    }

    private void InitializeComponent()
    {
        Text = "CSKU 불러오기";
        Size = new Size(720, 480);
        MinimumSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var searchBar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        _channelCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        _searchBox = new TextBox { Width = 220, PlaceholderText = "품목명 / CSKU / MSKU 검색" };
        _channelCombo.SelectedIndexChanged += (s, e) => ApplyFilter();
        _searchBox.TextChanged += (s, e) => ApplyFilter();
        searchBar.Controls.AddRange(new Control[]
        {
            new Label { Text = "채널:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) }, _channelCombo,
            new Label { Text = "검색:", AutoSize = true, Padding = new Padding(12, 5, 4, 0) }, _searchBox
        });

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _grid.Columns.Add("ChannelName", "채널");
        _grid.Columns.Add("CskuCode", "CSKU");
        _grid.Columns.Add("Msku", "MSKU");
        _grid.Columns.Add("ItemName", "품목명");
        _grid.Columns.Add("SupplyPrice", "공급가");
        _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Confirm(); };

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var btnOk = new Button { Text = "선택", Width = 90 };
        var btnCancel = new Button { Text = "취소", Width = 90, DialogResult = DialogResult.Cancel };
        btnOk.Click += (s, e) => Confirm();
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnOk);

        layout.Controls.Add(searchBar, 0, 0);
        layout.Controls.Add(_grid, 0, 1);
        layout.Controls.Add(btnPanel, 0, 2);
        Controls.Add(layout);
        CancelButton = btnCancel;
    }

    private void LoadData(string? defaultChannelCode)
    {
        var items = _itemRepo.GetAll().ToDictionary(i => i.Sku, i => i);
        var channels = _channelRepo.GetAll();
        var channelNames = channels.ToDictionary(c => c.ChannelCode, c => c.ChannelName);

        _allRows = _cskuRepo.GetAll().Select(c =>
        {
            items.TryGetValue(c.Msku, out var item);
            string itemName = !string.IsNullOrWhiteSpace(c.InvoiceDisplayName) ? c.InvoiceDisplayName! : (item?.ItemName ?? c.Msku);
            decimal cost = item?.CostPrice ?? 0m;
            string channelName = channelNames.TryGetValue(c.ChannelCode, out var name) ? name : c.ChannelCode;
            return new PickerRow(c.ChannelCode, channelName, c.CskuCode, c.Msku, itemName, c.SupplyPrice, cost);
        }).OrderBy(r => r.ChannelName).ThenBy(r => r.ItemName).ToList();

        _channelCombo.Items.Add("(전체)");
        foreach (var ch in channels.OrderBy(c => c.ChannelName)) _channelCombo.Items.Add(ch.ChannelName);
        _channelCombo.SelectedIndex = 0;
        if (defaultChannelCode != null && channelNames.TryGetValue(defaultChannelCode, out var defaultName))
        {
            int idx = _channelCombo.Items.IndexOf(defaultName);
            if (idx >= 0) _channelCombo.SelectedIndex = idx;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string channelFilter = _channelCombo.SelectedIndex > 0 ? _channelCombo.SelectedItem!.ToString()! : "";
        string search = _searchBox.Text.Trim();

        _filteredRows = _allRows.Where(r =>
            (channelFilter == "" || r.ChannelName == channelFilter) &&
            (search == "" ||
             r.ItemName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             r.CskuCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             r.Msku.Contains(search, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        _grid.Rows.Clear();
        foreach (var r in _filteredRows)
            _grid.Rows.Add(r.ChannelName, r.CskuCode, r.Msku, r.ItemName, r.SupplyPrice);
    }

    private void Confirm()
    {
        int idx = _grid.CurrentRow?.Index ?? -1;
        if (idx < 0 || idx >= _filteredRows.Count)
        {
            MessageBox.Show("품목을 선택하세요.", "알림");
            return;
        }
        var row = _filteredRows[idx];
        SelectedItemName = row.ItemName;
        SelectedUnitPrice = row.SupplyPrice;
        SelectedCostPrice = row.CostPrice;
        SelectedMsku = row.Msku;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record PickerRow(string ChannelCode, string ChannelName, string CskuCode, string Msku,
        string ItemName, decimal SupplyPrice, decimal CostPrice);
}

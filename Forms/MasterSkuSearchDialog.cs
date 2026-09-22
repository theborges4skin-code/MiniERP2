using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 마감/이익분석·OFS의 즉석 매핑 패널(QuickMappingPanel)에서 "검색" 버튼을 눌렀을 때 뜨는 작은
/// 팝업. 패널 인라인 목록(약 5줄)보다 넓게 보고 싶을 때 쓰며, 더블클릭(또는 Enter)으로 바로
/// 선택한다.
/// </summary>
public class MasterSkuSearchDialog : Form
{
    public string? SelectedSku { get; private set; }

    private readonly List<ItemModel> _allItems;
    private TextBox _searchBox = new();
    private DataGridView _grid = new();

    public MasterSkuSearchDialog(List<ItemModel> allItems, string initialQuery)
    {
        _allItems = allItems;
        InitializeComponent(initialQuery);
    }

    private void InitializeComponent(string initialQuery)
    {
        Text = "마스터SKU 검색";
        Size = new Size(520, 420);
        MinimumSize = new Size(400, 300);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _searchBox = new TextBox { Dock = DockStyle.Fill, Text = initialQuery, PlaceholderText = "SKU코드 또는 상품명 검색" };
        _searchBox.TextChanged += (s, e) => ApplyFilter();
        _searchBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Down)
            {
                if (_grid.Rows.Count > 0) { _grid.CurrentCell = _grid.Rows[0].Cells[0]; _grid.Focus(); }
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                if (_grid.Rows.Count > 0) { _grid.CurrentCell = _grid.Rows[0].Cells[0]; Accept(); }
                e.SuppressKeyPress = true;
            }
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Sku", HeaderText = "SKU", DataPropertyName = "Sku", Width = 150 },
            new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "상품명", DataPropertyName = "ItemName", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Accept(); };
        _grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Accept(); e.SuppressKeyPress = true; } };

        layout.Controls.Add(_searchBox, 0, 0);
        layout.Controls.Add(_grid, 0, 1);
        Controls.Add(layout);

        ApplyFilter();
        Shown += (s, e) => { _searchBox.Focus(); _searchBox.SelectAll(); };
    }

    private void ApplyFilter()
    {
        var query = _searchBox.Text.Trim();
        var results = string.IsNullOrEmpty(query)
            ? _allItems
            : _allItems.Where(i => i.Sku.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                    i.ItemName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _grid.DataSource = results.Take(200).ToList();
    }

    private void Accept()
    {
        if (_grid.CurrentRow?.DataBoundItem is not ItemModel item) return;
        SelectedSku = item.Sku;
        DialogResult = DialogResult.OK;
        Close();
    }
}

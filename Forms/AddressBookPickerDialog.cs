using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 배송지 주소록(AddressBookTable)에서 배송지 하나를 검색/선택해 OFS 발주 행에 주입하기 위한
/// 선택 다이얼로그(배송지주소록_개발기획서_확정본.md §4.2). CskuPickerDialog와 같은 패턴 —
/// 특정 채널에 태그된 주소를 목록 맨 위로 올리되, 채널 종속 없는 범용 주소록이라는 원칙(§1)에
/// 따라 태그 없는 주소·다른 채널 태그 주소도 항상 함께 검색/선택 가능하다.
/// </summary>
public class AddressBookPickerDialog : Form
{
    public string? SelectedReceiverName { get; private set; }
    public string? SelectedPhone { get; private set; }
    public string? SelectedAddress { get; private set; }

    private readonly AddressBookRepository _addressBookRepository = new();

    private TextBox _searchBox = new();
    private DataGridView _grid = new();
    private List<AddressBookEntry> _allEntries = new();
    private List<AddressBookEntry> _filteredEntries = new();

    /// <param name="priorityChannelCode">이 채널이 태그된 주소를 목록 맨 위로 올린다(그 외는 라벨순).
    /// 태그가 없거나 다른 채널이 태그된 주소도 항상 함께 노출/검색된다 — 제한하지 않는다.</param>
    public AddressBookPickerDialog(string? priorityChannelCode)
    {
        InitializeComponent();
        LoadData(priorityChannelCode);
    }

    private void InitializeComponent()
    {
        Text = "배송지 불러오기";
        Size = new Size(760, 480);
        MinimumSize = new Size(600, 360);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var searchBar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        _searchBox = new TextBox { Width = 280, PlaceholderText = "라벨 / 수취인 / 주소 검색" };
        _searchBox.TextChanged += (s, e) => ApplyFilter();
        searchBar.Controls.Add(new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
        searchBar.Controls.Add(_searchBox);

        _grid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _grid.Columns.Add("Label", "라벨");
        _grid.Columns.Add("ReceiverName", "수취인");
        _grid.Columns.Add("Phone", "연락처");
        _grid.Columns.Add("Address", "주소");
        _grid.Columns.Add("Tags", "채널태그");
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

    private void LoadData(string? priorityChannelCode)
    {
        _allEntries = _addressBookRepository.GetAll()
            .Where(a => a.IsActive)
            .OrderByDescending(a => priorityChannelCode != null &&
                a.ChannelTags.Contains(priorityChannelCode, StringComparer.OrdinalIgnoreCase))
            .ThenBy(a => a.Label)
            .ToList();

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var search = _searchBox.Text.Trim();

        _filteredEntries = _allEntries.Where(a =>
            search.Length == 0 ||
            a.Label.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            a.ReceiverName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            a.Address.Contains(search, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        _grid.Rows.Clear();
        foreach (var a in _filteredEntries)
            _grid.Rows.Add(a.Label, a.ReceiverName, a.Phone, a.Address, string.Join(", ", a.ChannelTags));
    }

    private void Confirm()
    {
        int idx = _grid.CurrentRow?.Index ?? -1;
        if (idx < 0 || idx >= _filteredEntries.Count)
        {
            MessageBox.Show("배송지를 선택하세요.", "알림");
            return;
        }

        var entry = _filteredEntries[idx];
        SelectedReceiverName = entry.ReceiverName;
        SelectedPhone = entry.Phone;
        SelectedAddress = entry.Address;
        DialogResult = DialogResult.OK;
        Close();
    }
}

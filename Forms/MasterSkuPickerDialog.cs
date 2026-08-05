using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 마스터SKU만 검색해서 고르는 최소 다이얼로그입니다(샘플발송이력관리_개발기획서.md §4.1(a), §2 D4).
/// CSKU 코드/납품가/단위 등은 전혀 묻지 않습니다 — 호출 측(ManualOrderDialog)이 선택된 마스터SKU로
/// 그 채널의 CSKU를 자동 생성하기 때문에, "CSKU 관리창을 여는 절차 없음"이라는 D4 요구를 만족하려면
/// 이 창은 SKU 선택 그 이상을 요구하면 안 됩니다.
/// </summary>
public class MasterSkuPickerDialog : Form
{
    private readonly ItemRepository _itemRepository = new();
    private TextBox _searchBox = new();
    private DataGridView _grid = new();
    private List<ItemModel> _filtered = [];

    public string? SelectedSku { get; private set; }
    public string? SelectedItemName { get; private set; }

    public MasterSkuPickerDialog(string? initialSearch = null)
    {
        InitializeComponent(initialSearch);
    }

    private void InitializeComponent(string? initialSearch)
    {
        Text = "마스터SKU에서 찾기";
        Size = new Size(480, 480);
        MinimumSize = new Size(380, 320);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _searchBox = new TextBox { Dock = DockStyle.Fill, Text = initialSearch ?? string.Empty, PlaceholderText = "SKU/품명 검색" };
        _searchBox.TextChanged += (s, e) => RunSearch();
        layout.Controls.Add(_searchBox, 0, 0);

        _grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Sku", HeaderText = "SKU", Width = 140 },
            new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "품명", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _grid.CellDoubleClick += (s, e) => Confirm();
        layout.Controls.Add(_grid, 0, 1);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnCancel = new Button { Text = "취소", Size = new Size(80, 30) };
        var btnOk = new Button { Text = "확인", Size = new Size(80, 30), Font = new Font(Font, FontStyle.Bold) };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnOk.Click += (s, e) => Confirm();
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);
        layout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(layout);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        RunSearch();
        _searchBox.Focus();
    }

    private void RunSearch()
    {
        var query = _searchBox.Text.Trim();
        var all = _itemRepository.GetAll();
        _filtered = string.IsNullOrEmpty(query)
            ? all
            : all.Where(i => i.Sku.Contains(query, StringComparison.OrdinalIgnoreCase) || i.ItemName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        _grid.Rows.Clear();
        foreach (var item in _filtered) _grid.Rows.Add(item.Sku, item.ItemName);
    }

    private void Confirm()
    {
        var idx = _grid.CurrentRow?.Index ?? -1;
        if (idx < 0 || idx >= _filtered.Count)
        {
            MessageBox.Show("마스터SKU를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = _filtered[idx];
        SelectedSku = selected.Sku;
        SelectedItemName = selected.ItemName;
        DialogResult = DialogResult.OK;
        Close();
    }
}

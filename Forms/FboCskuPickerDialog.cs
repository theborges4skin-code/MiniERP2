using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 풀필먼트 발주 처리(FboOrderForm)에서 등록된 FBO CSKU를 검색해 여러 건을 한 번에 선택하기 위한
/// 다이얼로그. 기존 CSKU 콤보(자동완성)는 한 번에 하나씩만 고를 수 있어 여러 품목을 담아야 하는
/// 발주서 작성 시 반복 검색이 불편했던 것을 개선 — 체크박스로 전체/일부를 골라 한 번에 추가하거나,
/// 행을 더블클릭해 그 한 건만 바로 추가할 수 있다.
/// </summary>
public class FboCskuPickerDialog : Form
{
    public List<FboCskuModel> SelectedCskus { get; } = new();

    private readonly List<FboCskuModel> _allCskus;
    private List<FboCskuModel> _filteredCskus = new();

    private TextBox _searchBox = new();
    private DataGridView _grid = new();

    public FboCskuPickerDialog(List<FboCskuModel> cskus)
    {
        _allCskus = cskus;
        InitializeComponent();
        ApplyFilter();
    }

    private void InitializeComponent()
    {
        Text = "CSKU 검색하여 추가";
        Size = new Size(680, 480);
        MinimumSize = new Size(520, 360);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var searchBar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        _searchBox = new TextBox { Width = 260, PlaceholderText = "CSKU / 품목명 / FBO상품코드 검색" };
        _searchBox.TextChanged += (s, e) => ApplyFilter();
        searchBar.Controls.AddRange(new Control[]
        {
            new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) }, _searchBox
        });

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnEnter,
        };
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "선택", Width = 45, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, FillWeight = 1 });
        _grid.Columns.Add("Csku", "CSKU");
        _grid.Columns.Add("ItemName", "품목명");
        _grid.Columns.Add("QtyPerBox", "박스당수량");
        _grid.Columns.Add("BoxType", "박스타입");
        _grid.Columns.Add("FboItemCode", "FBO상품코드");
        _grid.Columns["Csku"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns["Csku"]!.Width = 100;
        _grid.Columns["QtyPerBox"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns["QtyPerBox"]!.Width = 80;
        _grid.Columns["BoxType"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns["BoxType"]!.Width = 70;
        // 체크박스 열 더블클릭은 단순 토글, 그 외 열 더블클릭은 그 한 건만 바로 추가(빠른 단건 추가).
        _grid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == 0)
            {
                var cell = _grid.Rows[e.RowIndex].Cells["Selected"];
                cell.Value = !(cell.Value is true);
                return;
            }
            _grid.EndEdit();
            Confirm(singleRowIndex: e.RowIndex);
        };

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var btnOk = new Button { Text = "선택 항목 추가", Width = 110 };
        var btnCancel = new Button { Text = "취소", Width = 90, DialogResult = DialogResult.Cancel };
        var btnSelectAll = new Button { Text = "전체선택", Width = 90 };
        var btnSelectNone = new Button { Text = "전체해제", Width = 90 };
        btnSelectAll.Click += (s, e) => ToggleAll(true);
        btnSelectNone.Click += (s, e) => ToggleAll(false);
        btnOk.Click += (s, e) => Confirm();
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnOk);
        btnPanel.Controls.Add(btnSelectNone);
        btnPanel.Controls.Add(btnSelectAll);

        layout.Controls.Add(searchBar, 0, 0);
        layout.Controls.Add(_grid, 0, 1);
        layout.Controls.Add(btnPanel, 0, 2);
        Controls.Add(layout);
        CancelButton = btnCancel;
    }

    private void ApplyFilter()
    {
        var search = _searchBox.Text.Trim();
        _filteredCskus = _allCskus.Where(c =>
            search == "" ||
            c.Csku.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            c.ItemName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            c.FboItemCode.Contains(search, StringComparison.OrdinalIgnoreCase)
        ).OrderBy(c => c.Csku).ToList();

        _grid.Rows.Clear();
        foreach (var c in _filteredCskus)
            _grid.Rows.Add(false, c.Csku, c.ItemName, c.QtyPerBox, c.BoxType, c.FboItemCode);
    }

    private void ToggleAll(bool value)
    {
        foreach (DataGridViewRow row in _grid.Rows) row.Cells["Selected"].Value = value;
    }

    /// <summary>singleRowIndex가 지정되면(더블클릭) 체크 상태와 무관하게 그 행 한 건만 추가한다.
    /// null이면(버튼 클릭) 체크된 행 전부를 추가한다.</summary>
    private void Confirm(int? singleRowIndex = null)
    {
        SelectedCskus.Clear();

        if (singleRowIndex is { } idx)
        {
            SelectedCskus.Add(_filteredCskus[idx]);
        }
        else
        {
            _grid.EndEdit();
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                if (_grid.Rows[i].Cells["Selected"].Value is true) SelectedCskus.Add(_filteredCskus[i]);
            }
        }

        if (SelectedCskus.Count == 0)
        {
            MessageBox.Show("추가할 CSKU를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}

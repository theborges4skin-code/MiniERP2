using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 풀필먼트 발주 처리(FboOrderForm)에서 "지난 발주 불러오기"로 과거 발주 1건을 골라 그 구성을
/// 그대로 복사해오기 위한 선택 다이얼로그. FboHistoryForm(발주 이력 조회)의 "복사하여 신규 발주"와
/// 같은 목적이지만, 발주 작성 화면에서 다른 창을 거치지 않고 바로 고를 수 있게 한다.
/// </summary>
public class FboOrderPickerDialog : Form
{
    public string? SelectedFboNo { get; private set; }

    private readonly FboOrderRepository _orderRepository = new();
    private readonly FboChannelConfigRepository _channelConfigRepository = new();

    private TextBox _searchBox = new();
    private DataGridView _grid = new();
    private List<OrderSummary> _allOrders = [];
    private List<OrderSummary> _filteredOrders = [];

    public FboOrderPickerDialog()
    {
        InitializeComponent();
        LoadData();
    }

    private void InitializeComponent()
    {
        Text = "지난 발주 불러오기";
        Size = new Size(640, 460);
        MinimumSize = new Size(500, 340);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var searchBar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        _searchBox = new TextBox { Width = 260, PlaceholderText = "발주번호/채널 검색" };
        _searchBox.TextChanged += (s, e) => ApplyFilter();
        searchBar.Controls.AddRange(new Control[]
        {
            new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) }, _searchBox
        });

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
        _grid.Columns.Add("FboNo", "발주번호");
        _grid.Columns.Add("OrderDate", "발주일");
        _grid.Columns.Add("ChannelName", "채널");
        _grid.Columns.Add("BoxCount", "박스수");
        _grid.Columns.Add("TotalQty", "총수량");
        _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Confirm(); };

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var btnOk = new Button { Text = "불러오기", Width = 90 };
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

    private void LoadData()
    {
        var channelNames = _channelConfigRepository.GetAll().ToDictionary(c => c.ChannelId, c => c.ChannelName);
        // 과거 발주 전체(최근 2년)를 박스-품목 단위로 조회해 발주번호 기준으로 요약한다 — 발주
        // 목록 전용 조회 메서드가 따로 없어 기존 GetHistory(이력 조회창에서 쓰는 것과 동일)를 재사용.
        var rows = _orderRepository.GetHistory(null, DateTime.Today.AddYears(-2), DateTime.Today);

        _allOrders = rows.GroupBy(r => r.FboNo)
            .Select(g =>
            {
                var first = g.First();
                var channelName = channelNames.TryGetValue(first.ChannelId, out var name) ? name : first.ChannelId;
                return new OrderSummary(g.Key, first.OrderDate, channelName,
                    g.Select(r => r.BoxSeq).Distinct().Count(), g.Sum(r => r.Qty));
            })
            .OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.FboNo)
            .ToList();

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var search = _searchBox.Text.Trim();
        _filteredOrders = _allOrders.Where(o =>
            search.Length == 0 ||
            o.FboNo.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            o.ChannelName.Contains(search, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        _grid.Rows.Clear();
        foreach (var o in _filteredOrders)
            _grid.Rows.Add(o.FboNo, o.OrderDate.ToString("yyyy-MM-dd"), o.ChannelName, o.BoxCount, o.TotalQty);
    }

    private void Confirm()
    {
        var idx = _grid.CurrentRow?.Index ?? -1;
        if (idx < 0 || idx >= _filteredOrders.Count)
        {
            MessageBox.Show("불러올 발주를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SelectedFboNo = _filteredOrders[idx].FboNo;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record OrderSummary(string FboNo, DateTime OrderDate, string ChannelName, int BoxCount, int TotalQty);
}

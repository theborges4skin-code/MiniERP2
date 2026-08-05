using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 문서관리(DocsForm)의 품목 라인에 과거 출고이력(OutboundDetailTable)을 채널·기간으로 조회해
/// 체크한 여러 건을 한 번에 불러오기 위한 선택 다이얼로그. CskuPickerDialog(단건 CSKU 검색)와
/// 달리 이력은 여러 건을 동시에 문서 라인으로 주입하는 용도라 체크박스 다중선택 그리드를 쓴다.
/// </summary>
public class OutboundHistoryPickerDialog : Form
{
    public List<OutboundHistoryPick> SelectedPicks { get; } = new();

    private readonly OutboundRepository _outboundRepo = new();
    private readonly SalesChannelRepository _channelRepo = new();
    private readonly ChannelSkuRepository _cskuRepo = new();
    private readonly ItemRepository _itemRepo = new();

    private ComboBox _channelCombo = new();
    private DateTimePicker _fromPicker = new();
    private DateTimePicker _toPicker = new();
    private DataGridView _grid = new();
    private List<OutboundDetail> _rows = new();
    private Dictionary<string, string> _channelNames = new();

    public OutboundHistoryPickerDialog(string? defaultChannelCode = null)
    {
        InitializeComponent();
        LoadChannels(defaultChannelCode);
        RunQuery();
    }

    private void InitializeComponent()
    {
        Text = "출고이력 불러오기";
        Size = new Size(820, 520);
        MinimumSize = new Size(620, 380);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var searchBar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        _channelCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        _fromPicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        _toPicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 100 };
        var btnQuickDate = DateRangeQuickSelect.CreateButton(_fromPicker, _toPicker);
        var btnQuery = new Button { Text = "조회", Width = 70 };
        btnQuery.Click += (s, e) => RunQuery();
        _channelCombo.SelectedIndexChanged += (s, e) => RunQuery();

        searchBar.Controls.AddRange(new Control[]
        {
            new Label { Text = "채널:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) }, _channelCombo,
            new Label { Text = "기간:", AutoSize = true, Padding = new Padding(12, 5, 4, 0) }, _fromPicker,
            new Label { Text = "~", AutoSize = true, Padding = new Padding(4, 5, 4, 0) }, _toPicker,
            btnQuickDate,
            btnQuery,
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
        _grid.Columns.Add("CreatedAt", "발주일");
        _grid.Columns.Add("ChannelName", "채널");
        _grid.Columns.Add("ItemName", "품목(CSKU)");
        _grid.Columns.Add("Qty", "수량");
        _grid.Columns.Add("SupplyPrice", "납품가");
        _grid.Columns["CreatedAt"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _grid.Columns["CreatedAt"]!.Width = 90;
        _grid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 0)
            {
                var cell = _grid.Rows[e.RowIndex].Cells["Selected"];
                cell.Value = !(cell.Value is true);
            }
        };

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var btnOk = new Button { Text = "선택 항목 추가", Width = 120 };
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

    private void LoadChannels(string? defaultChannelCode)
    {
        var channels = _channelRepo.GetAll();
        _channelNames = channels.ToDictionary(c => c.ChannelCode, c => c.ChannelName);

        _channelCombo.Items.Add("(전체)");
        foreach (var ch in channels.OrderBy(c => c.ChannelName)) _channelCombo.Items.Add(ch.ChannelName);
        _channelCombo.SelectedIndex = 0;
        if (defaultChannelCode != null && _channelNames.TryGetValue(defaultChannelCode, out var defaultName))
        {
            int idx = _channelCombo.Items.IndexOf(defaultName);
            if (idx >= 0) _channelCombo.SelectedIndex = idx;
        }
    }

    private void RunQuery()
    {
        string? channelCode = null;
        if (_channelCombo.SelectedIndex > 0)
        {
            var selectedName = _channelCombo.SelectedItem!.ToString();
            channelCode = _channelNames.FirstOrDefault(kv => kv.Value == selectedName).Key;
        }

        // 거래명세표에는 정상 거래 라인만 실린다 — 샘플·CS는 제외한다(샘플발송이력관리_개발기획서.md §4.4).
        _rows = _outboundRepo.GetHistory(channelCode, _fromPicker.Value.Date, _toPicker.Value.Date.AddDays(1).AddSeconds(-1), LineKindScope.SaleOnly);

        _grid.Rows.Clear();
        foreach (var r in _rows)
        {
            var channelName = _channelNames.TryGetValue(r.ChannelCode, out var name) ? name : r.ChannelCode;
            _grid.Rows.Add(false, r.CreatedAt.ToString("yyyy-MM-dd"), channelName, ResolveItemName(r), r.Qty, r.SupplyPrice);
        }
    }

    /// <summary>품목명은 CSKU의 송장표시명을 우선하고, 없으면 발주 당시 저장된 원본 상품명으로 대체한다.</summary>
    private string ResolveItemName(OutboundDetail detail)
    {
        var csku = _cskuRepo.GetByChannelAndCskuCode(detail.ChannelCode, detail.MskuCode);
        if (csku != null && !string.IsNullOrWhiteSpace(csku.InvoiceDisplayName)) return csku.InvoiceDisplayName!;
        return string.IsNullOrWhiteSpace(detail.ProductName) ? detail.MskuCode : detail.ProductName;
    }

    private decimal ResolveCostPrice(OutboundDetail detail)
    {
        var csku = _cskuRepo.GetByChannelAndCskuCode(detail.ChannelCode, detail.MskuCode);
        if (csku == null) return 0m;
        return _itemRepo.GetBySku(csku.Msku)?.CostPrice ?? 0m;
    }

    private void ToggleAll(bool value)
    {
        foreach (DataGridViewRow row in _grid.Rows)
            row.Cells["Selected"].Value = value;
    }

    private void Confirm()
    {
        _grid.EndEdit();
        SelectedPicks.Clear();
        for (int i = 0; i < _grid.Rows.Count; i++)
        {
            if (_grid.Rows[i].Cells["Selected"].Value is not true) continue;
            var detail = _rows[i];
            SelectedPicks.Add(new OutboundHistoryPick(detail, (string)_grid.Rows[i].Cells["ItemName"].Value!, ResolveCostPrice(detail)));
        }
        if (SelectedPicks.Count == 0)
        {
            MessageBox.Show("불러올 항목을 선택하세요.", "알림");
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}

public sealed record OutboundHistoryPick(OutboundDetail Detail, string ItemName, decimal CostPrice);

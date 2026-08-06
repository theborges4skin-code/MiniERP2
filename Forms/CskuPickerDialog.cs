using MiniERP2.Database;
using MiniERP2.UI;

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
    public string? SelectedCskuCode { get; private set; }
    public string? SelectedChannelCode { get; private set; }
    public string? SelectedUnit { get; private set; }
    public string? SelectedPacking { get; private set; }

    private readonly ChannelSkuRepository _cskuRepo = new();
    private readonly ItemRepository _itemRepo = new();
    private readonly SalesChannelRepository _channelRepo = new();

    private ComboBox _channelCombo = new();
    private TextBox _searchBox = new();
    private DataGridView _grid = new();
    private List<PickerRow> _allRows = new();
    private List<PickerRow> _filteredRows = new();

    /// <summary>채널 미지정 마스터SKU 단독 행에 쓰는 채널코드/채널명 자리표시자(§5, 간이마진계산기_개발기획서.md).</summary>
    private const string MasterSkuOnlyChannelName = "(마스터SKU 전용)";

    private readonly bool _allowMasterSkuOnly;

    /// <param name="priorityChannelCode">이 채널의 CSKU를 목록 맨 위로 올려서(그 외는 채널명순)
    /// 보여준다. 다른 채널의 CSKU도 항상 함께 검색된다(제한하지 않음).</param>
    /// <param name="preselectChannelFilter">true(기본값)면 채널 콤보의 초기 선택값을
    /// priorityChannelCode로 맞춘다. false면 "(전체)"로 시작해 모든 채널의 CSKU가 바로 보이되,
    /// 정렬 우선순위는 그대로 priorityChannelCode 적용(예: OFS 수동주문 — 다른 채널 CSKU도
    /// 검색은 되어야 하지만 현재 채널 것을 먼저 보고 싶을 때).</param>
    /// <param name="allowMasterSkuOnly">true면 CSKU 목록 외에 채널 미지정 마스터SKU 단독 행도
    /// 함께 보여준다(간이 마진 계산기 전용, §5). 기존 호출부(문서관리 등)는 기본값 false로
    /// 동작이 그대로 유지된다.</param>
    public CskuPickerDialog(string? priorityChannelCode = null, bool preselectChannelFilter = true, bool allowMasterSkuOnly = false)
    {
        _allowMasterSkuOnly = allowMasterSkuOnly;
        InitializeComponent();
        LoadData(priorityChannelCode, preselectChannelFilter);
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
        var btnNewCsku = new Button { Text = "새 CSKU 등록...", Width = 120 };
        btnOk.Click += (s, e) => Confirm();
        btnNewCsku.Click += OnNewCskuClick;
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnOk);
        btnPanel.Controls.Add(btnNewCsku);

        layout.Controls.Add(searchBar, 0, 0);
        layout.Controls.Add(_grid, 0, 1);
        layout.Controls.Add(btnPanel, 0, 2);
        Controls.Add(layout);
        CancelButton = btnCancel;
    }

    private void LoadData(string? priorityChannelCode, bool preselectChannelFilter)
    {
        var items = _itemRepo.GetAll().ToDictionary(i => i.Sku, i => i);
        var channels = _channelRepo.GetAll();
        var channelNames = channels.ToDictionary(c => c.ChannelCode, c => c.ChannelName);

        _allRows = _cskuRepo.GetAll().Select(c =>
        {
            items.TryGetValue(c.Msku, out var item);
            string itemName = !string.IsNullOrWhiteSpace(c.InvoiceDisplayName) ? c.InvoiceDisplayName! : (item?.ItemName ?? c.Msku);
            // 매입처 맥락이 없는 범용 검색 화면이라 CostResolver의 3단계 중 뒤 2단계만 적용한다
            // (개별관리 오버라이드 > 마스터 대표원가). §4.4/§5 참고.
            decimal cost = c.CostPriceOverride ?? item?.CostPrice ?? 0m;
            string channelName = channelNames.TryGetValue(c.ChannelCode, out var name) ? name : c.ChannelCode;
            return new PickerRow(c.ChannelCode, channelName, c.CskuCode, c.Msku, itemName, c.SupplyPrice, cost, c.Unit, c.Packing);
        })
        // priorityChannelCode의 CSKU를 맨 위로, 그 다음은 기존처럼 채널명/품목명순.
        .OrderByDescending(r => priorityChannelCode != null && r.ChannelCode.Equals(priorityChannelCode, StringComparison.OrdinalIgnoreCase))
        .ThenBy(r => r.ChannelName).ThenBy(r => r.ItemName).ToList();

        if (_allowMasterSkuOnly)
        {
            // ChannelCode=""를 채널 미지정 마스터SKU 단독 행의 자리표시자로 쓴다(§5). CostResolver의
            // 3단계 중 마스터 대표원가만 적용된다 — 채널/CSKU 맥락이 없어 개별원가 오버라이드가 없다.
            var masterOnlyRows = items.Values
                .OrderBy(i => i.ItemName)
                .Select(i => new PickerRow("", MasterSkuOnlyChannelName, "", i.Sku, i.ItemName, 0m, i.CostPrice, i.Unit, null));
            _allRows.AddRange(masterOnlyRows);
        }

        _channelCombo.Items.Add("(전체)");
        foreach (var ch in channels.OrderBy(c => c.ChannelName)) _channelCombo.Items.Add(ch.ChannelName);
        if (_allowMasterSkuOnly) _channelCombo.Items.Add(MasterSkuOnlyChannelName);
        _channelCombo.SelectedIndex = 0;
        if (preselectChannelFilter && priorityChannelCode != null && channelNames.TryGetValue(priorityChannelCode, out var defaultName))
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
        var isMasterSkuOnly = row.ChannelCode.Length == 0;
        SelectedItemName = row.ItemName;
        SelectedUnitPrice = row.SupplyPrice;
        SelectedCostPrice = row.CostPrice;
        SelectedMsku = row.Msku;
        SelectedCskuCode = isMasterSkuOnly ? null : row.CskuCode;
        SelectedChannelCode = isMasterSkuOnly ? null : row.ChannelCode;
        SelectedUnit = row.Unit;
        SelectedPacking = row.Packing;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// 원하는 CSKU가 목록에 없을 때 바로 등록한다(사용자 요청). 새로 등록한 CSKU를 방금 목록에서
    /// 고른 것처럼 그대로 반환해, 다시 검색해서 찾을 필요 없이 이 창을 그대로 닫는다.
    /// </summary>
    private void OnNewCskuClick(object? sender, EventArgs e)
    {
        string? channelCode = null;
        if (_channelCombo.SelectedIndex > 0)
        {
            var name = _channelCombo.SelectedItem!.ToString();
            channelCode = _channelRepo.GetAll().FirstOrDefault(c => c.ChannelName == name)?.ChannelCode;
        }

        using var dialog = new NewCskuRegistrationDialog(channelCode, _searchBox.Text.Trim());
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK || dialog.ResultCskuCode == null) return;

        SelectedItemName = dialog.ResultItemName;
        SelectedUnitPrice = dialog.ResultUnitPrice;
        SelectedCostPrice = dialog.ResultCostPrice;
        SelectedMsku = dialog.ResultMsku;
        SelectedCskuCode = dialog.ResultCskuCode;
        SelectedChannelCode = dialog.ResultChannelCode;
        SelectedUnit = dialog.ResultUnit;
        SelectedPacking = dialog.ResultPacking;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record PickerRow(string ChannelCode, string ChannelName, string CskuCode, string Msku,
        string ItemName, decimal SupplyPrice, decimal CostPrice, string Unit, string? Packing);
}

using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 기획서 5.3절 '매핑관리창'
/// </summary>
public class MappingForm : Form
{
    private readonly MappingRepository _mappingRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly ItemRepository _itemRepository = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly ChannelConfigService _channelConfigService = new();

    private ComboBox _channelComboBox = new();
    private TabControl _ruleTabControl = new();
    private readonly HashSet<TabPage> _dirtyTabs = new();
    /// <summary>저장 확인이 끝나 닫기를 다시 시도하는 경우 OnFormClosing의 확인 절차를 또 거치지
    /// 않게 하는 플래그. <see cref="OnFormClosing"/> 참고.</summary>
    private bool _closeConfirmed;
    private readonly Dictionary<MappingRuleType, HashSet<string>> _conflictingKeysByType = new();
    private DataGridView _conflictGrid = new();

    // "전체 규칙 관리" 탭 — SalesManagerV2(레거시)의 통합 규칙 관리자 패턴. 예외/1:1/임시/조건부
    // 4종을 한 테이블에 모아 채널/키워드로 검색하고, 체크박스 다중선택으로 일괄 삭제할 수 있다.
    // CSKU(송장표시명/납품가)를 JOIN해서 보여주는 게 레거시에는 없던 MiniERP2 고유의 보강 지점.
    private DataGridView _unifiedRulesGrid = new();
    private ComboBox _unifiedFilterChannelCombo = new();
    private ComboBox _unifiedFilterTargetCombo = new();
    private TextBox _unifiedSearchTextBox = new();
    private List<UnifiedRuleRow> _allUnifiedRules = [];

    private class UnifiedRuleRow
    {
        public bool Selected { get; set; }
        public string TypeLabel { get; set; } = string.Empty;
        public MappingRuleType RuleType { get; set; }
        public long RuleId { get; set; }
        public string ChannelCode { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string TargetSku { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;

        // 아래 4개는 TargetSku(CSKU 코드)로 조회한 ChannelSkuTable 정보로, 여기서 직접 편집하면
        // ChannelSkuRepository.Upsert를 통해 실제 DB에 반영된다(OnUnifiedRulesGridCellEndEdit 참고).
        public string CskuMsku { get; set; } = string.Empty;
        public string CskuInvoiceDisplayName { get; set; } = string.Empty;
        public decimal? CskuSupplyPrice { get; set; }
        public string CskuNote { get; set; } = string.Empty;
        public DateTime? CskuUpdatedAt { get; set; }
    }

    // 조건부 매핑(상세) 탭 — 다중 AND/OR 조건 전용 편집기. 기존 "조건부 매핑" 단순 그리드(SaveRules)와
    // 분리되어 즉시 DB에 반영되므로, 단순 그리드 저장이 이 탭의 데이터를 건드리지 않는다.
    private TabPage _conditionDetailTabPage = new();
    private DataGridView _conditionRuleGrid = new();
    private DataGridView _conditionDetailGrid = new();
    private TextBox _conditionKeyTextBox = new();
    private TextBox _conditionTargetSkuTextBox = new();
    private Label _conditionPreviewLabel = new();
    private Label _conditionSaveFeedbackLabel = new();
    private Label _globalStatusLabel = new();
    private Label _channelTypeLabel = new();
    private long _selectedConditionRuleId = -1;
    private ListBox _skuSearchListBox = new();
    private string[] _allSkuCodes = Array.Empty<string>();
    private Button _btnUndoLastMapping = new();
    private long _lastSavedConditionRuleId = -1;

    // 조건부 매핑(상세) 탭 — 인라인 CSKU 편집
    private DataGridView _conditionCskuGrid = new();
    private TextBox _conditionCskuCodeBox = new();
    private TextBox _conditionCskuInvoiceBox = new();
    private TextBox _conditionCskuPriceBox = new();
    private RadioButton _conditionVatIncludedRadio = new();
    private RadioButton _conditionVatExcludedRadio = new();

    // "미매핑 처리" 탭 — OFS에서 로드한 발주서를 보면서 바로 매핑할 수 있게 하는 화면.
    // 상단(미매핑 목록)/하단(마스터DB 검색+CSKU 입력)으로 분리되어 있다.
    private TabPage _unmappedTabPage = new();
    private ExcelLikeDataGridView _unmappedGrid = new();
    private TextBox _masterSearchBox = new();
    private DataGridView _masterCandidateGrid = new();
    private DataGridView _cskuHistoryGrid = new();
    private TextBox _unmappedCskuCodeTextBox = new();
    private TextBox _unmappedSupplyPriceTextBox = new();
    private RadioButton _unmappedVatIncludedRadio = new();
    private RadioButton _unmappedVatExcludedRadio = new();
    private TextBox _unmappedInvoiceNameTextBox = new();
    private Label _invoicePreviewLabel = new();
    private BindingList<OfsOrderItem>? _sourceOrders;
    private Action? _onMappingApplied;
    private string? _unmappedChannelCode;

    public MappingForm()
    {
        InitializeComponent();
        LoadChannels();
    }

    private void InitializeComponent()
    {
        Text = "매핑 관리";
        Size = new Size(1024, 768);

        // Enable drag-and-drop functionality
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topPanel = CreateTopPanel();
        _ruleTabControl = CreateRuleTabControl();

        mainLayout.Controls.Add(topPanel, 0, 0);
        mainLayout.Controls.Add(_ruleTabControl, 0, 1);
        Controls.Add(mainLayout);

        FormClosing += OnFormClosing;
    }

    private Control CreateTopPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        var channelLabel = new Label { Text = "채널:", Anchor = AnchorStyles.Left, AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
        _channelComboBox = new ComboBox { Size = new Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList };
        _channelComboBox.SelectedIndexChanged += OnChannelComboBoxSelectedIndexChanged;

        var btnSave = new Button { Text = "저장", Size = new Size(100, 30) };
        btnSave.Click += OnSaveClick;

        _globalStatusLabel = new Label { AutoSize = true, Padding = new Padding(15, 8, 0, 0), ForeColor = Color.DarkGreen };
        _channelTypeLabel = new Label { AutoSize = true, Padding = new Padding(6, 8, 0, 0), ForeColor = Color.SteelBlue, Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold) };

        panel.Controls.Add(channelLabel);
        panel.Controls.Add(_channelComboBox);
        panel.Controls.Add(_channelTypeLabel);
        panel.Controls.Add(btnSave);
        panel.Controls.Add(_globalStatusLabel);

        return panel;
    }

    private TabControl CreateRuleTabControl()
    {
        var tabControl = new TabControl { Dock = DockStyle.Fill };

        _unmappedTabPage = CreateUnmappedTabPage();
        tabControl.TabPages.Add(_unmappedTabPage);
        tabControl.TabPages.Add(CreateRuleTabPage("예외 처리", MappingRuleType.Exception));
        tabControl.TabPages.Add(CreateRuleTabPage("1:1 매핑", MappingRuleType.Exact));
        tabControl.TabPages.Add(CreateRuleTabPage("임시 매핑", MappingRuleType.Temp));
        _conditionDetailTabPage = CreateConditionDetailTabPage();
        tabControl.TabPages.Add(_conditionDetailTabPage);
        tabControl.TabPages.Add(CreateConflictTabPage());
        tabControl.TabPages.Add(CreateUnifiedRulesTabPage());

        tabControl.Selecting += OnTabSelecting;

        return tabControl;
    }

    /// <summary>
    /// 발주서에서 로드된 미매핑건을 보면서 바로 매핑할 수 있는 탭. 상단은 미매핑 목록(원본
    /// OfsOrderItem 참조 — 여기서 매핑하면 OFS 화면에도 그대로 반영됨), 하단은 마스터DB
    /// 검색(SKU/상품명)과 CSKU(납품가/송장표시명) 입력, 그리고 선택한 SKU에 이미 매핑된 다른
    /// 상품명+옵션명 조합들을 보여주는 "CSKU 매핑 이력" 영역으로 구성된다.
    /// </summary>
    private TabPage CreateUnmappedTabPage()
    {
        var tabPage = new TabPage("미매핑 처리");

        // 기본값은 미매핑 목록이 위쪽 10줄 정도만 보이게 둔다(처음 실행 시에만 적용되고, 한 번
        // 조절하면 PersistentSplitContainer가 그 값을 기억해 다음에도 유지한다).
        var split = new PersistentSplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 270, PersistenceKey = "MappingForm.UnmappedSplit" };

        _unmappedGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "MappingForm.UnmappedGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AllowUserToResizeColumns = true,
        };
        // OptionName을 AutoSizeMode.Fill로 두면 사용자가 다른 열의 폭을 조절해도 Fill 열이 즉시
        // 남는 공간을 다시 차지하면서 헤더 폭이 "고정"되지 않는 문제가 있었다. 모든 열에 고정 폭을
        // 주고 PersistenceKey로 사용자가 조절한 폭을 다음에 열 때도 기억하게 한다.
        _unmappedGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "ProductName", HeaderText = "상품명", DataPropertyName = "ProductName", Width = 220, Resizable = DataGridViewTriState.True },
            new DataGridViewTextBoxColumn { Name = "OptionName", HeaderText = "옵션명", DataPropertyName = "OptionName", Width = 260, Resizable = DataGridViewTriState.True },
            new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "수량", DataPropertyName = "Quantity", Width = 60, Resizable = DataGridViewTriState.True },
            new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "상태", DataPropertyName = "Status", Width = 100, Resizable = DataGridViewTriState.True }
        );
        _unmappedGrid.SelectionChanged += OnUnmappedRowSelectionChanged;
        SetupUnmappedContextMenu();

        var bottomLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6 };
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 3, 5, 0) };
        searchPanel.Controls.Add(new Label { Text = "검색(마스터DB SKU/상품명 + CSKU 코드/송장표시명):", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
        _masterSearchBox = new TextBox { Width = 320 };
        _masterSearchBox.TextChanged += (s, e) => { RunMasterSearch(); RunCskuSearch(); };
        searchPanel.Controls.Add(_masterSearchBox);

        _masterCandidateGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _masterCandidateGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Sku", HeaderText = "SKU", DataPropertyName = "Sku", Width = 150 },
            new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "상품명", DataPropertyName = "ItemName", Width = 220 },
            new DataGridViewTextBoxColumn { Name = "CostPrice", HeaderText = "제조원가(VAT포함)", DataPropertyName = "CostPrice", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _masterCandidateGrid.SelectionChanged += (s, e) => OnMasterCandidateSelectionChanged();

        var masterPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        masterPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        masterPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        masterPanel.Controls.Add(new Label { Text = "마스터DB 후보", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        masterPanel.Controls.Add(_masterCandidateGrid, 0, 1);

        _cskuHistoryGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _cskuHistoryGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "CskuCode", HeaderText = "CSKU 코드", DataPropertyName = "CskuCode", Width = 150 },
            new DataGridViewTextBoxColumn { Name = "Msku", HeaderText = "마스터SKU", DataPropertyName = "Msku", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "InvoiceDisplayName", HeaderText = "송장표시명", DataPropertyName = "InvoiceDisplayName", Width = 180 },
            new DataGridViewTextBoxColumn { Name = "SupplyPrice", HeaderText = "납품가", DataPropertyName = "SupplyPrice", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _cskuHistoryGrid.SelectionChanged += (s, e) => OnCskuSearchResultSelectionChanged();
        _cskuHistoryGrid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ApplyExactMappingToSelectedUnmapped(); };

        var cskuPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        cskuPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        cskuPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cskuPanel.Controls.Add(new Label { Text = "CSKU 검색결과 — 더블클릭하면 바로 매핑됩니다", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        cskuPanel.Controls.Add(_cskuHistoryGrid, 0, 1);

        var candidatesSplit = new PersistentSplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 430, PersistenceKey = "MappingForm.CandidatesSplit" };
        candidatesSplit.Panel1.Controls.Add(masterPanel);
        candidatesSplit.Panel2.Controls.Add(cskuPanel);

        var infoPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 3, 5, 0) };
        infoPanel.Controls.Add(new Label { Text = "CSKU 코드(자동제안, 편집 가능):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        _unmappedCskuCodeTextBox = new TextBox { Width = 150 };
        infoPanel.Controls.Add(_unmappedCskuCodeTextBox);
        infoPanel.Controls.Add(new Label { Text = "납품단가(선택):", AutoSize = true, Padding = new Padding(15, 6, 4, 0) });
        _unmappedSupplyPriceTextBox = new TextBox { Width = 90 };
        infoPanel.Controls.Add(_unmappedSupplyPriceTextBox);
        _unmappedVatIncludedRadio = new RadioButton { Text = "VAT포함", AutoSize = true, Checked = true, Padding = new Padding(8, 4, 0, 0) };
        _unmappedVatExcludedRadio = new RadioButton { Text = "VAT별도", AutoSize = true, Padding = new Padding(5, 4, 0, 0) };
        infoPanel.Controls.Add(_unmappedVatIncludedRadio);
        infoPanel.Controls.Add(_unmappedVatExcludedRadio);
        infoPanel.Controls.Add(new Label { Text = "송장표시명(선택):", AutoSize = true, Padding = new Padding(15, 6, 4, 0) });
        _unmappedInvoiceNameTextBox = new TextBox { Width = 220 };
        _unmappedInvoiceNameTextBox.TextChanged += (s, e) => RefreshInvoicePreview();
        infoPanel.Controls.Add(_unmappedInvoiceNameTextBox);

        _invoicePreviewLabel = new Label { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(5, 3, 0, 0), ForeColor = Color.DimGray };
        RefreshInvoicePreview();

        // 미매핑건을 선택하고 위 정보를 입력한 뒤 누르는 핵심 동작이라 다른 보조 버튼들과
        // 분리해 항상 보이는 자리에 둔다(이전에는 보조 버튼들과 같은 줄에 섞여 있어 좁은 창에서
        // 잘려 보이지 않는 경우가 있었음).
        var primaryButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 4, 5, 0) };
        var btnApplyExact = new Button { Text = "매핑하기", Size = new Size(120, 34), Font = new Font(Font, FontStyle.Bold) };
        btnApplyExact.Click += (s, e) => ApplyExactMappingToSelectedUnmapped();
        primaryButtonPanel.Controls.Add(btnApplyExact);
        primaryButtonPanel.Controls.Add(new Label
        {
            Text = "위에서 마스터DB 후보 또는 기존 CSKU를 선택하고 정보를 입력한 뒤 누르세요.",
            AutoSize = true,
            Padding = new Padding(10, 9, 0, 0),
            ForeColor = Color.DimGray,
        });

        var secondaryButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 0, 5, 0) };
        var btnRegisterTemp = new Button { Text = "임시 SKU 등록 후 매핑", Size = new Size(150, 30) };
        btnRegisterTemp.Click += (s, e) => RegisterTempSkuAndMap();
        var btnAddCondition = new Button { Text = "조건부 매핑 규칙 추가", Size = new Size(150, 30) };
        btnAddCondition.Click += (s, e) => AddCellAsConditionToRule();
        var btnExclude = new Button { Text = "예외 처리(매핑 제외)", Size = new Size(140, 30) };
        btnExclude.Click += (s, e) => ExcludeSelectedUnmapped();
        secondaryButtonPanel.Controls.Add(btnRegisterTemp);
        secondaryButtonPanel.Controls.Add(btnAddCondition);
        secondaryButtonPanel.Controls.Add(btnExclude);

        bottomLayout.Controls.Add(searchPanel, 0, 0);
        bottomLayout.Controls.Add(candidatesSplit, 0, 1);
        bottomLayout.Controls.Add(infoPanel, 0, 2);
        bottomLayout.Controls.Add(primaryButtonPanel, 0, 3);
        bottomLayout.Controls.Add(secondaryButtonPanel, 0, 4);
        bottomLayout.Controls.Add(_invoicePreviewLabel, 0, 5);

        split.Panel1.Controls.Add(_unmappedGrid);
        split.Panel2.Controls.Add(bottomLayout);

        tabPage.Controls.Add(split);
        return tabPage;
    }

    private void SetupUnmappedContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("1:1 매핑 적용", null, (s, e) => ApplyExactMappingToSelectedUnmapped());
        menu.Items.Add("임시 SKU 등록 후 매핑", null, (s, e) => RegisterTempSkuAndMap());
        menu.Items.Add("조건부 매핑 규칙 추가", null, (s, e) => AddCellAsConditionToRule());
        menu.Items.Add("예외 처리(매핑 제외)", null, (s, e) => ExcludeSelectedUnmapped());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("이 셀 내용을 CSKU 상품명으로 사용", null, (s, e) => UseCurrentCellAsInvoiceDisplayName());
        _unmappedGrid.ContextMenuStrip = menu;

        // 실무에서는 오른쪽 클릭으로 바로 메뉴를 여는 경우가 많으므로, 우클릭한 셀을 먼저 선택시킨다
        // (어느 열을 우클릭했는지도 함께 기억해 "CSKU 상품명으로 사용"이 그 칸의 값을 그대로 쓸 수 있게 한다).
        _unmappedGrid.MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var hit = _unmappedGrid.HitTest(e.X, e.Y);
            if (hit.RowIndex < 0) return;
            var columnIndex = hit.ColumnIndex >= 0 ? hit.ColumnIndex : 0;
            _unmappedGrid.CurrentCell = _unmappedGrid.Rows[hit.RowIndex].Cells[columnIndex];
        };
    }

    /// <summary>
    /// 우클릭한 셀(상품명/옵션명 등)의 표시 텍스트를 그대로 "송장표시명" 입력란에 채워준다.
    /// 발주서의 상품명/옵션명 표기가 그대로 송장에 쓸 만큼 간결한 경우, 다시 타이핑하지 않고
    /// 바로 가져다 쓸 수 있게 하기 위함이다.
    /// </summary>
    private void UseCurrentCellAsInvoiceDisplayName()
    {
        var cell = _unmappedGrid.CurrentCell;
        if (cell == null) return;

        _unmappedInvoiceNameTextBox.Text = cell.Value?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// OFS에서 발주서를 로드한 직후(또는 수동으로) 호출됩니다. 채널을 선택하고 "미매핑 처리" 탭으로
    /// 전환해, 로드된 주문 목록에서 미매핑 항목을 바로 보면서 매핑할 수 있게 합니다. orders는 OFS의
    /// 그리드와 같은 인스턴스를 참조해야 하며, 여기서 매핑을 적용하면 그 객체에 직접 반영되고
    /// onMappingApplied 콜백으로 OFS 화면도 새로고침할 수 있습니다.
    /// </summary>
    public void ShowUnmappedItems(string channelCode, BindingList<OfsOrderItem> orders, Action? onMappingApplied = null)
    {
        _sourceOrders = orders;
        _onMappingApplied = onMappingApplied;
        _unmappedChannelCode = channelCode;

        _channelComboBox.SelectedValue = channelCode;
        RefreshUnmappedGrid();

        // RefreshUnmappedGrid가 그리드를 바인딩하면서 첫 행이 자동 선택되어 검색창에 그 상품명이
        // 채워질 수 있으므로, 창을 열 때는 항상 빈 검색창에서 시작하도록 마지막에 다시 비워준다.
        _masterSearchBox.Text = string.Empty;
        RunMasterSearch();
        _ruleTabControl.SelectedTab = _unmappedTabPage;
        _masterSearchBox.Focus();
        _masterSearchBox.SelectAll();
    }

    private void RefreshUnmappedGrid()
    {
        if (_sourceOrders == null || string.IsNullOrEmpty(_unmappedChannelCode))
        {
            _unmappedGrid.DataSource = null;
            return;
        }

        var unmapped = _sourceOrders
            .Where(o => o.ChannelCode == _unmappedChannelCode && (o.Status == "매핑 실패" || o.Status == "매핑 키 없음"))
            .ToList();
        _unmappedGrid.DataSource = new BindingList<OfsOrderItem>(unmapped);
    }

    /// <summary>
    /// 미매핑 항목을 선택해도 검색창은 건드리지 않는다(빈칸 유지). 상품명을 그대로 옮겨 쓰고 싶으면
    /// 미매핑 목록을 우클릭해 "CSKU 상품명으로 사용"을 쓰면 된다 — 검색창 자동입력은 매번 지우고
    /// 다시 입력해야 하는 불편함이 있어 제거했다.
    /// </summary>
    private void OnUnmappedRowSelectionChanged(object? sender, EventArgs e)
    {
        RefreshInvoicePreview();
    }

    /// <summary>
    /// 선택한 미매핑 항목의 수량과 송장표시명 입력값을 조합해, 택배사 출력양식에 실제로 나갈
    /// "송장표시 품목명"이 어떤 모습일지 매핑하기 전에 미리 보여준다(SkuMapper.BuildInvoiceLabel과
    /// 같은 조합 규칙: 표시명 + 수량 + "개").
    /// </summary>
    private void RefreshInvoicePreview()
    {
        var quantity = _unmappedGrid.CurrentRow?.DataBoundItem is OfsOrderItem item ? item.Quantity : 0;
        var name = _unmappedInvoiceNameTextBox.Text.Trim();

        _invoicePreviewLabel.Text = string.IsNullOrEmpty(name)
            ? "송장표시 미리보기: (송장표시명을 입력하면 택배사 양식에 실제로 나갈 모습을 여기서 미리 볼 수 있습니다)"
            : $"송장표시 미리보기: \"{name} {quantity}개\"";
    }

    private void RunMasterSearch()
    {
        var query = _masterSearchBox.Text.Trim();
        var allItems = _itemRepository.GetAll();

        var matches = string.IsNullOrEmpty(query)
            ? allItems
            : allItems.Where(i => i.Sku.Contains(query, StringComparison.OrdinalIgnoreCase) || i.ItemName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        _masterCandidateGrid.DataSource = new BindingList<ItemModel>(matches);
    }

    /// <summary>
    /// 검색창의 내용으로 마스터DB뿐 아니라 이미 만들어진 CSKU(코드/마스터SKU/송장표시명 기준)도
    /// 함께 찾아 "CSKU 검색결과" 그리드에 보여준다. 기존 CSKU가 있으면 더블클릭(또는 선택 후
    /// "매핑하기")으로 새로 만들지 않고 바로 그 CSKU에 매핑할 수 있다.
    /// </summary>
    private void RunCskuSearch()
    {
        if (string.IsNullOrEmpty(_unmappedChannelCode))
        {
            _cskuHistoryGrid.DataSource = null;
            return;
        }

        var query = _masterSearchBox.Text.Trim();
        var allCskus = _channelSkuRepository.GetAllByChannel(_unmappedChannelCode);

        var matches = string.IsNullOrEmpty(query)
            ? allCskus
            : allCskus.Where(c =>
                c.CskuCode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Msku.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (c.InvoiceDisplayName ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase))
              .ToList();

        _cskuHistoryGrid.DataSource = new BindingList<ChannelSkuModel>(matches);
    }

    private void OnMasterCandidateSelectionChanged()
    {
        _unmappedCskuCodeTextBox.Text = string.Empty;
        _unmappedSupplyPriceTextBox.Text = string.Empty;
        _unmappedInvoiceNameTextBox.Text = string.Empty;
        _unmappedVatIncludedRadio.Checked = true;

        if (_masterCandidateGrid.CurrentRow?.DataBoundItem is not ItemModel selected) return;

        var defaultCode = BuildDefaultCskuCode(selected.Sku);
        _unmappedCskuCodeTextBox.Text = defaultCode;

        if (string.IsNullOrEmpty(_unmappedChannelCode)) return;

        var existing = _channelSkuRepository.GetByChannelAndCskuCode(_unmappedChannelCode, defaultCode);
        if (existing != null)
        {
            _unmappedSupplyPriceTextBox.Text = existing.SupplyPrice.ToString();
            _unmappedInvoiceNameTextBox.Text = existing.InvoiceDisplayName ?? string.Empty;
        }
    }

    /// <summary>
    /// CSKU 검색결과에서 행을 선택하면 그 CSKU의 코드/납품가/송장표시명을 입력란에 채워준다.
    /// 이 상태로 "매핑하기"를 누르면 새 CSKU를 만들지 않고 그 CSKU에 매핑 조건만 추가된다.
    /// </summary>
    private void OnCskuSearchResultSelectionChanged()
    {
        if (_cskuHistoryGrid.CurrentRow?.DataBoundItem is not ChannelSkuModel csku) return;

        _unmappedCskuCodeTextBox.Text = csku.CskuCode;
        _unmappedSupplyPriceTextBox.Text = csku.SupplyPrice.ToString();
        _unmappedInvoiceNameTextBox.Text = csku.InvoiceDisplayName ?? string.Empty;
        _unmappedVatIncludedRadio.Checked = true;
    }

    /// <summary>
    /// "채널명 앞 3글자_마스터SKU" 형태의 기본 CSKU 코드를 제안한다. 사용자가 직접 편집할 수 있는
    /// 출발점일 뿐이다(같은 마스터SKU도 채널 옵션 구성에 따라 CSKU 코드를 다르게 줄 수 있음).
    /// </summary>
    private string BuildDefaultCskuCode(string masterSku)
    {
        if (string.IsNullOrEmpty(_unmappedChannelCode)) return masterSku;

        var channelName = _salesChannelRepository.GetAll().FirstOrDefault(c => c.ChannelCode == _unmappedChannelCode)?.ChannelName ?? _unmappedChannelCode;
        return CskuCodeGenerator.BuildDefault(channelName, masterSku);
    }

    private string BuildConditionTabDefaultCskuCode(string masterSku)
    {
        var channelCode = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(channelCode)) return masterSku;
        var channelName = _salesChannelRepository.GetAll().FirstOrDefault(c => c.ChannelCode == channelCode)?.ChannelName ?? channelCode;
        return CskuCodeGenerator.BuildDefault(channelName, masterSku);
    }

    /// <summary>
    /// 조건부 매핑(상세) 탭의 대상 SKU가 바뀔 때 그 MSKU에 이미 부여된 이 채널의 CSKU 목록을 갱신한다.
    /// 텍스트가 MSKU이면 그것을 Msku로 가진 CSKU를, CSKU 코드 자체이면 그 단일 CSKU를 표시한다.
    /// CSKU가 아직 없으면 기본 코드를 입력란에 자동 제안한다.
    /// </summary>
    private void RefreshConditionCskuGrid()
    {
        var channelCode = _channelComboBox.SelectedValue as string;
        var targetText = _conditionTargetSkuTextBox.Text.Trim();
        if (string.IsNullOrEmpty(channelCode) || string.IsNullOrEmpty(targetText))
        {
            _conditionCskuGrid.DataSource = null;
            return;
        }

        var cskus = _channelSkuRepository.GetAllByMsku(targetText)
            .Where(c => string.Equals(c.ChannelCode, channelCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (cskus.Count == 0)
        {
            var byCode = _channelSkuRepository.GetByChannelAndCskuCode(channelCode, targetText);
            if (byCode != null) cskus = [byCode];
        }

        _conditionCskuGrid.DataSource = new BindingList<ChannelSkuModel>(cskus);

        if (cskus.Count == 0 && string.IsNullOrEmpty(_conditionCskuCodeBox.Text))
            _conditionCskuCodeBox.Text = BuildConditionTabDefaultCskuCode(targetText);
    }

    /// <summary>
    /// CSKU 목록에서 행을 선택하면 코드/송장표시명/납품가를 입력란에 채우고
    /// 대상 SKU 텍스트박스를 그 CSKU 코드로 업데이트한다.
    /// </summary>
    private void OnConditionCskuGridSelectionChanged()
    {
        if (_conditionCskuGrid.CurrentRow?.DataBoundItem is not ChannelSkuModel csku) return;
        _conditionCskuCodeBox.Text = csku.CskuCode;
        _conditionCskuInvoiceBox.Text = csku.InvoiceDisplayName ?? string.Empty;
        _conditionCskuPriceBox.Text = csku.SupplyPrice > 0 ? csku.SupplyPrice.ToString() : string.Empty;
        _conditionVatIncludedRadio.Checked = true;
        if (_conditionTargetSkuTextBox.Enabled)
            _conditionTargetSkuTextBox.Text = csku.CskuCode;
    }

    /// <summary>
    /// "CSKU 저장" — 대상 SKU(MSKU 또는 기존 CSKU 코드)를 기준으로 새 CSKU를 만들거나 기존을 갱신한다.
    /// 저장 후 대상 SKU 입력란을 CSKU 코드로 교체해 매핑 규칙이 정확히 CSKU를 가리키게 한다.
    /// </summary>
    private void OnSaveConditionCskuClick(object? sender, EventArgs e)
    {
        var channelCode = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(channelCode))
        {
            MessageBox.Show("채널을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var targetText = _conditionTargetSkuTextBox.Text.Trim();
        if (string.IsNullOrEmpty(targetText))
        {
            MessageBox.Show("대상 SKU(마스터SKU)를 먼저 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var cskuCode = _conditionCskuCodeBox.Text.Trim();
        if (string.IsNullOrEmpty(cskuCode))
        {
            MessageBox.Show("CSKU 코드를 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 대상 텍스트가 이미 CSKU 코드라면 그 Msku를 사용, 아니면 텍스트 자체가 Msku
        var existingAsCsku = _channelSkuRepository.GetByChannelAndCskuCode(channelCode, targetText);
        var msku = existingAsCsku?.Msku ?? targetText;

        decimal.TryParse(_conditionCskuPriceBox.Text, out var price);
        if (_conditionVatExcludedRadio.Checked && price > 0)
            price = Math.Round(price * 1.1m, 0);

        _channelSkuRepository.Upsert(new ChannelSkuModel
        {
            ChannelCode = channelCode,
            CskuCode = cskuCode,
            Msku = msku,
            SupplyPrice = price,
            InvoiceDisplayName = string.IsNullOrWhiteSpace(_conditionCskuInvoiceBox.Text) ? null : _conditionCskuInvoiceBox.Text.Trim()
        });

        // 대상 SKU를 CSKU 코드로 교체 (규칙이 정확히 CSKU를 가리키게)
        if (_conditionTargetSkuTextBox.Enabled && _conditionTargetSkuTextBox.Text != cskuCode)
            _conditionTargetSkuTextBox.Text = cskuCode;

        // SKU 검색 목록에 새 CSKU 코드 반영
        LoadConditionRules(channelCode);
        RefreshConditionCskuGrid();

        _conditionSaveFeedbackLabel.Text = $"CSKU '{cskuCode}' 저장됨. 이제 규칙 정보와 상세조건도 저장하세요.";
    }

    /// <summary>
    /// 매핑 대상 마스터SKU를 결정한다. CSKU 검색결과에서 기존 CSKU를 고른 경우 그 CSKU가 연결된
    /// 마스터SKU를 우선 쓰고(이미 있는 CSKU에 조건만 추가하는 빠른 경로), 그게 아니면 마스터DB
    /// 후보 목록에서 고른 항목을 쓴다.
    /// </summary>
    private string? ResolveSelectedMasterSku()
    {
        if (_cskuHistoryGrid.CurrentRow?.DataBoundItem is ChannelSkuModel csku) return csku.Msku;
        if (_masterCandidateGrid.CurrentRow?.DataBoundItem is ItemModel selected) return selected.Sku;
        return null;
    }

    private void ApplyExactMappingToSelectedUnmapped()
    {
        if (_unmappedGrid.CurrentRow?.DataBoundItem is not OfsOrderItem item)
        {
            MessageBox.Show("매핑할 미매핑 항목을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var masterSku = ResolveSelectedMasterSku();
        if (masterSku == null)
        {
            MessageBox.Show("마스터DB 후보 또는 CSKU 검색결과에서 매핑할 대상을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var cskuCode = ResolveCskuCodeOrShowError();
        if (cskuCode == null) return;

        ApplyMappingToItem(item, cskuCode, masterSku, "매핑(1:1)", saveAsExactRule: true);
    }

    private void RegisterTempSkuAndMap()
    {
        if (_unmappedGrid.CurrentRow?.DataBoundItem is not OfsOrderItem item)
        {
            MessageBox.Show("매핑할 미매핑 항목을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var existingSkus = _itemRepository.GetAll().Select(i => i.Sku);
        var tempSku = TempSkuGenerator.GenerateNext(existingSkus);

        var confirm = MessageBox.Show(
            $"임시 SKU '{tempSku}'를 마스터DB에 새로 등록하고 이 주문에 매핑하시겠습니까?\n상품명: {item.ProductName}\n\n등록 후 마스터SKU 관리창에서 원가 등 정보를 보완해주세요.",
            "임시 SKU 등록", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _itemRepository.Upsert(new ItemModel
        {
            Sku = tempSku,
            ItemName = string.IsNullOrWhiteSpace(item.ProductName) ? tempSku : item.ProductName,
            CostPrice = 0m,
        });

        var cskuCode = string.IsNullOrWhiteSpace(_unmappedCskuCodeTextBox.Text) ? BuildDefaultCskuCode(tempSku) : _unmappedCskuCodeTextBox.Text.Trim();

        ApplyMappingToItem(item, cskuCode, tempSku, "매핑(임시)", saveAsExactRule: true);
    }

    private string? ResolveCskuCodeOrShowError()
    {
        var code = _unmappedCskuCodeTextBox.Text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            MessageBox.Show("CSKU 코드를 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        return code;
    }

    private void ApplyMappingToItem(OfsOrderItem item, string cskuCode, string masterSku, string status, bool saveAsExactRule)
    {
        var channelCode = !string.IsNullOrEmpty(item.ChannelCode) ? item.ChannelCode : _unmappedChannelCode;
        if (string.IsNullOrEmpty(channelCode)) return;

        SaveChannelSkuInfoFromUnmappedPanel(cskuCode, masterSku, channelCode);

        var key = (item.ProductName ?? string.Empty) + (item.OptionName ?? string.Empty);

        if (saveAsExactRule && !string.IsNullOrWhiteSpace(key))
        {
            _mappingRepository.UpsertExactRule(channelCode, key, cskuCode);
        }

        item.MappedSku = cskuCode;
        item.Status = status;

        // 지금 로드되어 있는 발주서 안에 같은 상품명+옵션명 조합의 다른 미매핑 항목이 더 있으면,
        // 다시 불러오지 않아도 바로 같은 결과로 자동 매핑해 매번 한 건씩 처리하는 수고를 없앤다.
        if (!string.IsNullOrWhiteSpace(key))
        {
            ApplySameKeyToOtherUnmappedSiblings(key, cskuCode, status);
        }

        RefreshUnmappedGrid();
        _unmappedCskuCodeTextBox.Text = string.Empty;
        _unmappedSupplyPriceTextBox.Text = string.Empty;
        _unmappedInvoiceNameTextBox.Text = string.Empty;
        _unmappedVatIncludedRadio.Checked = true;
        _masterSearchBox.Text = string.Empty;
        _onMappingApplied?.Invoke();
    }

    /// <summary>
    /// 같은 채널에 로드된 다른 미매핑 항목 중 (상품명+옵션명) 키가 같은 건들을 찾아 같은 SKU/상태로
    /// 즉시 매핑한다. 방금 저장한 1:1 규칙이 적용된 것과 같은 결과이지만, 발주서를 다시 불러오지
    /// 않고도 지금 화면에 떠 있는 나머지 건들이 한꺼번에 처리되게 하기 위함이다.
    /// </summary>
    private void ApplySameKeyToOtherUnmappedSiblings(string key, string? targetSku, string status)
    {
        if (_sourceOrders == null) return;

        var siblings = _sourceOrders.Where(o =>
            o.ChannelCode == _unmappedChannelCode &&
            (o.Status == "매핑 실패" || o.Status == "매핑 키 없음") &&
            (o.ProductName ?? string.Empty) + (o.OptionName ?? string.Empty) == key);

        foreach (var sibling in siblings)
        {
            sibling.MappedSku = targetSku;
            sibling.Status = status;
        }
    }

    /// <summary>
    /// 상세조건 저장 직후, 현재 채널의 모든 조건부 매핑 규칙을 아직 매핑되지 않은 발주 항목들에
    /// 재적용한다. 조건 편집 → 저장 즉시 미매핑 목록에 결과가 반영되게 하기 위함이다.
    /// </summary>
    private void ReapplyConditionRulesToUnmappedItems()
    {
        if (_sourceOrders == null || string.IsNullOrEmpty(_unmappedChannelCode)) return;

        var conditionRules = _mappingRepository.GetRules(MappingRuleType.Condition, _unmappedChannelCode);
        if (conditionRules.Count == 0) return;

        var rulesWithDetails = conditionRules
            .Where(r => !string.IsNullOrWhiteSpace(r.TargetSku))
            .Select(r => (rule: r, details: _mappingRepository.GetConditionDetails(r.Id)))
            .Where(rd => rd.details.Count > 0)
            .ToList();

        if (rulesWithDetails.Count == 0) return;

        var changed = false;
        foreach (var order in _sourceOrders)
        {
            if (order.ChannelCode != _unmappedChannelCode) continue;
            if (order.Status != "매핑 실패" && order.Status != "매핑 키 없음") continue;

            foreach (var (rule, details) in rulesWithDetails)
            {
                if (!ConditionEvaluator.Matches(details, order)) continue;
                order.MappedSku = rule.TargetSku;
                order.Status = "매핑(조건부)";
                changed = true;
                break;
            }
        }

        if (changed)
        {
            RefreshUnmappedGrid();
            _onMappingApplied?.Invoke();
        }
    }

    /// <summary>
    /// CSKU(채널+CSKU코드)가 이미 존재하면 "기존 CSKU에 조건을 추가"하는 것으로 간주해 그 CSKU의
    /// 납품가/송장표시명은 그대로 두고(매핑 규칙만 ApplyMappingToItem이 추가) 안내만 띄운다.
    /// 존재하지 않으면 입력된 납품단가/송장표시명으로 새로 만든다. 납품단가는 VAT별도로 선택했으면
    /// 1.1을 곱해 VAT포함 기준으로 변환한다.
    /// </summary>
    private void SaveChannelSkuInfoFromUnmappedPanel(string cskuCode, string masterSku, string channelCode)
    {
        decimal.TryParse(_unmappedSupplyPriceTextBox.Text, out var enteredPrice);
        var supplyPrice = enteredPrice > 0
            ? (_unmappedVatExcludedRadio.Checked ? Math.Round(enteredPrice * 1.1m, 0) : enteredPrice)
            : 0m;

        if (!_channelSkuRepository.CreateIfNew(channelCode, cskuCode, masterSku, supplyPrice, _unmappedInvoiceNameTextBox.Text))
        {
            MessageBox.Show(
                $"기존 CSKU '{cskuCode}'가 이미 존재합니다. 이 상품명/옵션명 조합을 그 CSKU에 매핑하는 조건을 추가합니다.",
                "기존 CSKU 존재", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static readonly Dictionary<string, StdField> UnmappedGridColumnToStdField = new()
    {
        ["ProductName"] = StdField.ProductName,
        ["OptionName"] = StdField.OptionName,
        ["Quantity"] = StdField.Quantity,
    };

    /// <summary>
    /// 우클릭한 셀(상품명/옵션명/수량)의 값을 조건부 매핑 조건으로 즉시 추가한다. SalesManagerV2의
    /// "셀 클릭 → 조건 자동 주입" 패턴을 그대로 가져온 것 — "조건부 매핑(상세)" 탭에 이미 편집
    /// 중인 규칙이 열려 있으면 그 규칙에 조건을 누적해서 추가하고(아직 저장 전, '상세조건 저장'을
    /// 눌러야 반영), 편집 중인 규칙이 없으면 이 한 조건만으로 새 규칙을 만들어 그 탭으로 이동한다.
    /// </summary>
    private void AddCellAsConditionToRule()
    {
        if (_unmappedGrid.CurrentCell is null)
        {
            MessageBox.Show("조건으로 추가할 셀을 먼저 우클릭하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var columnName = _unmappedGrid.Columns[_unmappedGrid.CurrentCell.ColumnIndex].Name;
        if (!UnmappedGridColumnToStdField.TryGetValue(columnName, out var field))
        {
            MessageBox.Show("이 열은 조건부 매핑 조건으로 사용할 수 없습니다(상품명/옵션명/수량만 가능합니다).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var value = _unmappedGrid.CurrentCell.Value?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show("선택한 셀에 값이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (string.IsNullOrEmpty(_unmappedChannelCode)) return;

        var newDetail = new MappingConditionDetail { HeaderField = field, Operator = ConditionOperator.Contains, TargetValue = value, Logic = ConditionLogic.And };

        if (_selectedConditionRuleId >= 0 && _conditionDetailGrid.DataSource is BindingList<MappingConditionDetail> details)
        {
            newDetail.RuleId = _selectedConditionRuleId;
            details.Add(newDetail);
            UpdateConditionPreview();
        }
        else
        {
            var newRuleId = _mappingRepository.AddConditionRuleWithDetails(_unmappedChannelCode, value, string.Empty, [newDetail]);
            LoadConditionRules(_unmappedChannelCode);
            SelectConditionRuleById(newRuleId);
        }

        _ruleTabControl.SelectedTab = _conditionDetailTabPage;
    }

    /// <summary>
    /// 선택한 미매핑 항목(상품명+옵션명 조합)을 매핑 대상에서 제외(배송비/수수료 등)하는
    /// 예외 규칙으로 저장한다.
    /// </summary>
    private void ExcludeSelectedUnmapped()
    {
        if (_unmappedGrid.CurrentRow?.DataBoundItem is not OfsOrderItem item)
        {
            MessageBox.Show("제외 처리할 미매핑 항목을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (string.IsNullOrEmpty(_unmappedChannelCode)) return;

        var key = (item.ProductName ?? string.Empty) + (item.OptionName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(key)) return;

        var confirm = MessageBox.Show(
            $"'{item.ProductName} {item.OptionName}' 조합을 매핑 대상에서 제외(배송비/수수료 등)하도록 예외 규칙으로 저장하시겠습니까?",
            "예외 처리 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _mappingRepository.UpsertRule(MappingRuleType.Exception, _unmappedChannelCode, key, SkuMapper.ExcludedTargetSku);

        item.MappedSku = null;
        item.Status = "제외(배송비 등)";
        ApplySameKeyToOtherUnmappedSiblings(key, null, "제외(배송비 등)");

        RefreshUnmappedGrid();
        _onMappingApplied?.Invoke();
    }

    private TabPage CreateConflictTabPage()
    {
        var tabPage = new TabPage("충돌 감지");

        _conflictGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.MistyRose },
        };
        _conflictGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "RuleTypeName", HeaderText = "규칙 유형", DataPropertyName = "RuleTypeName", Width = 100 },
            new DataGridViewTextBoxColumn { Name = "KeyA", HeaderText = "키 A", DataPropertyName = "KeyA", Width = 200 },
            new DataGridViewTextBoxColumn { Name = "TargetSkuA", HeaderText = "SKU A", DataPropertyName = "TargetSkuA", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "KeyB", HeaderText = "키 B", DataPropertyName = "KeyB", Width = 200 },
            new DataGridViewTextBoxColumn { Name = "TargetSkuB", HeaderText = "SKU B", DataPropertyName = "TargetSkuB", Width = 120 }
        );

        tabPage.Controls.Add(_conflictGrid);
        return tabPage;
    }

    /// <summary>
    /// 예외/1:1/임시/조건부 4종 규칙을 한 테이블에 모아 채널/키워드로 검색하고 체크박스로 다중선택
    /// 삭제할 수 있는 탭(SalesManagerV2의 MappingRulesManagerDialog 패턴). CSKU 송장표시명/납품가를
    /// JOIN해서 함께 보여준다 — 어떤 SKU로 매핑되는지뿐 아니라 그 CSKU의 실제 정보까지 한눈에 확인.
    /// </summary>
    private TabPage CreateUnifiedRulesTabPage()
    {
        var tabPage = new TabPage("전체 규칙 관리");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var filterPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnRefresh = new Button { Text = "새로고침", Size = new Size(80, 28) };
        btnRefresh.Click += (s, e) => LoadUnifiedRules();
        filterPanel.Controls.Add(btnRefresh);

        filterPanel.Controls.Add(new Label { Text = "채널:", AutoSize = true, Padding = new Padding(10, 6, 2, 0) });
        _unifiedFilterChannelCombo = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        _unifiedFilterChannelCombo.SelectedIndexChanged += (s, e) => ApplyUnifiedRuleFilter();
        filterPanel.Controls.Add(_unifiedFilterChannelCombo);

        filterPanel.Controls.Add(new Label { Text = "검색 항목:", AutoSize = true, Padding = new Padding(10, 6, 2, 0) });
        _unifiedFilterTargetCombo = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        _unifiedFilterTargetCombo.Items.AddRange(["전체", "타입", "채널", "키", "대상 SKU", "상세"]);
        _unifiedFilterTargetCombo.SelectedIndex = 0;
        _unifiedFilterTargetCombo.SelectedIndexChanged += (s, e) => ApplyUnifiedRuleFilter();
        filterPanel.Controls.Add(_unifiedFilterTargetCombo);

        filterPanel.Controls.Add(new Label { Text = "검색어:", AutoSize = true, Padding = new Padding(10, 6, 2, 0) });
        _unifiedSearchTextBox = new TextBox { Width = 180 };
        _unifiedSearchTextBox.TextChanged += (s, e) => ApplyUnifiedRuleFilter();
        filterPanel.Controls.Add(_unifiedSearchTextBox);

        _unifiedRulesGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _unifiedRulesGrid.Columns.AddRange(
            new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "선택", DataPropertyName = "Selected", Width = 40 },
            new DataGridViewTextBoxColumn { Name = "TypeLabel", HeaderText = "타입", DataPropertyName = "TypeLabel", Width = 80, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "ChannelCode", HeaderText = "채널", DataPropertyName = "ChannelCode", Width = 90, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "키", DataPropertyName = "Key", Width = 200, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "TargetSku", HeaderText = "대상 SKU(CSKU)", DataPropertyName = "TargetSku", Width = 130, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "CskuMsku", HeaderText = "매칭된 마스터SKU", DataPropertyName = "CskuMsku", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "CskuInvoiceDisplayName", HeaderText = "CSKU 송장표시명", DataPropertyName = "CskuInvoiceDisplayName", Width = 140 },
            new DataGridViewTextBoxColumn { Name = "CskuSupplyPrice", HeaderText = "CSKU 납품가", DataPropertyName = "CskuSupplyPrice", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "CskuNote", HeaderText = "CSKU 비고", DataPropertyName = "CskuNote", Width = 140 },
            new DataGridViewTextBoxColumn { Name = "CskuUpdatedAt", HeaderText = "CSKU 마지막 수정", DataPropertyName = "CskuUpdatedAt", Width = 120, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm" } },
            new DataGridViewTextBoxColumn { Name = "Detail", HeaderText = "상세(조건부 매핑 조건 등)", DataPropertyName = "Detail", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true }
        );
        _unifiedRulesGrid.CellEndEdit += OnUnifiedRulesGridCellEndEdit;
        _unifiedRulesGrid.DataError += (s, e) => { e.ThrowException = false; };

        var menu = new ContextMenuStrip();
        menu.Items.Add("이 규칙 수정하기(해당 탭으로 이동)", null, (s, e) => OnEditSelectedUnifiedRuleClick());
        menu.Items.Add("CSKU 변경 이력 보기", null, (s, e) => OnViewSelectedCskuHistoryClick());
        _unifiedRulesGrid.ContextMenuStrip = menu;
        // CSKU 정보 열(마스터SKU/송장표시명/납품가/비고)은 이제 직접 편집 가능해졌으므로, 그 열을
        // 더블클릭하면 편집만 시작하고 "규칙 수정 탭으로 이동"은 그 외 열(타입/채널/키/대상SKU/상세)
        // 더블클릭 때만 동작하게 한다.
        _unifiedRulesGrid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (IsCskuEditableColumn(_unifiedRulesGrid.Columns[e.ColumnIndex].Name)) return;
            OnEditSelectedUnifiedRuleClick();
        };

        var bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnDelete = new Button { Text = "선택 삭제", Size = new Size(90, 28) };
        btnDelete.Click += (s, e) => OnDeleteSelectedUnifiedRulesClick();
        bottomPanel.Controls.Add(btnDelete);
        bottomPanel.Controls.Add(new Label
        {
            Text = "체크박스로 여러 건을 선택해 한꺼번에 삭제할 수 있습니다. 마스터SKU/송장표시명/납품가/비고는 직접 수정하면 바로 저장됩니다. " +
                   "그 외 열을 더블클릭(또는 우클릭 → 수정하기)하면 해당 규칙의 원래 탭으로 이동합니다.",
            AutoSize = true,
            Padding = new Padding(10, 6, 0, 0),
            ForeColor = Color.DimGray,
        });

        layout.Controls.Add(filterPanel, 0, 0);
        layout.Controls.Add(_unifiedRulesGrid, 0, 1);
        layout.Controls.Add(bottomPanel, 0, 2);
        tabPage.Controls.Add(layout);

        return tabPage;
    }

    private void LoadUnifiedRules()
    {
        var rows = new List<UnifiedRuleRow>();
        var cskuCache = new Dictionary<(string ChannelCode, string TargetSku), ChannelSkuModel?>();

        ChannelSkuModel? ResolveCsku(string channelCode, string targetSku)
        {
            var cacheKey = (channelCode, targetSku);
            if (!cskuCache.TryGetValue(cacheKey, out var csku))
            {
                csku = string.IsNullOrEmpty(targetSku) || targetSku == SkuMapper.ExcludedTargetSku
                    ? null
                    : _channelSkuRepository.GetByChannelAndCskuCode(channelCode, targetSku);
                cskuCache[cacheKey] = csku;
            }
            return csku;
        }

        void AddSimpleRules(MappingRuleType ruleType, string typeLabel)
        {
            foreach (var rule in _mappingRepository.GetAllRules(ruleType))
            {
                var csku = ResolveCsku(rule.ChannelCode, rule.TargetSku);
                rows.Add(new UnifiedRuleRow
                {
                    TypeLabel = typeLabel,
                    RuleType = ruleType,
                    RuleId = rule.Id,
                    ChannelCode = rule.ChannelCode,
                    Key = rule.Key,
                    TargetSku = rule.TargetSku,
                    Detail = "-",
                    CskuMsku = csku?.Msku ?? string.Empty,
                    CskuInvoiceDisplayName = csku?.InvoiceDisplayName ?? string.Empty,
                    CskuSupplyPrice = csku?.SupplyPrice,
                    CskuNote = csku?.Note ?? string.Empty,
                    CskuUpdatedAt = csku?.UpdatedAt,
                });
            }
        }

        AddSimpleRules(MappingRuleType.Exception, "예외 처리");
        AddSimpleRules(MappingRuleType.Exact, "1:1 매핑");
        AddSimpleRules(MappingRuleType.Temp, "임시 매핑");

        foreach (var (rule, details) in _mappingRepository.GetAllConditionRulesWithDetails())
        {
            var csku = ResolveCsku(rule.ChannelCode, rule.TargetSku);
            var detailText = string.Join(" ; ", details.Select(d => $"{d.Logic} {d.HeaderField} {d.Operator} \"{d.TargetValue}\""));
            rows.Add(new UnifiedRuleRow
            {
                TypeLabel = "조건부 매핑",
                RuleType = MappingRuleType.Condition,
                RuleId = rule.Id,
                ChannelCode = rule.ChannelCode,
                Key = rule.Key,
                TargetSku = rule.TargetSku,
                Detail = string.IsNullOrEmpty(detailText) ? "(조건 없음)" : detailText,
                CskuMsku = csku?.Msku ?? string.Empty,
                CskuInvoiceDisplayName = csku?.InvoiceDisplayName ?? string.Empty,
                CskuSupplyPrice = csku?.SupplyPrice,
                CskuNote = csku?.Note ?? string.Empty,
                CskuUpdatedAt = csku?.UpdatedAt,
            });
        }

        _allUnifiedRules = rows;

        var previousChannel = _unifiedFilterChannelCombo.SelectedItem as string;
        var channels = rows.Select(r => r.ChannelCode).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
        _unifiedFilterChannelCombo.Items.Clear();
        _unifiedFilterChannelCombo.Items.Add("(전체)");
        foreach (var channel in channels) _unifiedFilterChannelCombo.Items.Add(channel);
        _unifiedFilterChannelCombo.SelectedItem = previousChannel != null && _unifiedFilterChannelCombo.Items.Contains(previousChannel) ? previousChannel : "(전체)";

        ApplyUnifiedRuleFilter();
    }

    private void ApplyUnifiedRuleFilter()
    {
        var channelFilter = _unifiedFilterChannelCombo.SelectedItem as string;
        var target = _unifiedFilterTargetCombo.SelectedItem as string ?? "전체";
        var keyword = _unifiedSearchTextBox.Text.Trim();

        var filtered = _allUnifiedRules
            .Where(r => string.IsNullOrEmpty(channelFilter) || channelFilter == "(전체)" || r.ChannelCode == channelFilter)
            .Where(r => MatchesUnifiedRuleKeyword(r, target, keyword))
            .ToList();

        _unifiedRulesGrid.DataSource = new BindingList<UnifiedRuleRow>(filtered);
    }

    private static bool MatchesUnifiedRuleKeyword(UnifiedRuleRow row, string target, string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return true;
        bool Has(string text) => text.Contains(keyword, StringComparison.OrdinalIgnoreCase);

        return target switch
        {
            "타입" => Has(row.TypeLabel),
            "채널" => Has(row.ChannelCode),
            "키" => Has(row.Key),
            "대상 SKU" => Has(row.TargetSku),
            "상세" => Has(row.Detail),
            _ => Has(row.TypeLabel) || Has(row.ChannelCode) || Has(row.Key) || Has(row.TargetSku) || Has(row.Detail) || Has(row.CskuInvoiceDisplayName),
        };
    }

    private static readonly HashSet<string> CskuEditableColumnNames =
        ["CskuMsku", "CskuInvoiceDisplayName", "CskuSupplyPrice", "CskuNote"];

    private static bool IsCskuEditableColumn(string columnName) => CskuEditableColumnNames.Contains(columnName);

    /// <summary>
    /// "전체 규칙 관리" 탭에서 마스터SKU/송장표시명/납품가/비고를 직접 고치면 그 규칙이 가리키는
    /// CSKU(ChannelSkuTable) 레코드에 바로 반영한다. 같은 CSKU를 가리키는 다른 규칙 행이 있을 수
    /// 있고(예: 1:1 매핑 + 예외처리가 같은 CSKU를 공유) 마지막 수정일도 새로 반영해야 하므로,
    /// 저장 성공/실패 여부와 무관하게 항상 LoadUnifiedRules()로 전체를 다시 읽어 화면을 DB 상태와
    /// 맞춘다(편집을 취소/되돌리는 효과도 겸함).
    /// </summary>
    private void OnUnifiedRulesGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (!IsCskuEditableColumn(_unifiedRulesGrid.Columns[e.ColumnIndex].Name)) return;
        if (_unifiedRulesGrid.Rows[e.RowIndex].DataBoundItem is not UnifiedRuleRow row) return;

        try
        {
            if (string.IsNullOrEmpty(row.TargetSku) || row.TargetSku == SkuMapper.ExcludedTargetSku)
            {
                _globalStatusLabel.ForeColor = Color.Red;
                _globalStatusLabel.Text = "이 항목은 CSKU가 연결되어 있지 않아 여기서 수정할 수 없습니다.";
                return;
            }

            var existing = _channelSkuRepository.GetByChannelAndCskuCode(row.ChannelCode, row.TargetSku);
            if (existing == null)
            {
                _globalStatusLabel.ForeColor = Color.Red;
                _globalStatusLabel.Text = $"'{row.TargetSku}'은(는) 등록된 CSKU가 아니라(마스터SKU 코드를 직접 매핑에 사용 중) 여기서 수정할 수 없습니다.";
                return;
            }

            if (string.IsNullOrWhiteSpace(row.CskuMsku))
            {
                _globalStatusLabel.ForeColor = Color.Red;
                _globalStatusLabel.Text = "마스터SKU는 비워둘 수 없습니다.";
                return;
            }

            _channelSkuRepository.Upsert(new ChannelSkuModel
            {
                ChannelCode = row.ChannelCode,
                CskuCode = row.TargetSku,
                Msku = row.CskuMsku.Trim(),
                SupplyPrice = row.CskuSupplyPrice ?? 0m,
                InvoiceDisplayName = string.IsNullOrWhiteSpace(row.CskuInvoiceDisplayName) ? null : row.CskuInvoiceDisplayName.Trim(),
                Note = string.IsNullOrWhiteSpace(row.CskuNote) ? null : row.CskuNote.Trim(),
            });

            _globalStatusLabel.ForeColor = Color.DarkGreen;
            _globalStatusLabel.Text = $"CSKU '{row.TargetSku}' 정보가 저장되었습니다. ({DateTime.Now:HH:mm:ss})";
        }
        catch (Exception ex)
        {
            _globalStatusLabel.ForeColor = Color.Red;
            _globalStatusLabel.Text = $"저장 중 오류가 발생했습니다: {ex.Message}";
        }
        finally
        {
            LoadUnifiedRules();
        }
    }

    /// <summary>선택된 행의 CSKU 변경 이력(마스터SKU/송장표시명/비고/납품가)을 보여준다.</summary>
    private void OnViewSelectedCskuHistoryClick()
    {
        if (_unifiedRulesGrid.CurrentRow?.DataBoundItem is not UnifiedRuleRow row) return;

        if (string.IsNullOrEmpty(row.TargetSku) || row.TargetSku == SkuMapper.ExcludedTargetSku ||
            _channelSkuRepository.GetByChannelAndCskuCode(row.ChannelCode, row.TargetSku) == null)
        {
            MessageBox.Show("이 항목은 등록된 CSKU가 없어 변경 이력을 조회할 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var historyForm = new ChannelSkuHistoryForm(row.ChannelCode, row.TargetSku);
        FormManager.ApplyBoundsTracking(historyForm);
        historyForm.ShowDialog(this);
    }

    /// <summary>
    /// 체크된 규칙들을 삭제한다. 현재 채널에 발주서가 로드되어 있으면(_sourceOrders) 삭제로 인해
    /// 미매핑 상태로 되돌아갈 건수를 추정해 경고에 포함한다(레거시의 "약 N건의 매핑이
    /// 해제됩니다" 경고와 같은 취지).
    /// </summary>
    private void OnDeleteSelectedUnifiedRulesClick()
    {
        if (_unifiedRulesGrid.DataSource is not BindingList<UnifiedRuleRow> rows) return;

        var selected = rows.Where(r => r.Selected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("삭제할 규칙을 먼저 체크하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var message = $"{selected.Count}건의 규칙을 삭제하시겠습니까?";
        if (_sourceOrders != null)
        {
            var impact = EstimateUnifiedRuleDeleteImpact(selected);
            if (impact > 0) message += $"\n\n⚠ 현재 로드된 발주서 중 약 {impact}건이 미매핑 상태로 되돌아갈 수 있습니다.";
        }

        if (MessageBox.Show(message, "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        foreach (var row in selected)
        {
            if (row.RuleType == MappingRuleType.Condition)
            {
                _mappingRepository.DeleteConditionRule(row.RuleId);
            }
            else
            {
                _mappingRepository.DeleteRule(row.RuleType, row.RuleId);
            }
        }

        LoadUnifiedRules();
        if (!string.IsNullOrEmpty(_channelComboBox.SelectedValue as string)) LoadRulesForSelectedChannel();
        // LoadUnifiedRules/LoadRulesForSelectedChannel이 그리드를 통째로 다시 그린 직후라 같은
        // 위험 패턴 — 모달 대신 상태표시줄로 안내한다.
        _globalStatusLabel.ForeColor = Color.DarkGreen;
        _globalStatusLabel.Text = $"{selected.Count}건을 삭제했습니다. ({DateTime.Now:HH:mm:ss})";
        MappingRulesChanged?.Invoke();
    }

    private int EstimateUnifiedRuleDeleteImpact(List<UnifiedRuleRow> selected)
    {
        if (_sourceOrders == null) return 0;

        var count = 0;
        foreach (var row in selected)
        {
            var candidates = _sourceOrders.Where(o => o.ChannelCode == row.ChannelCode);
            if (row.RuleType == MappingRuleType.Condition)
            {
                var details = _mappingRepository.GetConditionDetails(row.RuleId);
                count += candidates.Count(o => ConditionEvaluator.Matches(details, o));
            }
            else
            {
                count += candidates.Count(o => (o.ProductName ?? string.Empty) + (o.OptionName ?? string.Empty) == row.Key);
            }
        }
        return count;
    }

    /// <summary>
    /// 더블클릭/우클릭한 규칙의 원래 탭으로 이동해 그 줄을 선택한다. 현재 상단에서 선택된 채널과
    /// 다른 채널의 규칙이면, 채널 전환 시 다른 탭들이 비동기로 다시 로드되는 도중에 선택을
    /// 시도하면 어긋날 수 있어 안내만 하고 직접 채널을 바꿔달라고 한다.
    /// </summary>
    private void OnEditSelectedUnifiedRuleClick()
    {
        if (_unifiedRulesGrid.CurrentRow?.DataBoundItem is not UnifiedRuleRow row) return;

        var currentChannel = _channelComboBox.SelectedValue as string;
        if (!string.Equals(currentChannel, row.ChannelCode, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                $"이 규칙은 '{row.ChannelCode}' 채널의 규칙입니다.\n상단의 채널을 '{row.ChannelCode}'로 바꾼 뒤 다시 시도해주세요.",
                "다른 채널", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (row.RuleType == MappingRuleType.Condition)
        {
            _ruleTabControl.SelectedTab = _conditionDetailTabPage;
            SelectConditionRuleById(row.RuleId);
        }
        else
        {
            SelectSimpleRuleAndSwitchTab(row.RuleType, row.RuleId);
        }
    }

    private void SelectSimpleRuleAndSwitchTab(MappingRuleType ruleType, long ruleId)
    {
        foreach (TabPage tabPage in _ruleTabControl.TabPages)
        {
            if (tabPage.Controls.Count == 0 || tabPage.Controls[0] is not ExcelLikeDataGridView grid || grid.Tag is not MappingRuleType rt || rt != ruleType) continue;

            _ruleTabControl.SelectedTab = tabPage;
            foreach (DataGridViewRow gridRow in grid.Rows)
            {
                if (gridRow.DataBoundItem is MappingRule rule && rule.Id == ruleId)
                {
                    grid.CurrentCell = gridRow.Cells[0];
                    break;
                }
            }
            return;
        }
    }

    /// <summary>
    /// 조건부 매핑 규칙별 여러 상세조건(HeaderField/Operator/TargetValue/Logic)을 추가/삭제/수정하는 전용 탭.
    /// 왼쪽에서 규칙을 고르면 오른쪽에 그 규칙의 상세조건이 뜨고, 각 영역의 저장 버튼이 즉시 DB에 반영한다
    /// (매핑관리창 상단의 일괄 [저장] 버튼/단순 그리드와는 무관하게 동작).
    /// </summary>
    /// <summary>
    /// 노션 5.1 피드백: 조건부 매핑을 빨리 끝내는 게 목적인 화면인데, 왼쪽에 항상 떠 있는 전체
    /// 규칙 목록은 방해만 되고 느려 보이는 원인이었다(목록을 새로 그릴 때마다 그리드 갱신).
    /// 좌측 목록을 이 탭에서는 빼고, 필요할 때만 "전체 조건부규칙 보기" 버튼으로 별도 창
    /// (ConditionRuleListForm)을 띄워 거기서 고르거나 새로 만들면 그 결과만 이 탭에 로드한다.
    /// _conditionRuleGrid 자체는 화면에 추가하지 않지만 내부 상태 보관용으로는 그대로 쓴다
    /// (LoadConditionRules/SelectConditionRuleById/OnConditionRuleSelectionChanged 등 기존
    /// 데이터 흐름을 바꾸지 않기 위함 — DataGridView는 화면에 안 붙여도 DataSource/CurrentCell이
    /// 정상 동작한다).
    /// </summary>
    private TabPage CreateConditionDetailTabPage()
    {
        var tabPage = new TabPage("조건부 매핑(상세)");

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 3, 5, 3) };
        var btnAddRule = new Button { Text = "새 규칙 추가", Size = new Size(100, 28) };
        btnAddRule.Click += OnAddConditionRuleClick;
        var btnBrowseAllRules = new Button { Text = "전체 조건부규칙 보기", Size = new Size(140, 28) };
        btnBrowseAllRules.Click += OnBrowseAllConditionRulesClick;
        var btnDeleteRule = new Button { Text = "이 규칙 삭제", Size = new Size(100, 28) };
        btnDeleteRule.Click += OnDeleteConditionRuleClick;
        topToolbar.Controls.Add(btnAddRule);
        topToolbar.Controls.Add(btnBrowseAllRules);
        topToolbar.Controls.Add(btnDeleteRule);

        // 화면에는 안 붙이지만, 기존 LoadConditionRules/SelectConditionRuleById 등이 그대로
        // 동작하도록 내부 상태 보관용으로 유지한다.
        _conditionRuleGrid = new DataGridView
        {
            AutoGenerateColumns = false,
            ReadOnly = true,
        };
        _conditionRuleGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "키(레거시 매칭용/요약)", DataPropertyName = "Key" },
            new DataGridViewTextBoxColumn { Name = "TargetSku", HeaderText = "대상 SKU", DataPropertyName = "TargetSku" }
        );
        _conditionRuleGrid.SelectionChanged += OnConditionRuleSelectionChanged;

        // 우측(이제 전체 폭): 선택한 규칙의 요약 정보 + SKU 검색 목록 + CSKU 목록/편집 + 상세조건 목록
        var rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5 };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));   // 규칙 요약(키/대상SKU)
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));  // SKU 검색 리스트(좌) + CSKU 목록(우)
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // CSKU 입력 폼
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 상세 조건 그리드
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // 조건 버튼

        var summaryPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        _conditionKeyTextBox = new TextBox { Width = 220 };
        _conditionTargetSkuTextBox = new TextBox { Width = 150 };
        var btnSaveSummary = new Button { Text = "규칙 정보 저장", Size = new Size(110, 28) };
        btnSaveSummary.Click += OnSaveConditionSummaryClick;
        _btnUndoLastMapping = new Button { Text = "직전매핑취소", Size = new Size(100, 28), Enabled = false, ForeColor = Color.Firebrick };
        _btnUndoLastMapping.Click += OnUndoLastMappingClick;
        summaryPanel.Controls.Add(new Label { Text = "키(요약):", AutoSize = true, Padding = new Padding(0, 7, 3, 0) });
        summaryPanel.Controls.Add(_conditionKeyTextBox);
        summaryPanel.Controls.Add(new Label { Text = "대상 SKU:", AutoSize = true, Padding = new Padding(10, 7, 3, 0) });
        summaryPanel.Controls.Add(_conditionTargetSkuTextBox);
        summaryPanel.Controls.Add(btnSaveSummary);
        summaryPanel.Controls.Add(_btnUndoLastMapping);
        _conditionTargetSkuTextBox.TextChanged += (s, e) => { FilterSkuSearchList(); RefreshConditionCskuGrid(); };
        _conditionPreviewLabel = new Label
        {
            Text = "예상 매칭 건수: -",
            AutoSize = true,
            Padding = new Padding(15, 7, 0, 0),
            ForeColor = Color.Blue,
            Font = new Font(Font, FontStyle.Bold),
        };
        summaryPanel.Controls.Add(_conditionPreviewLabel);
        summaryPanel.Controls.Add(new Label
        {
            Text = "※ 여기서 추가한 모든 조건은 AND/OR(Logic 열)로 차례대로 결합되어 평가됩니다.",
            AutoSize = true,
            Padding = new Padding(0, 5, 0, 0),
            ForeColor = Color.DimGray,
        });

        _conditionDetailGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
        };
        var headerFieldColumn = new DataGridViewComboBoxColumn
        {
            Name = "HeaderField",
            HeaderText = "비교할 항목",
            DataPropertyName = "HeaderField",
            DataSource = Enum.GetValues(typeof(StdField)),
            Width = 130,
        };
        var operatorColumn = new DataGridViewComboBoxColumn
        {
            Name = "Operator",
            HeaderText = "조건",
            DataPropertyName = "Operator",
            DataSource = Enum.GetValues(typeof(ConditionOperator)),
            Width = 110,
        };
        var targetValueColumn = new DataGridViewTextBoxColumn
        {
            Name = "TargetValue",
            HeaderText = "비교할 값",
            DataPropertyName = "TargetValue",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        };
        var logicColumn = new DataGridViewComboBoxColumn
        {
            Name = "Logic",
            HeaderText = "다음 조건과 결합",
            DataPropertyName = "Logic",
            DataSource = Enum.GetValues(typeof(ConditionLogic)),
            Width = 110,
        };
        _conditionDetailGrid.Columns.AddRange(headerFieldColumn, operatorColumn, targetValueColumn, logicColumn);
        // 콤보 열(항목/조건/논리)을 고치는 즉시(셀에서 벗어나길 기다리지 않고) 미리보기가 갱신되도록
        // 바로 커밋시킨다. 텍스트 입력(비교할 값)은 CellValueChanged만으로 충분하다(포커스 이동 시 커밋됨).
        _conditionDetailGrid.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (_conditionDetailGrid.IsCurrentCellDirty) _conditionDetailGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _conditionDetailGrid.CellValueChanged += (s, e) => UpdateConditionPreview();

        var detailButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var btnAddDetail = new Button { Text = "조건 추가", Size = new Size(90, 28) };
        btnAddDetail.Click += OnAddConditionDetailClick;
        var btnDeleteDetail = new Button { Text = "조건 삭제", Size = new Size(90, 28) };
        btnDeleteDetail.Click += OnDeleteConditionDetailClick;
        var btnSaveDetails = new Button { Text = "상세조건 저장", Size = new Size(110, 28) };
        btnSaveDetails.Click += OnSaveConditionDetailsClick;
        detailButtonPanel.Controls.Add(btnAddDetail);
        detailButtonPanel.Controls.Add(btnDeleteDetail);
        detailButtonPanel.Controls.Add(btnSaveDetails);
        _conditionSaveFeedbackLabel = new Label
        {
            AutoSize = true,
            Padding = new Padding(15, 7, 0, 0),
            ForeColor = Color.DarkGreen,
        };
        detailButtonPanel.Controls.Add(_conditionSaveFeedbackLabel);

        _skuSearchListBox = new ListBox { Dock = DockStyle.Fill };
        _skuSearchListBox.Click += (s, e) =>
        {
            if (_skuSearchListBox.SelectedItem is string selected && _conditionTargetSkuTextBox.Enabled)
                _conditionTargetSkuTextBox.Text = selected;
        };
        var skuSearchLabel = new Label { Text = "대상 SKU 검색 — 타이핑하면 자동 필터, 클릭 시 자동 입력", Dock = DockStyle.Top, Height = 18, ForeColor = Color.DimGray };
        var skuSearchPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5, 0, 5, 3) };
        skuSearchPanel.Controls.Add(_skuSearchListBox);
        skuSearchPanel.Controls.Add(skuSearchLabel);

        _conditionCskuGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _conditionCskuGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "CskuCode", HeaderText = "CSKU 코드", DataPropertyName = "CskuCode", Width = 140 },
            new DataGridViewTextBoxColumn { Name = "InvoiceDisplayName", HeaderText = "송장표시명", DataPropertyName = "InvoiceDisplayName", Width = 150 },
            new DataGridViewTextBoxColumn { Name = "SupplyPrice", HeaderText = "납품가", DataPropertyName = "SupplyPrice", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _conditionCskuGrid.SelectionChanged += (s, e) => OnConditionCskuGridSelectionChanged();
        var cskuGridLabel = new Label { Text = "이 채널의 CSKU 목록 (대상 SKU 기준 — 클릭 시 대상으로 지정)", Dock = DockStyle.Top, Height = 18, ForeColor = Color.DimGray };
        var cskuGridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5, 0, 5, 3) };
        cskuGridPanel.Controls.Add(_conditionCskuGrid);
        cskuGridPanel.Controls.Add(cskuGridLabel);

        var skuCskuSplit = new PersistentSplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 320,
            PersistenceKey = "MappingForm.ConditionSkuCskuSplit"
        };
        skuCskuSplit.Panel1.Controls.Add(skuSearchPanel);
        skuCskuSplit.Panel2.Controls.Add(cskuGridPanel);

        var cskuInputPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 4, 5, 0) };
        cskuInputPanel.Controls.Add(new Label { Text = "CSKU 코드:", AutoSize = true, Padding = new Padding(0, 5, 3, 0) });
        _conditionCskuCodeBox = new TextBox { Width = 140 };
        cskuInputPanel.Controls.Add(_conditionCskuCodeBox);
        cskuInputPanel.Controls.Add(new Label { Text = "송장표시명:", AutoSize = true, Padding = new Padding(10, 5, 3, 0) });
        _conditionCskuInvoiceBox = new TextBox { Width = 200 };
        cskuInputPanel.Controls.Add(_conditionCskuInvoiceBox);
        cskuInputPanel.Controls.Add(new Label { Text = "납품가:", AutoSize = true, Padding = new Padding(10, 5, 3, 0) });
        _conditionCskuPriceBox = new TextBox { Width = 85 };
        cskuInputPanel.Controls.Add(_conditionCskuPriceBox);
        _conditionVatIncludedRadio = new RadioButton { Text = "VAT포함", AutoSize = true, Checked = true, Padding = new Padding(8, 3, 0, 0) };
        _conditionVatExcludedRadio = new RadioButton { Text = "VAT별도", AutoSize = true, Padding = new Padding(4, 3, 0, 0) };
        cskuInputPanel.Controls.Add(_conditionVatIncludedRadio);
        cskuInputPanel.Controls.Add(_conditionVatExcludedRadio);
        var btnSaveCsku = new Button { Text = "CSKU 저장", Size = new Size(90, 26) };
        btnSaveCsku.Click += OnSaveConditionCskuClick;
        cskuInputPanel.Controls.Add(btnSaveCsku);

        rightPanel.Controls.Add(summaryPanel, 0, 0);
        rightPanel.Controls.Add(skuCskuSplit, 0, 1);
        rightPanel.Controls.Add(cskuInputPanel, 0, 2);
        rightPanel.Controls.Add(_conditionDetailGrid, 0, 3);
        rightPanel.Controls.Add(detailButtonPanel, 0, 4);

        mainLayout.Controls.Add(topToolbar, 0, 0);
        mainLayout.Controls.Add(rightPanel, 0, 1);
        tabPage.Controls.Add(mainLayout);

        SetConditionDetailEditorEnabled(false);

        return tabPage;
    }

    /// <summary>"전체 조건부규칙 보기" — 별도 창에서 전체 목록(중복 강조+병합 포함)을 보여주고, 고른 규칙을 이 탭에 로드한다.</summary>
    private void OnBrowseAllConditionRulesClick(object? sender, EventArgs e)
    {
        var channelCode = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(channelCode))
        {
            MessageBox.Show("먼저 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var listForm = new ConditionRuleListForm(_mappingRepository, channelCode);
        FormManager.ApplyBoundsTracking(listForm);
        if (listForm.ShowDialog(this) != DialogResult.OK || listForm.SelectedRuleId == null) return;

        LoadConditionRules(channelCode);
        SelectConditionRuleById(listForm.SelectedRuleId.Value);
    }

    private void SetConditionDetailEditorEnabled(bool enabled)
    {
        _conditionKeyTextBox.Enabled = enabled;
        _conditionTargetSkuTextBox.Enabled = enabled;
        _conditionDetailGrid.Enabled = enabled;
        if (!enabled)
        {
            _conditionKeyTextBox.Text = string.Empty;
            _conditionTargetSkuTextBox.Text = string.Empty;
            _conditionDetailGrid.DataSource = null;
            _skuSearchListBox.Items.Clear();
            _conditionCskuGrid.DataSource = null;
            _conditionCskuCodeBox.Text = string.Empty;
            _conditionCskuInvoiceBox.Text = string.Empty;
            _conditionCskuPriceBox.Text = string.Empty;
        }
        UpdateConditionPreview();
    }

    private void LoadConditionRules(string channelCode)
    {
        var rules = _mappingRepository.GetRules(MappingRuleType.Condition, channelCode);
        _conditionRuleGrid.DataSource = new BindingList<MappingRule>(rules);
        _selectedConditionRuleId = -1;
        SetConditionDetailEditorEnabled(false);

        // 대상 SKU 자동완성 + 하단 검색 목록 갱신. 채널이 바뀔 때마다 다시 구성한다.
        var codes = _itemRepository.GetAll().Select(i => i.Sku)
            .Concat(_channelSkuRepository.GetAllByChannel(channelCode).Select(c => c.CskuCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToArray();
        _allSkuCodes = codes;
        var source = new AutoCompleteStringCollection();
        source.AddRange(codes);
        _conditionTargetSkuTextBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _conditionTargetSkuTextBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _conditionTargetSkuTextBox.AutoCompleteCustomSource = source;
        FilterSkuSearchList();
    }

    private void OnConditionRuleSelectionChanged(object? sender, EventArgs e)
    {
        if (_conditionRuleGrid.CurrentRow?.DataBoundItem is not MappingRule rule)
        {
            _selectedConditionRuleId = -1;
            SetConditionDetailEditorEnabled(false);
            return;
        }

        SelectConditionRule(rule);
    }

    /// <summary>
    /// "조건부 매핑(상세)" 탭의 키/대상SKU/조건 그리드를 채우는 실제 로직. <see cref="_conditionRuleGrid"/>
    /// 가 화면에 추가되지 않은(핸들이 생성되지 않은) 그리드라 <c>SelectionChanged</c> 이벤트가 항상
    /// 믿을 수 있게 발생하지 않는다 — 특히 <see cref="StartNewConditionRuleFor"/>처럼 막 만든 새
    /// 규칙을 프로그램적으로 선택할 때 이벤트가 발생하지 않아 화면이 빈 채로 남는 버그가 있었다("조건부
    /// 매핑창이 뜨지만 키/대상SKU/조건이 전부 빈칸으로 멈춘 것처럼 보임" 신고). 그래서
    /// <see cref="SelectConditionRuleById"/>가 이벤트에 의존하지 않고 이 메서드를 직접 호출한다.
    /// </summary>
    private void SelectConditionRule(MappingRule rule)
    {
        _selectedConditionRuleId = rule.Id;
        _conditionKeyTextBox.Text = rule.Key;
        _conditionTargetSkuTextBox.Text = rule.TargetSku;

        var details = _mappingRepository.GetConditionDetails(rule.Id);
        _conditionDetailGrid.DataSource = new BindingList<MappingConditionDetail>(details);
        SetConditionDetailEditorEnabled(true);
    }

    /// <summary>
    /// 마감/이익분석창에서 "조건부 매핑 규칙 추가"로 넘어왔을 때, 거기 로드된 정산파일 데이터로도
    /// "예상 매칭 건수"를 미리볼 수 있게 한다. 참조를 그대로 들고 있어서(복사하지 않음) 그 창에서
    /// 정산파일을 다시 불러오거나 매핑이 바뀌어도 항상 최신 상태를 반영한다.
    /// </summary>
    public void SetSettlementPreviewData(IReadOnlyList<SettlementData> rows)
    {
        _settlementRowsForPreview = rows;
        UpdateConditionPreview();
    }

    private IReadOnlyList<SettlementData>? _settlementRowsForPreview;

    /// <summary>
    /// SalesManagerV2의 "N건 매칭예상" 실시간 미리보기를 가져온 기능. 조건을 추가/수정/삭제할
    /// 때마다 현재 채널에 로드되어 있는 발주서(_sourceOrders, OFS에서 넘겨받은 것과 같은 인스턴스)와
    /// 정산파일(_settlementRowsForPreview, 마감/이익분석창에서 넘겨받은 것)에 그 조건을 즉시 적용해
    /// 몇 건이 매칭되는지 보여준다. 정산파일 쪽은 "미매핑건 중 적용/전체 중 적용"을 나눠 보여줘서,
    /// 이미 매핑된 건까지 새로 걸리면(=전체 중 적용이 미매핑 중 적용보다 큼) 기존 매핑이 이 규칙으로
    /// 덮이거나 충돌할 수 있다는 걸 미리 알 수 있게 한다(사용자 요청: "중복매핑이 될 수 있으니").
    /// 둘 다 로드되어 있지 않으면 미리볼 데이터가 없다고 안내한다.
    /// </summary>
    private void UpdateConditionPreview()
    {
        if (_conditionDetailGrid.DataSource is not BindingList<MappingConditionDetail> details || details.Count == 0)
        {
            _conditionPreviewLabel.Text = "예상 매칭 건수: -";
            return;
        }

        var channelCode = _channelComboBox.SelectedValue as string;
        var validDetails = details.Where(d => !string.IsNullOrWhiteSpace(d.TargetValue)).ToList();
        var parts = new List<string>();

        if (_sourceOrders != null && !string.IsNullOrEmpty(channelCode))
        {
            var candidates = _sourceOrders.Where(o => o.ChannelCode == channelCode).ToList();
            parts.Add(validDetails.Count == 0
                ? $"발주서 전체 {candidates.Count}건(조건 없음)"
                : $"발주서 {candidates.Count(o => ConditionEvaluator.Matches(validDetails, o))}건/전체 {candidates.Count}건");
        }

        if (_settlementRowsForPreview != null && !string.IsNullOrEmpty(channelCode))
        {
            var candidates = _settlementRowsForPreview.Where(d => d.ChannelCode == channelCode).ToList();
            if (validDetails.Count == 0)
            {
                parts.Add($"정산파일 전체 {candidates.Count}건(조건 없음)");
            }
            else
            {
                var matchingAll = candidates.Where(d => ConditionEvaluator.Matches(validDetails, d)).ToList();
                var matchingUnmapped = matchingAll.Count(SettlementRowStatus.IsUnresolved);
                parts.Add($"정산파일 미매핑건 중 적용 {matchingUnmapped}건 / 전체 중 적용 {matchingAll.Count}건(전체 {candidates.Count}건 중)");
            }
        }

        _conditionPreviewLabel.Text = parts.Count > 0
            ? "예상 매칭 건수: " + string.Join("  |  ", parts)
            : "예상 매칭 건수: (발주서 또는 정산파일을 불러와야 미리볼 수 있습니다)";
    }

    /// <summary>
    /// 다른 창(마감/이익분석 등)에서 미매핑 행의 상품명/옵션명을 조건으로 바로 조건부 매핑 규칙을
    /// 만들고 싶을 때 호출하는 진입점. 채널을 선택하고, 상품명/옵션명을 Contains 조건으로 채운
    /// 새 규칙을 만들어 "조건부 매핑(상세)" 탭에서 바로 다듬을 수 있게 한다.
    ///
    /// 마감/이익분석창에서 "조건부 매핑 규칙 추가"로 이 창을 막 열거나 앞으로 가져온 직후(같은 틱)
    /// 호출된다. 채널을 바꿀 때 쓰는 일반 경로(<see cref="LoadRulesForSelectedChannel"/>)는 저장
    /// 확인 모달을 띄울 수 있는데, 창이 막 보이게 된 직후 같은 틱에서 모달을 띄우면 그 모달이
    /// Visible=False로 생성되는 경쟁 상태가 재현된다("조건부매핑창이 열리지만 반응 없음" 신고와
    /// 동일 원인) — 그래서 여기서는 이벤트 핸들러를 잠시 떼고 모달 없는 핵심 로직만 직접 호출한다.
    /// 다른 채널 탭에 저장 안 한 변경사항이 있었다면 그 변경사항은 경고 없이 버려진다(이 창을 다시
    /// 열 때 즉시 새 조건부 규칙을 만드는 게 목적이라, 모달로 막는 것보다 이 트레이드오프가 낫다).
    /// </summary>
    /// <param name="initialConditions">초기 조건 목록. 우클릭한 셀의 필드와 값만 넘기면 그것만 조건으로 추가된다.</param>
    public void StartNewConditionRuleFor(string channelCode, IList<(StdField Field, string Value)> initialConditions)
    {
        if (_channelComboBox.SelectedValue as string != channelCode)
        {
            _channelComboBox.SelectedIndexChanged -= OnChannelComboBoxSelectedIndexChanged;
            _channelComboBox.SelectedValue = channelCode;
            _channelComboBox.SelectedIndexChanged += OnChannelComboBoxSelectedIndexChanged;
            LoadRulesForSelectedChannelCore(channelCode);
        }

        var details = initialConditions
            .Select(c => new MappingConditionDetail { HeaderField = c.Field, Operator = ConditionOperator.Contains, TargetValue = c.Value, Logic = ConditionLogic.And })
            .ToList();

        var summaryKey = string.Join(" ", initialConditions.Select(c => c.Value)).Trim();
        var newRuleId = _mappingRepository.AddConditionRuleWithDetails(channelCode, summaryKey, string.Empty, details);
        LoadConditionRules(channelCode);
        SelectConditionRuleById(newRuleId);

        // OnTabSelecting을 잠시 떼어 탭 전환 시 "변경사항 저장?" 모달이 뜨지 않게 한다.
        // (창이 막 보이게 된 직후 같은 틱에서 모달을 띄우면 Visible=False 경쟁 상태가 재현됨.)
        _ruleTabControl.Selecting -= OnTabSelecting;
        _ruleTabControl.SelectedTab = _conditionDetailTabPage;
        _ruleTabControl.Selecting += OnTabSelecting;
    }

    /// <summary>
    /// 조건부 매핑 규칙이 추가/수정/삭제될 때 알리는 이벤트. 마감/이익분석창이 이걸 구독해서
    /// "정산파일 로드"를 다시 안 해도 미매핑 목록에 즉시 반영되게 한다.
    /// </summary>
    public event Action? MappingRulesChanged;

    private void FilterSkuSearchList()
    {
        var query = _conditionTargetSkuTextBox.Text.Trim();
        _skuSearchListBox.BeginUpdate();
        _skuSearchListBox.Items.Clear();
        if (!string.IsNullOrEmpty(query))
        {
            foreach (var code in _allSkuCodes.Where(s => s.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(20))
                _skuSearchListBox.Items.Add(code);
        }
        _skuSearchListBox.EndUpdate();
    }

    private void OnAddConditionRuleClick(object? sender, EventArgs e)
    {
        var selectedChannel = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(selectedChannel))
        {
            MessageBox.Show("먼저 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var newRuleId = _mappingRepository.AddConditionRuleWithDetails(selectedChannel, "새 조건부 규칙", string.Empty, new List<MappingConditionDetail>());
        LoadConditionRules(selectedChannel);
        SelectConditionRuleById(newRuleId);
    }

    /// <summary>
    /// 이전 시도(이벤트 대신 직접 호출)로도 같은 증상이 계속됐다 — 진짜 원인은 한 단계 더 깊었다.
    /// <see cref="_conditionRuleGrid"/>는 핸들이 생성된 적이 없는 그리드라(화면에 추가 안 함),
    /// `Rows` 컬렉션 자체가 바인딩된 데이터를 전혀 반영하지 못하는 경우가 있어(핸들 없으면
    /// 행이 실체화되지 않음) `foreach (... in _conditionRuleGrid.Rows)`가 한 건도 못 찾고 끝났다.
    /// 그래서 `Rows`가 아니라 `DataSource`로 바인딩해 둔 메모리상의 리스트에서 직접 찾는다(핸들
    /// 여부와 무관하게 항상 동작). `Rows` 기반 CurrentCell 동기화는 되면 좋고 안 돼도 무해하므로
    /// best-effort로만 시도한다.
    /// </summary>
    private void SelectConditionRuleById(long ruleId)
    {
        if (_conditionRuleGrid.DataSource is not BindingList<MappingRule> rules) return;
        var rule = rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule == null) return;

        foreach (DataGridViewRow row in _conditionRuleGrid.Rows)
        {
            if (row.DataBoundItem is MappingRule r && r.Id == ruleId)
            {
                _conditionRuleGrid.CurrentCell = row.Cells[0];
                break;
            }
        }

        SelectConditionRule(rule);
    }

    private void OnDeleteConditionRuleClick(object? sender, EventArgs e)
    {
        if (_selectedConditionRuleId < 0) return;

        var confirm = MessageBox.Show(
            "선택한 조건부 매핑 규칙과 그 상세조건을 모두 삭제합니다. 계속하시겠습니까?",
            "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _mappingRepository.DeleteConditionRule(_selectedConditionRuleId);

        var selectedChannel = _channelComboBox.SelectedValue as string;
        if (!string.IsNullOrEmpty(selectedChannel))
        {
            LoadConditionRules(selectedChannel);
        }
    }

    private void OnSaveConditionSummaryClick(object? sender, EventArgs e)
    {
        if (_selectedConditionRuleId < 0) return;
        var ruleId = _selectedConditionRuleId;

        // 상세조건도 함께 저장 — "상세조건 저장" 버튼을 따로 누르지 않아도 됨.
        if (_conditionDetailGrid.DataSource is BindingList<MappingConditionDetail> details)
            _mappingRepository.ReplaceConditionDetails(ruleId, details.ToList());

        _mappingRepository.UpdateConditionRuleSummary(ruleId, _conditionKeyTextBox.Text, _conditionTargetSkuTextBox.Text);

        _lastSavedConditionRuleId = ruleId;
        _btnUndoLastMapping.Enabled = true;

        // 저장 후 에디터를 초기화해 연속 작업(미매핑리스트에서 다음 항목 클릭)이 바로 가능하게 한다.
        _selectedConditionRuleId = -1;
        SetConditionDetailEditorEnabled(false);
        _conditionSaveFeedbackLabel.Text = $"저장됨 ({DateTime.Now:HH:mm:ss}) — 취소하려면 [직전매핑취소] 클릭, 다음 항목 클릭 또는 '전체 조건부규칙 보기'로 재편집하세요.";
        MappingRulesChanged?.Invoke();
    }

    private void OnUndoLastMappingClick(object? sender, EventArgs e)
    {
        if (_lastSavedConditionRuleId < 0) return;
        _mappingRepository.DeleteConditionRule(_lastSavedConditionRuleId);
        _lastSavedConditionRuleId = -1;
        _btnUndoLastMapping.Enabled = false;
        _conditionSaveFeedbackLabel.Text = $"직전매핑 취소됨 ({DateTime.Now:HH:mm:ss})";

        var channelCode = _channelComboBox.SelectedValue as string;
        if (!string.IsNullOrEmpty(channelCode))
            LoadConditionRules(channelCode);

        MappingRulesChanged?.Invoke();
    }

    private void OnAddConditionDetailClick(object? sender, EventArgs e)
    {
        if (_conditionDetailGrid.DataSource is not BindingList<MappingConditionDetail> details) return;

        // 노션 5.1: 조건을 추가할 때마다 항목/조건/결합방식을 매번 다시 고르기 귀찮다는 피드백 —
        // 마지막 줄의 항목/조건/결합방식은 그대로 복사하고, 비교할 값만 비워서 추가한다(첫 조건이면
        // 기존 기본값을 그대로 쓴다).
        var last = details.LastOrDefault();
        details.Add(new MappingConditionDetail
        {
            RuleId = _selectedConditionRuleId,
            HeaderField = last?.HeaderField ?? StdField.ProductName,
            Operator = last?.Operator ?? ConditionOperator.Contains,
            TargetValue = string.Empty,
            Logic = last?.Logic ?? ConditionLogic.And,
        });
        UpdateConditionPreview();
    }

    private void OnDeleteConditionDetailClick(object? sender, EventArgs e)
    {
        if (_conditionDetailGrid.DataSource is not BindingList<MappingConditionDetail> details) return;
        if (_conditionDetailGrid.CurrentRow?.DataBoundItem is not MappingConditionDetail detail) return;

        details.Remove(detail);
        UpdateConditionPreview();
    }

    private void OnSaveConditionDetailsClick(object? sender, EventArgs e)
    {
        if (_selectedConditionRuleId < 0) return;
        if (_conditionDetailGrid.DataSource is not BindingList<MappingConditionDetail> details) return;

        _mappingRepository.ReplaceConditionDetails(_selectedConditionRuleId, details.ToList());
        _conditionSaveFeedbackLabel.Text = $"상세조건 저장됨 ({DateTime.Now:HH:mm:ss})";
        ReapplyConditionRulesToUnmappedItems();
        MappingRulesChanged?.Invoke();
    }

    private TabPage CreateRuleTabPage(string title, MappingRuleType ruleType)
    {
        var tabPage = new TabPage(title);
        var grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = $"MappingForm.{ruleType}Grid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            Tag = ruleType // Store the rule type in the Tag for later use
        };

        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "매칭 키 (상품명/옵션명 등)", DataPropertyName = "Key", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "TargetSku", HeaderText = "매핑할 SKU", DataPropertyName = "TargetSku", Width = 250 }
        );

        // 데이터 변경 시 'dirty' 상태로 만들기 위한 이벤트 핸들러 연결
        grid.CellValueChanged += (s, e) => { MarkTabAsDirty(tabPage); RefreshConflicts(); };
        grid.RowsAdded += (s, e) => { MarkTabAsDirty(tabPage); RefreshConflicts(); };
        grid.RowsRemoved += (s, e) => { MarkTabAsDirty(tabPage); RefreshConflicts(); };

        // 충돌 규칙이 있는 행을 강조 표시
        grid.RowPrePaint += (s, e) => OnRuleGridRowPrePaint(grid, ruleType, e);

        tabPage.Controls.Add(grid);
        return tabPage;
    }

    private void OnRuleGridRowPrePaint(ExcelLikeDataGridView grid, MappingRuleType ruleType, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count || grid.Rows[e.RowIndex].IsNewRow) return;

        var row = grid.Rows[e.RowIndex];
        if (row.DataBoundItem is not MappingRule rule) return;

        var isConflicting = _conflictingKeysByType.TryGetValue(ruleType, out var keys) && keys.Contains(rule.Key);
        // 다크모드에서 기본 글자색이 흰색으로 바뀌어도 강조 배경에서 글자가 보이도록 검은색으로 고정한다.
        row.DefaultCellStyle.BackColor = isConflicting ? Color.MistyRose : grid.DefaultCellStyle.BackColor;
        row.DefaultCellStyle.ForeColor = isConflicting ? Color.Black : grid.DefaultCellStyle.ForeColor;
    }

    /// <summary>
    /// 현재 채널에 로드된 4종 규칙 전체를 대상으로 충돌을 다시 감지하고,
    /// 규칙 그리드 강조 및 '충돌 감지' 탭 요약을 갱신합니다.
    /// </summary>
    private void RefreshConflicts()
    {
        _conflictingKeysByType.Clear();
        var allConflicts = new List<MappingConflict>();

        foreach (TabPage tabPage in _ruleTabControl.TabPages)
        {
            if (tabPage.Controls.Count == 0 || tabPage.Controls[0] is not ExcelLikeDataGridView grid || grid.Tag is not MappingRuleType ruleType) continue;

            var rules = (grid.DataSource as BindingList<MappingRule>)?.ToList() ?? new List<MappingRule>();
            var conflicts = MappingConflictDetector.Detect(ruleType, rules);
            if (conflicts.Count > 0)
            {
                _conflictingKeysByType[ruleType] = MappingConflictDetector.GetConflictingKeys(conflicts);
                allConflicts.AddRange(conflicts);
            }

            grid.Invalidate();
        }

        _conflictGrid.DataSource = new BindingList<ConflictRow>(allConflicts.Select(c => new ConflictRow(c)).ToList());
    }

    private record ConflictRow(MappingConflict Conflict)
    {
        public string RuleTypeName => Conflict.RuleType switch
        {
            MappingRuleType.Exception => "예외 처리",
            MappingRuleType.Exact => "1:1 매핑",
            MappingRuleType.Temp => "임시 매핑",
            MappingRuleType.Condition => "조건부 매핑",
            _ => Conflict.RuleType.ToString(),
        };
        public string KeyA => Conflict.KeyA;
        public string TargetSkuA => Conflict.TargetSkuA;
        public string KeyB => Conflict.KeyB;
        public string TargetSkuB => Conflict.TargetSkuB;
    }

    private void LoadChannels()
    {
        var channels = _salesChannelRepository.GetAll();
        _channelComboBox.DataSource = channels;
        _channelComboBox.DisplayMember = "ChannelName";
        _channelComboBox.ValueMember = "ChannelCode";
    }

    /// <summary>
    /// 지정된 채널 코드를 콤보박스에서 선택합니다. OFS에서 미매핑건이 발견되었을 때
    /// 해당 채널의 매핑 규칙을 바로 보여주기 위해 사용합니다.
    /// </summary>
    public void SelectChannelByCode(string channelCode)
    {
        _channelComboBox.SelectedValue = channelCode;
    }

    private void OnChannelComboBoxSelectedIndexChanged(object? sender, EventArgs e) => LoadRulesForSelectedChannel();

    private async void LoadRulesForSelectedChannel()
    {
        // Use SelectedValue which corresponds to ValueMember ("ChannelCode")
        var selectedChannel = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(selectedChannel)) return;

        // 채널 변경 전, 저장되지 않은 변경사항이 있는지 비동기적으로 확인
        if (!await PromptToSaveChanges()) return;

        LoadRulesForSelectedChannelCore(selectedChannel);
    }

    /// <summary>
    /// 채널의 규칙 그리드/조건부 매핑/미매핑 목록을 다시 불러오는 실제 로직. 사용자가 채널
    /// 드롭다운을 직접 바꿀 때는 위 <see cref="LoadRulesForSelectedChannel"/>가 저장 확인 모달을
    /// 먼저 띄운 뒤 호출하지만, <see cref="StartNewConditionRuleFor"/>처럼 다른 창에서 막 연
    /// 직후(같은 틱) 호출되는 경로는 이 모달이 "Visible=False로 생성되는" 경쟁 상태를 그대로
    /// 재현하므로(정산파일 로드 멈춤과 동일 원인) 모달 없이 곧바로 이 메서드만 호출한다.
    /// </summary>
    private void LoadRulesForSelectedChannelCore(string selectedChannel)
    {
        var channelConfig = _channelConfigService.Load().FirstOrDefault(c => c.ChannelCode == selectedChannel);
        var channelType = channelConfig?.ChannelType ?? ChannelType.General;
        _channelTypeLabel.Text = $"[{channelType.ToKoreanLabel()}]";
        _channelTypeLabel.ForeColor = channelType.IsMarketplace() ? Color.SteelBlue : Color.DarkGreen;

        // 채널 변경 시, dirty 상태 및 직전매핑취소 상태 초기화
        _dirtyTabs.Clear();
        _lastSavedConditionRuleId = -1;
        _btnUndoLastMapping.Enabled = false;

        foreach (TabPage tabPage in _ruleTabControl.TabPages)
        {
            if (tabPage.Controls[0] is not ExcelLikeDataGridView grid || grid.Tag is not MappingRuleType ruleType) continue;

            var rules = _mappingRepository.GetRules(ruleType, selectedChannel);
            grid.DataSource = new BindingList<MappingRule>(rules);
        }

        LoadConditionRules(selectedChannel);
        _unmappedChannelCode = selectedChannel;
        RefreshUnmappedGrid();
        RefreshConflicts();
    }

    private async void OnSaveClick(object? sender, EventArgs e)
    {
        var selectedChannel = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(selectedChannel))
        {
            MessageBox.Show("저장할 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_dirtyTabs.Count == 0)
        {
            MessageBox.Show("변경된 내용이 없어 저장할 항목이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await SaveDirtyTabsAsync(selectedChannel);
    }

    /// <summary>
    /// 실제 저장 작업(백그라운드 DB 저장 + 상태표시줄 갱신)만 담당한다. 저장 버튼 클릭과
    /// "닫기 전 저장하시겠습니까?" 확인(<see cref="PromptToSaveChanges"/>)이 공유한다 — 예전엔
    /// 후자가 이미 UI 스레드에 있으면서도 `Task.Run(() => Invoke(...))`로 자기 자신을 다시
    /// 불러들이는 우회적인 방식을 썼는데, `OnSaveClick`이 async void라 그 `Invoke`가 실제 저장
    /// 완료를 기다리지 않고 곧바로 반환해버려서, 폼이 닫히는 시점과 경합해
    /// `ObjectDisposedException("MappingForm")`이 나는 버그가 있었다. 이미 UI 스레드에서
    /// 호출되므로 Invoke가 필요 없다 — 그냥 await로 직접 호출한다.
    /// </summary>
    private async Task SaveDirtyTabsAsync(string selectedChannel)
    {
        var dirtyTabsToSave = _dirtyTabs.ToList();
        int savedTabsCount = 0;

        // UI를 대기 상태로 변경
        Cursor = Cursors.WaitCursor;
        Enabled = false;

        try
        {
            // DB 작업을 백그라운드 스레드에서 실행
            await Task.Run(() =>
            {
                foreach (var dirtyTab in dirtyTabsToSave)
                {
                    if (dirtyTab.Controls[0] is not ExcelLikeDataGridView grid || grid.Tag is not MappingRuleType ruleType) continue;

                    var rules = (grid.DataSource as BindingList<MappingRule>)?.ToList() ?? new List<MappingRule>();
                    _mappingRepository.SaveRules(ruleType, selectedChannel, rules);
                    savedTabsCount++;
                }
            });

            _dirtyTabs.Clear();
            // 노션 5.1 후속 점검: 자기 자신을 Enabled=false로 비활성화한 채로(아래 finally에서만
            // 다시 true) 모달을 띄우면, 정산파일 로드/조건부 매핑 저장에서 반복 재현됐던 것과 같은
            // "모달이 안 보이게 생성되는" 경쟁 상태와 같은 위험군이라 비모달 라벨로 대체했다.
            _globalStatusLabel.ForeColor = Color.DarkGreen;
            _globalStatusLabel.Text = $"'{selectedChannel}' 채널의 변경된 {savedTabsCount}개 탭의 규칙이 저장되었습니다. ({DateTime.Now:HH:mm:ss})";
            if (savedTabsCount > 0) MappingRulesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _globalStatusLabel.ForeColor = Color.Red;
            _globalStatusLabel.Text = $"저장 중 오류가 발생했습니다: {ex.Message}";
        }
        finally
        {
            // UI 상태 복원
            Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (files.Length == 1 && Path.GetExtension(files[0]).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var selectedChannel = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(selectedChannel))
        {
            MessageBox.Show("먼저 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var activeTab = _ruleTabControl.SelectedTab;
        if (activeTab?.Tag is not MappingRuleType ruleType)
        {
            MessageBox.Show("규칙을 적용할 유효한 탭이 선택되지 않았습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var files = (string[])e.Data!.GetData(DataFormats.FileDrop)!;
        var filePath = files[0];

        try
        {
            using var package = ExcelFileOpener.OpenWithPasswordPrompt(filePath, this);
            if (package == null) return;

            var worksheet = package.Workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
            {
                MessageBox.Show("엑셀 파일에 워크시트가 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var rulesToImport = new List<MappingRule>();
            for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
            {
                var key = worksheet.Cells[row, 1].Value?.ToString();
                if (string.IsNullOrWhiteSpace(key)) continue;

                var targetSku = worksheet.Cells[row, 2].Value?.ToString() ?? string.Empty;

                rulesToImport.Add(new MappingRule { Key = key, TargetSku = targetSku });
            }

            if (rulesToImport.Count == 0)
            {
                MessageBox.Show("가져올 유효한 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(
                $"'{activeTab.Text}' 탭에 {rulesToImport.Count}개의 규칙을 읽었습니다.\n기존 규칙을 모두 덮어쓰고 데이터베이스에 반영하시겠습니까?",
                "가져오기 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _mappingRepository.SaveRules(ruleType, selectedChannel, rulesToImport);

                // 그리드 데이터를 새로고침하고 dirty 상태로 표시
                if (activeTab.Controls[0] is ExcelLikeDataGridView grid)
                {
                    grid.DataSource = new BindingList<MappingRule>(rulesToImport);
                    MarkTabAsDirty(activeTab);
                    RefreshConflicts();
                }

                // 그리드를 막 다시 그린 직후라 같은 위험 패턴 — 모달 대신 상태표시줄로 안내한다.
                _globalStatusLabel.ForeColor = Color.DarkGreen;
                _globalStatusLabel.Text = $"데이터를 성공적으로 반영했습니다. ({DateTime.Now:HH:mm:ss})";
                MappingRulesChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀 파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void MarkTabAsDirty(TabPage tabPage)
    {
        _dirtyTabs.Add(tabPage);
    }

    private async void OnTabSelecting(object? sender, TabControlCancelEventArgs e)
    {
        // 탭을 변경하기 전에 저장되지 않은 변경사항이 있는지 확인
        if (!await PromptToSaveChanges())
        {
            // 사용자가 '취소'를 선택하면 탭 변경을 막음
            e.Cancel = true;
        }
    }

    /// <summary>
    /// 사용자에게 변경사항을 저장할지 묻고, 그 결과에 따라 후속 조치를 진행할지 여부를 반환합니다.
    /// </summary>
    /// <returns>후속 조치(탭 변경, 채널 변경 등)를 계속 진행하면 true, 중단하면 false를 반환합니다.</returns>
    private async Task<bool> PromptToSaveChanges()
    {
        if (_dirtyTabs.Count == 0)
        {
            return true; // 변경사항이 없으므로 계속 진행
        }

        var result = MessageBox.Show(
            "변경 내용이 있습니다. 저장하시겠습니까?",
            "저장 확인",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        switch (result)
        {
            case DialogResult.Yes:
                var selectedChannel = _channelComboBox.SelectedValue as string;
                if (!string.IsNullOrEmpty(selectedChannel))
                {
                    await SaveDirtyTabsAsync(selectedChannel);
                }
                return _dirtyTabs.Count == 0; // 저장이 성공적으로 완료되었는지(dirty flag가 해제되었는지) 확인
            case DialogResult.No:
                return true; // 변경사항을 무시하고 계속 진행
            case DialogResult.Cancel:
            default:
                return false; // 작업을 취소하고 계속 진행하지 않음
        }
    }

    /// <summary>
    /// WinForms는 async void FormClosing 핸들러가 await에서 멈춘 뒤에도 일단 그대로 폼을
    /// 닫아버린다(나중에 e.Cancel을 true로 바꿔도 이미 늦음 — 동기적으로 반환된 시점에 닫을지
    /// 말지가 이미 결정됨). 그 틈에 PromptToSaveChanges의 백그라운드 저장 작업이 이미 닫혀(disposed)
    /// 버린 이 폼을 다시 건드리려다 ObjectDisposedException("MappingForm")이 나는 버그가 있었다.
    /// 그래서 변경사항이 있으면 일단 동기적으로 닫기를 막아두고(e.Cancel = true), 확인/저장이 다
    /// 끝난 뒤에만 _closeConfirmed를 켜고 Close()를 다시 호출해 실제로 닫는다.
    /// </summary>
    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closeConfirmed) return;

        if (_dirtyTabs.Count == 0)
        {
            _unmappedGrid.SaveLayout();
            return;
        }

        e.Cancel = true;

        if (!await PromptToSaveChanges()) return;

        _unmappedGrid.SaveLayout();
        _closeConfirmed = true;
        Close();
    }
}
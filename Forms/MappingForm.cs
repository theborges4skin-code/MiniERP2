using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
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

    private ComboBox _channelComboBox = new();
    private TabControl _ruleTabControl = new();
    private readonly HashSet<TabPage> _dirtyTabs = new();
    private readonly Dictionary<MappingRuleType, HashSet<string>> _conflictingKeysByType = new();
    private DataGridView _conflictGrid = new();

    // 조건부 매핑(상세) 탭 — 다중 AND/OR 조건 전용 편집기. 기존 "조건부 매핑" 단순 그리드(SaveRules)와
    // 분리되어 즉시 DB에 반영되므로, 단순 그리드 저장이 이 탭의 데이터를 건드리지 않는다.
    private TabPage _conditionDetailTabPage = new();
    private DataGridView _conditionRuleGrid = new();
    private DataGridView _conditionDetailGrid = new();
    private TextBox _conditionKeyTextBox = new();
    private TextBox _conditionTargetSkuTextBox = new();
    private long _selectedConditionRuleId = -1;

    // "미매핑 처리" 탭 — OFS에서 로드한 발주서를 보면서 바로 매핑할 수 있게 하는 화면.
    // 상단(미매핑 목록)/하단(마스터DB 검색+CSKU 입력)으로 분리되어 있다.
    private TabPage _unmappedTabPage = new();
    private DataGridView _unmappedGrid = new();
    private TextBox _masterSearchBox = new();
    private DataGridView _masterCandidateGrid = new();
    private DataGridView _cskuHistoryGrid = new();
    private TextBox _unmappedSupplyPriceTextBox = new();
    private RadioButton _unmappedVatIncludedRadio = new();
    private RadioButton _unmappedVatExcludedRadio = new();
    private TextBox _unmappedInvoiceNameTextBox = new();
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
        _channelComboBox.SelectedIndexChanged += (s, e) => LoadRulesForSelectedChannel();

        var btnSave = new Button { Text = "저장", Size = new Size(100, 30) };
        btnSave.Click += OnSaveClick;

        panel.Controls.Add(channelLabel);
        panel.Controls.Add(_channelComboBox);
        panel.Controls.Add(btnSave);

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
        tabControl.TabPages.Add(CreateRuleTabPage("조건부 매핑", MappingRuleType.Condition));
        _conditionDetailTabPage = CreateConditionDetailTabPage();
        tabControl.TabPages.Add(_conditionDetailTabPage);
        tabControl.TabPages.Add(CreateConflictTabPage());

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

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 220 };

        _unmappedGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _unmappedGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "ProductName", HeaderText = "상품명", DataPropertyName = "ProductName", Width = 220 },
            new DataGridViewTextBoxColumn { Name = "OptionName", HeaderText = "옵션명", DataPropertyName = "OptionName", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "수량", DataPropertyName = "Quantity", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "상태", DataPropertyName = "Status", Width = 100 }
        );
        _unmappedGrid.SelectionChanged += OnUnmappedRowSelectionChanged;
        SetupUnmappedContextMenu();

        var bottomLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));

        var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 3, 5, 0) };
        searchPanel.Controls.Add(new Label { Text = "마스터DB 검색(SKU/상품명):", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
        _masterSearchBox = new TextBox { Width = 300 };
        _masterSearchBox.TextChanged += (s, e) => RunMasterSearch();
        searchPanel.Controls.Add(_masterSearchBox);

        _masterCandidateGrid = new DataGridView
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

        _cskuHistoryGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
        };
        _cskuHistoryGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "이 SKU로 매핑된 상품명+옵션명 조합(1:1 규칙)", DataPropertyName = "Key", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );

        var cskuPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        cskuPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        cskuPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cskuPanel.Controls.Add(new Label { Text = "CSKU 매핑 이력 — 같은 SKU에 매핑된 다른 상품명/옵션명 조합", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        cskuPanel.Controls.Add(_cskuHistoryGrid, 0, 1);

        var infoAndActions = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        var infoPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 0, 5, 0) };
        infoPanel.Controls.Add(new Label { Text = "납품단가(선택):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        _unmappedSupplyPriceTextBox = new TextBox { Width = 90 };
        infoPanel.Controls.Add(_unmappedSupplyPriceTextBox);
        _unmappedVatIncludedRadio = new RadioButton { Text = "VAT포함", AutoSize = true, Checked = true, Padding = new Padding(8, 4, 0, 0) };
        _unmappedVatExcludedRadio = new RadioButton { Text = "VAT별도", AutoSize = true, Padding = new Padding(5, 4, 0, 0) };
        infoPanel.Controls.Add(_unmappedVatIncludedRadio);
        infoPanel.Controls.Add(_unmappedVatExcludedRadio);
        infoPanel.Controls.Add(new Label { Text = "송장표시명(선택):", AutoSize = true, Padding = new Padding(15, 6, 4, 0) });
        _unmappedInvoiceNameTextBox = new TextBox { Width = 220 };
        infoPanel.Controls.Add(_unmappedInvoiceNameTextBox);

        var actionButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 0, 5, 0) };
        var btnApplyExact = new Button { Text = "1:1 매핑 적용", Size = new Size(110, 30) };
        btnApplyExact.Click += (s, e) => ApplyExactMappingToSelectedUnmapped();
        var btnRegisterTemp = new Button { Text = "임시 SKU 등록 후 매핑", Size = new Size(150, 30) };
        btnRegisterTemp.Click += (s, e) => RegisterTempSkuAndMap();
        var btnAddCondition = new Button { Text = "조건부 매핑 규칙 추가", Size = new Size(150, 30) };
        btnAddCondition.Click += (s, e) => AddConditionRuleFromSelectedUnmapped();
        var btnExclude = new Button { Text = "예외 처리(매핑 제외)", Size = new Size(140, 30) };
        btnExclude.Click += (s, e) => ExcludeSelectedUnmapped();
        actionButtonPanel.Controls.Add(btnApplyExact);
        actionButtonPanel.Controls.Add(btnRegisterTemp);
        actionButtonPanel.Controls.Add(btnAddCondition);
        actionButtonPanel.Controls.Add(btnExclude);

        infoAndActions.Controls.Add(infoPanel, 0, 0);
        infoAndActions.Controls.Add(actionButtonPanel, 0, 1);

        bottomLayout.Controls.Add(searchPanel, 0, 0);
        bottomLayout.Controls.Add(_masterCandidateGrid, 0, 1);
        bottomLayout.Controls.Add(cskuPanel, 0, 2);
        bottomLayout.Controls.Add(infoAndActions, 0, 3);

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
        menu.Items.Add("조건부 매핑 규칙 추가", null, (s, e) => AddConditionRuleFromSelectedUnmapped());
        menu.Items.Add("예외 처리(매핑 제외)", null, (s, e) => ExcludeSelectedUnmapped());
        _unmappedGrid.ContextMenuStrip = menu;

        // 실무에서는 오른쪽 클릭으로 바로 메뉴를 여는 경우가 많으므로, 우클릭한 행을 먼저 선택시킨다.
        _unmappedGrid.MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var hit = _unmappedGrid.HitTest(e.X, e.Y);
            if (hit.RowIndex >= 0) _unmappedGrid.CurrentCell = _unmappedGrid.Rows[hit.RowIndex].Cells[0];
        };
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
        _masterSearchBox.Text = string.Empty;
        RunMasterSearch();
        _ruleTabControl.SelectedTab = _unmappedTabPage;
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

    private void OnUnmappedRowSelectionChanged(object? sender, EventArgs e)
    {
        if (_unmappedGrid.CurrentRow?.DataBoundItem is not OfsOrderItem item) return;
        _masterSearchBox.Text = item.ProductName ?? string.Empty;
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

    private void OnMasterCandidateSelectionChanged()
    {
        _unmappedSupplyPriceTextBox.Text = string.Empty;
        _unmappedInvoiceNameTextBox.Text = string.Empty;
        _unmappedVatIncludedRadio.Checked = true;
        _cskuHistoryGrid.DataSource = null;

        if (_masterCandidateGrid.CurrentRow?.DataBoundItem is not ItemModel selected) return;
        if (string.IsNullOrEmpty(_unmappedChannelCode)) return;

        var existing = _channelSkuRepository.GetByChannelAndMsku(_unmappedChannelCode, selected.Sku);
        if (existing != null)
        {
            _unmappedSupplyPriceTextBox.Text = existing.SupplyPrice.ToString();
            _unmappedInvoiceNameTextBox.Text = existing.InvoiceDisplayName ?? string.Empty;
        }

        RefreshCskuHistoryGrid(selected.Sku);
    }

    /// <summary>
    /// 선택된 SKU로 이미 매핑된 1:1 규칙들(즉, 다른 상품명+옵션명 조합이라도 같은 CSKU로 모이는 사례)을
    /// 보여준다. 예: "상품A+옵션B"와 "상품A+옵션C"가 같은 SKU라면 둘 다 이 목록에 각각 나타난다.
    /// </summary>
    private void RefreshCskuHistoryGrid(string sku)
    {
        if (string.IsNullOrEmpty(_unmappedChannelCode))
        {
            _cskuHistoryGrid.DataSource = null;
            return;
        }

        var rows = _mappingRepository.GetRules(MappingRuleType.Exact, _unmappedChannelCode)
            .Where(r => r.TargetSku == sku)
            .ToList();
        _cskuHistoryGrid.DataSource = new BindingList<MappingRule>(rows);
    }

    private void ApplyExactMappingToSelectedUnmapped()
    {
        if (_unmappedGrid.CurrentRow?.DataBoundItem is not OfsOrderItem item)
        {
            MessageBox.Show("매핑할 미매핑 항목을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_masterCandidateGrid.CurrentRow?.DataBoundItem is not ItemModel candidate)
        {
            MessageBox.Show("마스터DB에서 매핑할 SKU를 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplyMappingToItem(item, candidate.Sku, "매핑(1:1)", saveAsExactRule: true);
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

        ApplyMappingToItem(item, tempSku, "매핑(임시)", saveAsExactRule: true);
    }

    private void ApplyMappingToItem(OfsOrderItem item, string targetSku, string status, bool saveAsExactRule)
    {
        if (string.IsNullOrEmpty(_unmappedChannelCode)) return;

        SaveChannelSkuInfoFromUnmappedPanel(targetSku);

        if (saveAsExactRule)
        {
            var key = (item.ProductName ?? string.Empty) + (item.OptionName ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(key))
            {
                _mappingRepository.UpsertExactRule(_unmappedChannelCode, key, targetSku);
            }
        }

        item.MappedSku = targetSku;
        item.Status = status;

        RefreshUnmappedGrid();
        RefreshCskuHistoryGrid(targetSku);
        _onMappingApplied?.Invoke();
    }

    /// <summary>
    /// 납품단가/송장표시명 중 하나라도 입력된 경우 채널-SKU(CSKU) 설정으로 저장한다.
    /// 납품단가는 VAT별도로 선택했으면 1.1을 곱해 VAT포함 기준으로 변환한다.
    /// </summary>
    private void SaveChannelSkuInfoFromUnmappedPanel(string targetSku)
    {
        if (string.IsNullOrEmpty(_unmappedChannelCode)) return;

        var hasPrice = decimal.TryParse(_unmappedSupplyPriceTextBox.Text, out var enteredPrice);
        var invoiceDisplayName = string.IsNullOrWhiteSpace(_unmappedInvoiceNameTextBox.Text) ? null : _unmappedInvoiceNameTextBox.Text.Trim();
        if (!hasPrice && invoiceDisplayName == null) return;

        var existing = _channelSkuRepository.GetByChannelAndMsku(_unmappedChannelCode, targetSku);
        var supplyPrice = hasPrice
            ? (_unmappedVatExcludedRadio.Checked ? Math.Round(enteredPrice * 1.1m, 0) : enteredPrice)
            : existing?.SupplyPrice ?? 0m;

        _channelSkuRepository.Upsert(new ChannelSkuModel
        {
            ChannelCode = _unmappedChannelCode,
            Msku = targetSku,
            SupplyPrice = supplyPrice,
            InvoiceDisplayName = invoiceDisplayName ?? existing?.InvoiceDisplayName,
        });
    }

    /// <summary>
    /// 선택한 미매핑 항목의 상품명/옵션명을 그대로 포함하는 조건(AND)으로 조건부 매핑 규칙을 만들고,
    /// "조건부 매핑(상세)" 탭으로 이동해 바로 다듬을 수 있게 한다.
    /// </summary>
    private void AddConditionRuleFromSelectedUnmapped()
    {
        if (_unmappedGrid.CurrentRow?.DataBoundItem is not OfsOrderItem item)
        {
            MessageBox.Show("조건부 매핑을 추가할 미매핑 항목을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_masterCandidateGrid.CurrentRow?.DataBoundItem is not ItemModel candidate)
        {
            MessageBox.Show("마스터DB에서 매핑할 SKU를 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (string.IsNullOrEmpty(_unmappedChannelCode)) return;

        var details = new List<MappingConditionDetail>();
        if (!string.IsNullOrWhiteSpace(item.ProductName))
        {
            details.Add(new MappingConditionDetail { HeaderField = StdField.ProductName, Operator = ConditionOperator.Contains, TargetValue = item.ProductName, Logic = ConditionLogic.And });
        }
        if (!string.IsNullOrWhiteSpace(item.OptionName))
        {
            details.Add(new MappingConditionDetail { HeaderField = StdField.OptionName, Operator = ConditionOperator.Contains, TargetValue = item.OptionName, Logic = ConditionLogic.And });
        }

        if (details.Count == 0)
        {
            MessageBox.Show("이 항목에는 조건으로 쓸 상품명/옵션명이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var summaryKey = $"{item.ProductName} {item.OptionName}".Trim();
        var newRuleId = _mappingRepository.AddConditionRuleWithDetails(_unmappedChannelCode, summaryKey, candidate.Sku, details);

        MessageBox.Show(
            "조건부 매핑 규칙을 추가했습니다. '조건부 매핑(상세)' 탭에서 조건을 다듬을 수 있습니다(기본값은 상품명/옵션명을 그대로 포함하는 조건입니다).",
            "추가 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);

        LoadConditionRules(_unmappedChannelCode);
        SelectConditionRuleById(newRuleId);
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

        RefreshUnmappedGrid();
        _onMappingApplied?.Invoke();
    }

    private TabPage CreateConflictTabPage()
    {
        var tabPage = new TabPage("충돌 감지");

        _conflictGrid = new DataGridView
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
    /// 조건부 매핑 규칙별 여러 상세조건(HeaderField/Operator/TargetValue/Logic)을 추가/삭제/수정하는 전용 탭.
    /// 왼쪽에서 규칙을 고르면 오른쪽에 그 규칙의 상세조건이 뜨고, 각 영역의 저장 버튼이 즉시 DB에 반영한다
    /// (매핑관리창 상단의 일괄 [저장] 버튼/단순 그리드와는 무관하게 동작).
    /// </summary>
    private TabPage CreateConditionDetailTabPage()
    {
        var tabPage = new TabPage("조건부 매핑(상세)");

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // 좌측: 규칙 목록
        var leftPanel = new Panel { Dock = DockStyle.Fill };

        _conditionRuleGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _conditionRuleGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "키(레거시 매칭용/요약)", DataPropertyName = "Key", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "TargetSku", HeaderText = "대상 SKU", DataPropertyName = "TargetSku", Width = 100 }
        );
        _conditionRuleGrid.SelectionChanged += OnConditionRuleSelectionChanged;

        var leftButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36 };
        var btnAddRule = new Button { Text = "규칙 추가", Size = new Size(90, 28) };
        btnAddRule.Click += OnAddConditionRuleClick;
        var btnDeleteRule = new Button { Text = "규칙 삭제", Size = new Size(90, 28) };
        btnDeleteRule.Click += OnDeleteConditionRuleClick;
        leftButtonPanel.Controls.Add(btnAddRule);
        leftButtonPanel.Controls.Add(btnDeleteRule);

        leftPanel.Controls.Add(_conditionRuleGrid);
        leftPanel.Controls.Add(leftButtonPanel);

        // 우측: 선택한 규칙의 요약 정보 + 상세조건 목록
        var rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var summaryPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        _conditionKeyTextBox = new TextBox { Width = 220 };
        _conditionTargetSkuTextBox = new TextBox { Width = 120 };
        var btnSaveSummary = new Button { Text = "규칙 정보 저장", Size = new Size(110, 28) };
        btnSaveSummary.Click += OnSaveConditionSummaryClick;
        summaryPanel.Controls.Add(new Label { Text = "키(요약):", AutoSize = true, Padding = new Padding(0, 7, 3, 0) });
        summaryPanel.Controls.Add(_conditionKeyTextBox);
        summaryPanel.Controls.Add(new Label { Text = "대상 SKU:", AutoSize = true, Padding = new Padding(10, 7, 3, 0) });
        summaryPanel.Controls.Add(_conditionTargetSkuTextBox);
        summaryPanel.Controls.Add(btnSaveSummary);
        summaryPanel.Controls.Add(new Label
        {
            Text = "※ 여기서 추가한 모든 조건은 AND/OR(Logic 열)로 차례대로 결합되어 평가됩니다.",
            AutoSize = true,
            Padding = new Padding(0, 5, 0, 0),
            ForeColor = Color.DimGray,
        });

        _conditionDetailGrid = new DataGridView
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

        rightPanel.Controls.Add(summaryPanel, 0, 0);
        rightPanel.Controls.Add(_conditionDetailGrid, 0, 1);
        rightPanel.Controls.Add(detailButtonPanel, 0, 2);

        mainLayout.Controls.Add(leftPanel, 0, 0);
        mainLayout.Controls.Add(rightPanel, 1, 0);
        tabPage.Controls.Add(mainLayout);

        SetConditionDetailEditorEnabled(false);

        return tabPage;
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
        }
    }

    private void LoadConditionRules(string channelCode)
    {
        var rules = _mappingRepository.GetRules(MappingRuleType.Condition, channelCode);
        _conditionRuleGrid.DataSource = new BindingList<MappingRule>(rules);
        _selectedConditionRuleId = -1;
        SetConditionDetailEditorEnabled(false);
    }

    private void OnConditionRuleSelectionChanged(object? sender, EventArgs e)
    {
        if (_conditionRuleGrid.CurrentRow?.DataBoundItem is not MappingRule rule)
        {
            _selectedConditionRuleId = -1;
            SetConditionDetailEditorEnabled(false);
            return;
        }

        _selectedConditionRuleId = rule.Id;
        _conditionKeyTextBox.Text = rule.Key;
        _conditionTargetSkuTextBox.Text = rule.TargetSku;

        var details = _mappingRepository.GetConditionDetails(rule.Id);
        _conditionDetailGrid.DataSource = new BindingList<MappingConditionDetail>(details);
        SetConditionDetailEditorEnabled(true);
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

    private void SelectConditionRuleById(long ruleId)
    {
        foreach (DataGridViewRow row in _conditionRuleGrid.Rows)
        {
            if (row.DataBoundItem is MappingRule rule && rule.Id == ruleId)
            {
                _conditionRuleGrid.CurrentCell = row.Cells[0];
                break;
            }
        }
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

        _mappingRepository.UpdateConditionRuleSummary(_selectedConditionRuleId, _conditionKeyTextBox.Text, _conditionTargetSkuTextBox.Text);

        var selectedChannel = _channelComboBox.SelectedValue as string;
        if (!string.IsNullOrEmpty(selectedChannel))
        {
            LoadConditionRules(selectedChannel);
        }
        MessageBox.Show("규칙 정보가 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnAddConditionDetailClick(object? sender, EventArgs e)
    {
        if (_conditionDetailGrid.DataSource is not BindingList<MappingConditionDetail> details) return;

        details.Add(new MappingConditionDetail
        {
            RuleId = _selectedConditionRuleId,
            HeaderField = StdField.ProductName,
            Operator = ConditionOperator.Contains,
            TargetValue = string.Empty,
            Logic = ConditionLogic.And,
        });
    }

    private void OnDeleteConditionDetailClick(object? sender, EventArgs e)
    {
        if (_conditionDetailGrid.DataSource is not BindingList<MappingConditionDetail> details) return;
        if (_conditionDetailGrid.CurrentRow?.DataBoundItem is not MappingConditionDetail detail) return;

        details.Remove(detail);
    }

    private void OnSaveConditionDetailsClick(object? sender, EventArgs e)
    {
        if (_selectedConditionRuleId < 0) return;
        if (_conditionDetailGrid.DataSource is not BindingList<MappingConditionDetail> details) return;

        _mappingRepository.ReplaceConditionDetails(_selectedConditionRuleId, details.ToList());
        MessageBox.Show("상세조건이 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private async void LoadRulesForSelectedChannel()
    {
        // Use SelectedValue which corresponds to ValueMember ("ChannelCode")
        var selectedChannel = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(selectedChannel)) return;

        // 채널 변경 전, 저장되지 않은 변경사항이 있는지 비동기적으로 확인
        if (!await PromptToSaveChanges()) return;

        // 채널 변경 시, dirty 상태 초기화
        _dirtyTabs.Clear();

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
            MessageBox.Show($"'{selectedChannel}' 채널의 변경된 {savedTabsCount}개 탭의 규칙이 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show("데이터를 성공적으로 반영했습니다.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // 그리드 데이터를 새로고침하고 dirty 상태로 표시
                if (activeTab.Controls[0] is ExcelLikeDataGridView grid)
                {
                    grid.DataSource = new BindingList<MappingRule>(rulesToImport);
                    MarkTabAsDirty(activeTab);
                    RefreshConflicts();
                }
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
                await Task.Run(() => Invoke(new Action(() => OnSaveClick(null, EventArgs.Empty))));
                return _dirtyTabs.Count == 0; // 저장이 성공적으로 완료되었는지(dirty flag가 해제되었는지) 확인
            case DialogResult.No:
                return true; // 변경사항을 무시하고 계속 진행
            case DialogResult.Cancel:
            default:
                return false; // 작업을 취소하고 계속 진행하지 않음
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!await PromptToSaveChanges())
        {
            e.Cancel = true;
        }
    }
}
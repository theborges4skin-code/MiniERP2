using System.ComponentModel;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 매핑시스템 통합개편 기획서 §4.5: 기존 <see cref="OrderSkuMappingDialog"/>(1:1 정확매핑+마스터DB
/// 신규등록)와 <see cref="QuickMappingPanel"/>(조건부매핑)의 매핑 등록 로직을 하나의 창으로 통합해,
/// 어디서 열든 동일한 4가지 액션(1:1 정확매핑 등록/조건부매핑 등록/매핑제외/마스터DB 신규등록)을
/// 탭으로 제공한다.
///
/// Phase 3(이 클래스 신규 구현)까지만 진행된 상태 — OFS/SettlementForm/UnmappedQueueForm의 기존
/// 진입점을 이 창 호출로 바꾸고 옛 편집기를 제거하는 것은 Phase 4에서 한다. 지금은 아직 어디서도
/// 이 창을 열지 않는다.
/// </summary>
public class MappingWorkbenchDialog : Form
{
    private readonly ItemRepository _itemRepository = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly MappingRepository _mappingRepository = new();

    private readonly OfsOrderItem _targetItem;
    private readonly string? _channelCode;
    private readonly bool _settlementMode;
    private readonly IReadOnlyDictionary<string, string>? _rawValues;

    private List<ItemModel> _allItems;

    /// <summary>매핑이 실제로 적용된 SKU(CSKU 또는 MSKU). 조건부매핑 탭은 규칙만 저장하고 즉시
    /// 재매핑은 호출 측이 하므로 이 값을 채우지 않을 수 있다 — <see cref="RuleWasSaved"/>를 함께 본다.</summary>
    public string? ResultMappedSku { get; private set; }
    public string? ResultStatus { get; private set; }

    /// <summary>어떤 탭에서든 규칙이 하나라도 저장되면 true — 호출 측이 재매핑을 트리거하는 신호.</summary>
    public bool RuleWasSaved { get; private set; }

    // ─── Tab 1: 1:1 정확매핑 ────────────────────────────────────────────────
    private TextBox _exactSearchBox = new();
    private DataGridView _exactCandidateGrid = new();
    private TextBox _exactCskuCode = new();
    private TextBox _exactSupplyPrice = new();
    private RadioButton _exactVatIncluded = new();
    private RadioButton _exactVatExcluded = new();
    private TextBox _exactInvoiceDisplayName = new();
    private CheckBox _exactSaveAsRule = new();
    private CheckBox _exactUseFourField = new();
    private Label _exactStatusLabel = new();

    // ─── Tab 2: 조건부매핑(QuickMappingPanel 임베드) ───────────────────────
    private QuickMappingPanel _conditionPanel = new();

    // ─── Tab 3: 매핑제외 ────────────────────────────────────────────────────
    private Label _excludeStatusLabel = new();

    // ─── Tab 4: 마스터DB 신규등록 ───────────────────────────────────────────
    private Label _newSkuResultLabel = new();

    public MappingWorkbenchDialog(OfsOrderItem targetItem, string? channelCode, bool settlementMode = false, IReadOnlyDictionary<string, string>? rawValues = null)
    {
        _targetItem = targetItem;
        _channelCode = channelCode;
        _settlementMode = settlementMode;
        _rawValues = rawValues;
        _allItems = _itemRepository.GetAll();
        InitializeComponent();
        RunExactSearch();
    }

    private void InitializeComponent()
    {
        Text = "매핑하기";
        Size = new Size(720, 640);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 500);

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        outer.Controls.Add(BuildInfoBar(), 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildExactMappingTab());
        tabs.TabPages.Add(BuildConditionMappingTab());
        tabs.TabPages.Add(BuildExcludeTab());
        tabs.TabPages.Add(BuildNewMasterSkuTab());
        outer.Controls.Add(tabs, 0, 1);

        Controls.Add(outer);
    }

    private Label BuildInfoBar()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(_channelCode)) parts.Add($"채널: {ResolveChannelName(_channelCode)}");
        if (!string.IsNullOrWhiteSpace(_targetItem.ProductName)) parts.Add($"상품명: {_targetItem.ProductName}");
        if (!string.IsNullOrWhiteSpace(_targetItem.OptionName)) parts.Add($"옵션명: {_targetItem.OptionName}");
        parts.Add($"수량: {_targetItem.Quantity:N0}");
        parts.Add(_targetItem.Revenue.HasValue ? $"매출액: {_targetItem.Revenue.Value:N0}" : "가격정보없음");

        return new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
            BackColor = Color.AliceBlue,
            Text = string.Join("  |  ", parts),
        };
    }

    private string ResolveChannelName(string channelCode) =>
        _salesChannelRepository.GetAll().FirstOrDefault(c => c.ChannelCode == channelCode)?.ChannelName ?? channelCode;

    // ═══════════════════════════════════════════════════════════════════════
    // Tab 1: 1:1 정확매핑 등록 (구 OrderSkuMappingDialog 로직 이식)
    // ═══════════════════════════════════════════════════════════════════════

    private TabPage BuildExactMappingTab()
    {
        var tab = new TabPage("1:1 정확매핑 등록");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

        var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        searchPanel.Controls.Add(new Label { Text = "마스터DB 검색(SKU/상품명):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        _exactSearchBox = new TextBox { Width = 280, Text = _targetItem.ProductName ?? string.Empty };
        _exactSearchBox.TextChanged += (s, e) => RunExactSearch();
        searchPanel.Controls.Add(_exactSearchBox);

        _exactCandidateGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _exactCandidateGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Sku", HeaderText = "SKU", DataPropertyName = "Sku", Width = 150 },
            new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "상품명", DataPropertyName = "ItemName", Width = 230 },
            new DataGridViewTextBoxColumn { Name = "CostPrice", HeaderText = "제조원가(VAT포함)", DataPropertyName = "CostPrice", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _exactCandidateGrid.SelectionChanged += (s, e) => PrefillFromExistingChannelSku();

        var cskuCodePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        cskuCodePanel.Controls.Add(new Label { Text = "CSKU 코드(자동제안, 편집 가능):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        _exactCskuCode = new TextBox { Width = 200 };
        cskuCodePanel.Controls.Add(_exactCskuCode);

        var pricePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        pricePanel.Controls.Add(new Label { Text = "납품단가(선택):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        _exactSupplyPrice = new TextBox { Width = 90 };
        pricePanel.Controls.Add(_exactSupplyPrice);
        _exactVatIncluded = new RadioButton { Text = "VAT포함", AutoSize = true, Checked = true, Padding = new Padding(10, 4, 0, 0) };
        _exactVatExcluded = new RadioButton { Text = "VAT별도", AutoSize = true, Padding = new Padding(5, 4, 0, 0) };
        pricePanel.Controls.Add(_exactVatIncluded);
        pricePanel.Controls.Add(_exactVatExcluded);

        var invoiceNamePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        invoiceNamePanel.Controls.Add(new Label { Text = "송장표시명(선택):", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        _exactInvoiceDisplayName = new TextBox { Width = 320, Text = _targetItem.ProductName ?? string.Empty };
        invoiceNamePanel.Controls.Add(_exactInvoiceDisplayName);

        var rulePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        _exactSaveAsRule = new CheckBox { Text = "다음에도 같은 상품명/옵션명은 자동으로 이 SKU로 매핑(1:1 규칙으로 저장)", AutoSize = true, Checked = true };
        // 매출액 정보가 있을 때만 "이 가격대에서만" 4필드 옵션을 제공한다 — 없는 채널은 애초에
        // 4필드 후보가 될 수 없다(매핑시스템 통합개편 기획서 §4.1).
        _exactUseFourField = new CheckBox
        {
            Text = "수량+매출액까지 포함해서 저장(4필드 — 이 조합의 다른 가격 옵션과 구분됨)",
            AutoSize = true,
            Checked = _targetItem.Revenue.HasValue,
            Enabled = _targetItem.Revenue.HasValue,
        };
        rulePanel.Controls.Add(_exactSaveAsRule);
        rulePanel.Controls.Add(_exactUseFourField);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var btnMap = new Button { Text = "이 SKU로 매핑", Width = 110 };
        btnMap.Click += OnExactMapClick;
        buttonPanel.Controls.Add(btnMap);
        _exactStatusLabel = new Label { AutoSize = true, Padding = new Padding(10, 6, 0, 0), ForeColor = Color.DarkGreen };
        buttonPanel.Controls.Add(_exactStatusLabel);

        layout.Controls.Add(searchPanel, 0, 0);
        layout.Controls.Add(_exactCandidateGrid, 0, 1);
        layout.Controls.Add(cskuCodePanel, 0, 2);
        layout.Controls.Add(pricePanel, 0, 3);
        layout.Controls.Add(invoiceNamePanel, 0, 4);
        layout.Controls.Add(rulePanel, 0, 5);
        layout.Controls.Add(buttonPanel, 0, 6);
        tab.Controls.Add(layout);
        return tab;
    }

    /// <summary>후보 선택 시 "채널명 앞 3글자_마스터SKU" 형태의 CSKU 코드를 기본 제안하고, 이미
    /// 저장된 CSKU 설정(납품가/송장표시명)이 있으면 함께 채운다 (OrderSkuMappingDialog와 동일 로직).</summary>
    private void PrefillFromExistingChannelSku()
    {
        _exactCskuCode.Text = string.Empty;
        _exactSupplyPrice.Text = string.Empty;
        _exactInvoiceDisplayName.Text = _targetItem.ProductName ?? string.Empty;
        _exactVatIncluded.Checked = true;

        if (_exactCandidateGrid.CurrentRow?.DataBoundItem is not ItemModel selected) return;

        var defaultCode = BuildDefaultCskuCode(selected.Sku);
        _exactCskuCode.Text = defaultCode;

        if (string.IsNullOrEmpty(_channelCode)) return;

        var existing = _channelSkuRepository.GetByChannelAndCskuCode(_channelCode, defaultCode);
        if (existing == null) return;

        _exactSupplyPrice.Text = existing.SupplyPrice.ToString();
        if (!string.IsNullOrWhiteSpace(existing.InvoiceDisplayName))
            _exactInvoiceDisplayName.Text = existing.InvoiceDisplayName;
    }

    private string BuildDefaultCskuCode(string masterSku)
    {
        if (string.IsNullOrEmpty(_channelCode)) return masterSku;
        return CskuCodeGenerator.BuildDefault(ResolveChannelName(_channelCode), masterSku);
    }

    private void RunExactSearch()
    {
        var query = _exactSearchBox.Text.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? _allItems
            : _allItems.Where(i => i.Sku.Contains(query, StringComparison.OrdinalIgnoreCase) || i.ItemName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _exactCandidateGrid.DataSource = new BindingList<ItemModel>(matches);
    }

    private void OnExactMapClick(object? sender, EventArgs e)
    {
        if (_exactCandidateGrid.CurrentRow?.DataBoundItem is not ItemModel selected)
        {
            MessageBox.Show("매핑할 SKU를 목록에서 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var cskuCode = ResolveCskuCodeOrShowError();
        if (cskuCode == null) return;

        SaveChannelSkuInfoIfEntered(cskuCode, selected.Sku);
        SaveAsExactRuleIfChecked(cskuCode);

        ResultMappedSku = cskuCode;
        ResultStatus = "수동 매핑";
        DialogResult = DialogResult.OK;
        Close();
    }

    private string? ResolveCskuCodeOrShowError()
    {
        var code = _exactCskuCode.Text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            MessageBox.Show("CSKU 코드를 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        return code;
    }

    private void SaveChannelSkuInfoIfEntered(string cskuCode, string masterSku)
    {
        if (string.IsNullOrEmpty(_channelCode))
        {
            MessageBox.Show("채널 정보가 없어 CSKU 설정(납품단가/송장표시명)을 저장하지 못했습니다. SKU 매핑만 적용됩니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        decimal.TryParse(_exactSupplyPrice.Text, out var enteredPrice);
        var supplyPrice = enteredPrice > 0
            ? (_exactVatExcluded.Checked ? Math.Round(enteredPrice * 1.1m, 0) : enteredPrice)
            : 0m;

        if (!_channelSkuRepository.CreateIfNew(_channelCode, cskuCode, masterSku, supplyPrice, _exactInvoiceDisplayName.Text))
        {
            MessageBox.Show(
                $"기존 CSKU '{cskuCode}'가 이미 존재합니다. 이 상품명/옵션명 조합을 그 CSKU에 매핑하는 조건을 추가합니다.",
                "기존 CSKU 존재", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>
    /// 4필드 체크박스가 켜져 있고 매출액 정보가 있으면 상품명+옵션명+수량+매출액 신규 규칙으로,
    /// 아니면 기존과 동일하게 상품명+옵션명 레거시 규칙으로 저장한다(매핑시스템 통합개편 기획서 §4.1).
    /// </summary>
    private void SaveAsExactRuleIfChecked(string targetSku)
    {
        if (!_exactSaveAsRule.Checked) return;
        if (string.IsNullOrEmpty(_channelCode)) return;

        var key = (_targetItem.ProductName ?? string.Empty) + (_targetItem.OptionName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(key)) return;

        if (_exactUseFourField.Checked && _targetItem.Revenue.HasValue)
        {
            _mappingRepository.UpsertExactRuleWithQuantityPrice(_channelCode, key, targetSku, _targetItem.Quantity, _targetItem.Revenue.Value);
        }
        else
        {
            _mappingRepository.UpsertExactRule(_channelCode, key, targetSku);
        }

        RuleWasSaved = true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tab 2: 조건부매핑 등록 — QuickMappingPanel을 그대로 임베드해서 재사용한다
    // (기획서 §4.5 "패널 자체를 새 창의 임베디드 모드로 재사용" 옵션).
    // ═══════════════════════════════════════════════════════════════════════

    private TabPage BuildConditionMappingTab()
    {
        var tab = new TabPage("조건부매핑 등록");
        _conditionPanel = new QuickMappingPanel { Dock = DockStyle.Fill };
        _conditionPanel.SetChannelCode(_channelCode ?? "", _settlementMode);
        _conditionPanel.LoadItem(_targetItem.ProductName ?? "", _targetItem.OptionName ?? "", _targetItem.Quantity, _targetItem.Revenue, _rawValues);
        _conditionPanel.RuleSaved += () =>
        {
            RuleWasSaved = true;
            ResultStatus = "매핑(조건)";
            DialogResult = DialogResult.OK;
            Close();
        };
        _conditionPanel.Skipped += () =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        tab.Controls.Add(_conditionPanel);
        return tab;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tab 3: 매핑제외 — 배송비/수수료 등 실제 상품이 아닌 행을 매핑 대상에서 제외
    // (MappingForm.ExcludeSelectedUnmapped와 동일한 RuleException 등록 로직).
    // ═══════════════════════════════════════════════════════════════════════

    private TabPage BuildExcludeTab()
    {
        var tab = new TabPage("매핑제외");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var explain = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "배송비/수수료 안내 등 실제 상품이 아닌 행을 앞으로도 매핑 대상에서 자동으로 제외합니다.\n" +
                   $"제외 대상: 상품명 \"{_targetItem.ProductName}\" / 옵션명 \"{_targetItem.OptionName}\"",
            ForeColor = Color.DimGray,
        };

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var btnExclude = new Button { Text = "매핑 대상에서 제외", Width = 140 };
        btnExclude.Click += OnExcludeClick;
        buttonPanel.Controls.Add(btnExclude);
        _excludeStatusLabel = new Label { AutoSize = true, Padding = new Padding(10, 6, 0, 0), ForeColor = Color.DarkGreen };
        buttonPanel.Controls.Add(_excludeStatusLabel);

        layout.Controls.Add(explain, 0, 0);
        layout.Controls.Add(buttonPanel, 0, 1);
        tab.Controls.Add(layout);
        return tab;
    }

    private void OnExcludeClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_channelCode))
        {
            MessageBox.Show("채널 정보가 없어 제외 규칙을 저장할 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var key = (_targetItem.ProductName ?? string.Empty) + (_targetItem.OptionName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("제외할 상품명/옵션명 정보가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"'{_targetItem.ProductName} {_targetItem.OptionName}' 조합을 매핑 대상에서 제외(배송비/수수료 등)하도록 저장하시겠습니까?",
            "예외 처리 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _mappingRepository.UpsertRule(MappingRuleType.Exception, _channelCode, key, SkuMapper.ExcludedTargetSku);

        RuleWasSaved = true;
        ResultMappedSku = null;
        ResultStatus = "제외(배송비 등)";
        _excludeStatusLabel.Text = "저장 완료 — 창을 닫으면 재매핑에 반영됩니다.";
        DialogResult = DialogResult.OK;
        Close();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tab 4: 마스터DB 신규등록 — 임시 SKU 등록 / 새 마스터SKU 정식 등록
    // (구 OrderSkuMappingDialog의 OnRegisterTempSkuClick/OnRegisterNewMasterSkuClick 이식,
    // 등록 후 Tab1의 CSKU코드/납품단가/송장표시명/1:1저장 옵션을 그대로 재사용한다).
    // ═══════════════════════════════════════════════════════════════════════

    private TabPage BuildNewMasterSkuTab()
    {
        var tab = new TabPage("마스터DB 신규등록");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var explain = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "마스터DB(품목 마스터)에 아직 없는 상품을 새로 등록하고 이 주문에 매핑합니다.\n" +
                   "등록 시 \"1:1 정확매핑 등록\" 탭에 입력해 둔 CSKU 코드/납품단가/송장표시명 설정을 그대로 사용합니다(비워두면 자동 제안값 사용).",
            ForeColor = Color.DimGray,
        };

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var btnTemp = new Button { Text = "임시 SKU 등록", Width = 110 };
        var btnNewMaster = new Button { Text = "새 마스터SKU 등록(정식)", Width = 160 };
        btnTemp.Click += OnRegisterTempSkuClick;
        btnNewMaster.Click += OnRegisterNewMasterSkuClick;
        buttonPanel.Controls.Add(btnTemp);
        buttonPanel.Controls.Add(btnNewMaster);

        layout.Controls.Add(explain, 0, 0);
        layout.Controls.Add(buttonPanel, 0, 1);
        _newSkuResultLabel = new Label { Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.DarkGreen };
        layout.Controls.Add(_newSkuResultLabel, 0, 2);
        tab.Controls.Add(layout);
        return tab;
    }

    private void OnRegisterTempSkuClick(object? sender, EventArgs e)
    {
        var existingSkus = _itemRepository.GetAll().Select(i => i.Sku);
        var tempSku = TempSkuGenerator.GenerateNext(existingSkus);

        var result = MessageBox.Show(
            $"임시 SKU '{tempSku}'를 마스터DB에 새로 등록하고 이 주문에 매핑하시겠습니까?\n상품명: {_targetItem.ProductName}\n\n등록 후 마스터SKU 관리창에서 원가 등 정보를 보완해주세요.",
            "임시 SKU 등록", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        _itemRepository.Upsert(new ItemModel
        {
            Sku = tempSku,
            ItemName = string.IsNullOrWhiteSpace(_targetItem.ProductName) ? tempSku : _targetItem.ProductName,
            CostPrice = 0m,
        });
        RefreshItemCache();

        FinishNewMasterSkuRegistration(tempSku, "임시 매핑");
    }

    private void OnRegisterNewMasterSkuClick(object? sender, EventArgs e)
    {
        using var dlg = new NewMasterSkuDialog(_targetItem.ProductName);
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK || dlg.ResultSku == null) return;

        RefreshItemCache();
        FinishNewMasterSkuRegistration(dlg.ResultSku, "신규 마스터SKU 등록");
    }

    private void FinishNewMasterSkuRegistration(string newSku, string status)
    {
        var cskuCode = string.IsNullOrWhiteSpace(_exactCskuCode.Text) ? BuildDefaultCskuCode(newSku) : _exactCskuCode.Text.Trim();

        SaveChannelSkuInfoIfEntered(cskuCode, newSku);
        SaveAsExactRuleIfChecked(cskuCode);

        ResultMappedSku = cskuCode;
        ResultStatus = status;
        _newSkuResultLabel.Text = $"등록 완료 — SKU: {newSku} → CSKU: {cskuCode}";
        DialogResult = DialogResult.OK;
        Close();
    }

    private void RefreshItemCache()
    {
        _allItems = _itemRepository.GetAll();
        RunExactSearch();
    }
}

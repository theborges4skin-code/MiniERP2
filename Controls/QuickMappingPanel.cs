using MiniERP2.Database;
using MiniERP2.Forms;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Controls;

/// <summary>
/// OFS/Settlement 화면에 인라인으로 붙어, 미매핑 행을 클릭하면 즉석에서 조건부 매핑 규칙을
/// 저장할 수 있는 패널. 별도 MappingForm 창 전환 없이 5단계 이내에 매핑을 완료한다.
/// </summary>
public class QuickMappingPanel : Panel
{
    private readonly MappingRepository _mappingRepo = new();
    private readonly ItemRepository _itemRepo = new();
    private readonly ChannelSkuRepository _cskuRepo = new();
    private readonly SalesChannelRepository _channelRepo = new();

    private string _channelCode = "";
    private bool _settlementMode;

    private readonly List<string> _recentMskus = new();
    private const int MaxRecentMskus = 5;

    private Label _infoLabel = new();
    private SplitContainer _mainSplit = new();
    private DataGridView _conditionGrid = new();
    private DataGridViewComboBoxColumn _fieldCol = new();
    private static readonly string[] FixedFieldNames = ["ProductName", "OptionName", "Quantity"];
    private IReadOnlyDictionary<string, string>? _currentRawValues;
    private TextBox _skuSearchBox = new();
    private ListBox _skuResultList = new();
    private ListBox _cskuListBox = new();
    private FlowLayoutPanel _recentPanel = new();
    private Button _saveCskuBtn = new();
    private Button _saveMskuBtn = new();
    private Button _assignMasterSkuBtn = new();
    private Button _skipBtn = new();
    private Label _statusLabel = new();

    // "원가 정보 없음" 행(=매핑 규칙/CSKU는 이미 정상 연결되어 있는데, 그 CSKU의 마스터SKU만 등록이
    // 안 된 경우) 전용 화면. 새 매핑 규칙을 또 만들면 기존 규칙 위에 중복만 쌓이므로, 이 모드에서는
    // 조건/검색 UI 대신 "기존 CSKU의 마스터SKU를 지금 연결하기" 한 가지 액션만 보여준다.
    private Panel _orphanPanel = new();
    private Label _orphanMessageLabel = new();
    private bool _orphanMode;
    private string? _orphanChannelCode;
    private string? _orphanCskuCode;

    private List<ItemModel> _allItems = new();

    /// <summary>규칙이 저장된 뒤 발생 — 호출 측에서 재매핑 + 다음 행 이동 처리.</summary>
    public event Action? RuleSaved;
    /// <summary>건너뛰기 버튼 클릭 — 다음 미매핑 행으로 이동.</summary>
    public event Action? Skipped;

    public QuickMappingPanel()
    {
        BuildUi();
    }

    public void SetChannelCode(string channelCode, bool settlementMode)
    {
        _channelCode = channelCode;
        _settlementMode = settlementMode;
        _allItems = _itemRepo.GetAll();

        _saveCskuBtn.Visible = !settlementMode;
        _saveMskuBtn.Text = settlementMode ? "MSKU로 저장 (정산용)" : "MSKU로 저장 (CSKU 없이)";
    }

    public void LoadItem(string productName, string optionName, int qty, decimal? revenue, IReadOnlyDictionary<string, string>? rawValues = null)
    {
        _orphanMode = false;
        _mainSplit.Visible = true;
        _orphanPanel.Visible = false;
        _assignMasterSkuBtn.Visible = false;
        _saveMskuBtn.Visible = true;
        _saveCskuBtn.Visible = !_settlementMode;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(productName)) parts.Add($"상품명: {productName}");
        if (!string.IsNullOrWhiteSpace(optionName)) parts.Add($"옵션명: {optionName}");
        parts.Add($"수량: {qty:N0}");
        if (revenue.HasValue && revenue.Value > 0) parts.Add($"매출액: {revenue.Value:N0}");
        _infoLabel.Text = string.Join("  |  ", parts);

        _currentRawValues = rawValues;
        RefreshFieldColumnItems();
        BuildDefaultConditions(productName, optionName);

        _skuSearchBox.Text = "";
        _skuResultList.Items.Clear();
        _cskuListBox.Items.Clear();
        _saveCskuBtn.Enabled = false;
        _saveMskuBtn.Enabled = false;
        _statusLabel.Text = "";
    }

    /// <summary>
    /// "원가 정보 없음" 행 전용 진입점. 매핑 규칙과 CSKU는 이미 정상 연결되어 있고, 그 CSKU의
    /// 마스터SKU만 등록되어 있지 않은 경우를 위한 것 — 새 규칙을 만드는 대신 기존 CSKU를 그대로
    /// 고쳐써서 규칙이 중복으로 쌓이지 않게 한다.
    /// </summary>
    public void LoadOrphanCsku(string channelCode, string cskuCode, string productName, string optionName, int qty)
    {
        _orphanMode = true;
        _orphanChannelCode = channelCode;
        _orphanCskuCode = cskuCode;

        _mainSplit.Visible = false;
        _orphanPanel.Visible = true;
        _saveCskuBtn.Visible = false;
        _saveMskuBtn.Visible = false;
        _assignMasterSkuBtn.Visible = true;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(productName)) parts.Add($"상품명: {productName}");
        if (!string.IsNullOrWhiteSpace(optionName)) parts.Add($"옵션명: {optionName}");
        parts.Add($"수량: {qty:N0}");
        _infoLabel.Text = string.Join("  |  ", parts);

        _orphanMessageLabel.Text =
            $"CSKU '{cskuCode}'는 매핑 규칙에 정상 연결되어 있지만, 연결된 마스터SKU가 등록되어 있지 않아 " +
            "원가를 알 수 없습니다. 아래 버튼으로 이 CSKU에 마스터SKU를 연결하세요(새 매핑 규칙을 만들 필요는 없습니다).";
        _statusLabel.Text = "";
    }

    /// <summary>
    /// "필드" 콤보에 고정 항목(상품명/옵션명/수량)에 더해, 지금 선택된 정산파일 행의 원본 열
    /// 이름을 전부 추가한다. 아마존의 sku 열처럼 표준필드로 매핑되지 않은 열도 조건으로 쓸 수
    /// 있도록 하기 위함 — 채널마다 원본 열 구성이 다르므로 행이 바뀔 때마다 다시 채운다.
    /// </summary>
    private void RefreshFieldColumnItems()
    {
        _fieldCol.Items.Clear();
        _fieldCol.Items.AddRange(FixedFieldNames);
        if (_currentRawValues != null)
        {
            var rawKeys = _currentRawValues.Keys
                .Where(k => !string.IsNullOrWhiteSpace(k) && !FixedFieldNames.Contains(k))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _fieldCol.Items.AddRange(rawKeys);
        }
    }

    private void BuildDefaultConditions(string productName, string optionName)
    {
        var dt = new System.Data.DataTable();
        dt.Columns.Add("Field", typeof(string));
        dt.Columns.Add("Operator", typeof(string));
        dt.Columns.Add("Value", typeof(string));
        dt.Columns.Add("Logic", typeof(string));

        if (!string.IsNullOrWhiteSpace(productName))
            dt.Rows.Add("ProductName", "Contains", productName, "And");
        if (!string.IsNullOrWhiteSpace(optionName))
            dt.Rows.Add("OptionName", "Contains", optionName, "And");

        _conditionGrid.DataSource = dt;
    }

    private void SearchMsku()
    {
        var query = _skuSearchBox.Text.Trim();
        _skuResultList.Items.Clear();
        _cskuListBox.Items.Clear();
        _saveCskuBtn.Enabled = false;
        _saveMskuBtn.Enabled = false;

        if (string.IsNullOrEmpty(query)) return;

        var results = _allItems
            .Where(i => i.Sku.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                       (i.ItemName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(20)
            .ToList();

        foreach (var item in results)
            _skuResultList.Items.Add(new MskuItem(item.Sku, item.ItemName ?? ""));

        if (results.Count == 1)
        {
            _skuResultList.SelectedIndex = 0;
            LoadCskuList(results[0].Sku);
        }
    }

    private void OnSkuResultSelected(object? sender, EventArgs e)
    {
        if (_skuResultList.SelectedItem is MskuItem item)
        {
            LoadCskuList(item.Sku);
            _saveMskuBtn.Enabled = true;
        }
    }

    private void LoadCskuList(string msku)
    {
        _cskuListBox.Items.Clear();
        _saveCskuBtn.Enabled = false;

        var cskus = _cskuRepo.GetAllByChannel(_channelCode)
            .Where(c => c.Msku.Equals(msku, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var csku in cskus)
            _cskuListBox.Items.Add(new CskuItem(csku.CskuCode, csku.Msku, csku.SupplyPrice, csku.InvoiceDisplayName));

        if (cskus.Count == 1)
        {
            _cskuListBox.SelectedIndex = 0;
            _saveCskuBtn.Enabled = true;
        }
    }

    private void OnCskuSelected(object? sender, EventArgs e)
    {
        _saveCskuBtn.Enabled = _cskuListBox.SelectedItem is CskuItem;
    }

    private void OnSaveCskuClick(object? sender, EventArgs e)
    {
        if (_cskuListBox.SelectedItem is not CskuItem csku) return;
        SaveRule(targetSku: csku.CskuCode, targetMsku: "");
    }

    private void OnSaveMskuClick(object? sender, EventArgs e)
    {
        if (_skuResultList.SelectedItem is not MskuItem msku) return;
        SaveRule(targetSku: "", targetMsku: msku.Sku);
    }

    private void SaveRule(string targetSku, string targetMsku)
    {
        var conditions = BuildConditionDetails();
        if (conditions.Count == 0)
        {
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = "조건을 1개 이상 입력하세요.";
            return;
        }

        var key = string.Join(" / ", conditions.Select(c => c.TargetValue));
        _mappingRepo.AddConditionRuleWithDetails(_channelCode, key, targetSku, conditions, targetMsku);

        var displayMsku = string.IsNullOrEmpty(targetSku)
            ? targetMsku
            : ((_skuResultList.SelectedItem as MskuItem)?.Sku ?? targetSku);
        AddRecentMsku(displayMsku);

        var label = string.IsNullOrEmpty(targetSku) ? "MSKU" : "CSKU";
        _statusLabel.ForeColor = Color.DarkGreen;
        _statusLabel.Text = $"저장 완료 ({label}: {(string.IsNullOrEmpty(targetSku) ? targetMsku : targetSku)})";
        RuleSaved?.Invoke();
    }

    private List<MappingConditionDetail> BuildConditionDetails()
    {
        var details = new List<MappingConditionDetail>();
        if (_conditionGrid.DataSource is not System.Data.DataTable dt) return details;

        // Commit any active edit
        _conditionGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        _conditionGrid.EndEdit();

        foreach (System.Data.DataRow row in dt.Rows)
        {
            var fieldStr = row["Field"]?.ToString() ?? "";
            var opStr = row["Operator"]?.ToString() ?? "";
            var value = row["Value"]?.ToString() ?? "";
            var logicStr = row["Logic"]?.ToString() ?? "And";

            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!Enum.TryParse<ConditionOperator>(opStr, out var op)) continue;
            if (!Enum.TryParse<ConditionLogic>(logicStr, out var logic)) logic = ConditionLogic.And;

            // 필드 콤보는 고정 항목(ProductName/OptionName/Quantity) 외에 정산파일 원본 열 이름도
            // 담고 있다. 고정 항목이면 표준필드로, 그 외 문자열이면 원본 열 조건(Raw)으로 취급한다.
            MappingConditionDetail detail;
            if (FixedFieldNames.Contains(fieldStr) && Enum.TryParse<StdField>(fieldStr, out var field))
            {
                detail = new MappingConditionDetail { HeaderField = field, Operator = op, TargetValue = value, Logic = logic };
            }
            else if (!string.IsNullOrWhiteSpace(fieldStr))
            {
                detail = new MappingConditionDetail { HeaderField = StdField.Raw, RawFieldName = fieldStr, Operator = op, TargetValue = value, Logic = logic };
            }
            else
            {
                continue;
            }

            details.Add(detail);
        }
        return details;
    }

    private void AddRecentMsku(string msku)
    {
        if (string.IsNullOrEmpty(msku)) return;
        _recentMskus.Remove(msku);
        _recentMskus.Insert(0, msku);
        if (_recentMskus.Count > MaxRecentMskus) _recentMskus.RemoveAt(_recentMskus.Count - 1);
        RebuildRecentButtons();
    }

    private void RebuildRecentButtons()
    {
        _recentPanel.Controls.Clear();
        _recentPanel.Controls.Add(new Label { Text = "최근:", AutoSize = true, Padding = new Padding(0, 4, 4, 0) });
        foreach (var msku in _recentMskus)
        {
            var btn = new Button { Text = msku, AutoSize = true, Margin = new Padding(2), Height = 22 };
            btn.Click += (s, e) => { _skuSearchBox.Text = msku; SearchMsku(); };
            _recentPanel.Controls.Add(btn);
        }
    }

    private void BuildUi()
    {
        Dock = DockStyle.Fill;
        BackColor = SystemColors.Control;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        // Row 0: info
        _infoLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0),
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
            BackColor = Color.AliceBlue,
        };
        outer.Controls.Add(_infoLabel, 0, 0);

        // Row 1: main split
        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };
        var mainSplit = _mainSplit;
        mainSplit.SplitterMoved += (s, e) => { };

        // Left: condition grid
        var leftLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var condToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = false };
        condToolbar.Controls.Add(new Label { Text = "매핑 조건:", AutoSize = true, Padding = new Padding(0, 5, 6, 0) });
        var btnAdd = new Button { Text = "+", Size = new Size(28, 22), Margin = new Padding(2) };
        var btnRem = new Button { Text = "-", Size = new Size(28, 22), Margin = new Padding(2) };
        btnAdd.Click += OnAddConditionRow;
        btnRem.Click += OnRemoveConditionRow;
        condToolbar.Controls.Add(btnAdd);
        condToolbar.Controls.Add(btnRem);

        _conditionGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            ColumnHeadersHeight = 22,
            EditMode = DataGridViewEditMode.EditOnEnter,
        };

        var fieldCol = new DataGridViewComboBoxColumn
        {
            Name = "Field", HeaderText = "필드", DataPropertyName = "Field", Width = 100, FlatStyle = FlatStyle.Flat,
        };
        fieldCol.Items.AddRange(FixedFieldNames);
        _fieldCol = fieldCol;

        var opCol = new DataGridViewComboBoxColumn
        {
            Name = "Operator", HeaderText = "조건", DataPropertyName = "Operator", Width = 80, FlatStyle = FlatStyle.Flat,
        };
        opCol.Items.AddRange("Contains", "NotContains", "Equals");

        var valueCol = new DataGridViewTextBoxColumn
        {
            Name = "Value", HeaderText = "값", DataPropertyName = "Value",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        };

        var logicCol = new DataGridViewComboBoxColumn
        {
            Name = "Logic", HeaderText = "연결", DataPropertyName = "Logic", Width = 56, FlatStyle = FlatStyle.Flat,
        };
        logicCol.Items.AddRange("And", "Or");

        _conditionGrid.Columns.AddRange(fieldCol, opCol, valueCol, logicCol);
        _conditionGrid.EditingControlShowing += (s, e) =>
        {
            if (_conditionGrid.CurrentCell?.OwningColumn is DataGridViewComboBoxColumn && e.Control is ComboBox cb)
                cb.DropDownStyle = ComboBoxStyle.DropDownList;
        };

        leftLayout.Controls.Add(condToolbar, 0, 0);
        leftLayout.Controls.Add(_conditionGrid, 0, 1);
        mainSplit.Panel1.Controls.Add(leftLayout);

        // Right: MSKU/CSKU search
        var rightLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5 };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _recentPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _recentPanel.Controls.Add(new Label { Text = "최근:", AutoSize = true, Padding = new Padding(0, 4, 4, 0) });

        var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        _skuSearchBox = new TextBox { Width = 160, PlaceholderText = "SKU코드 또는 이름 검색 (Enter)" };
        _skuSearchBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { SearchMsku(); e.SuppressKeyPress = true; } };
        var btnSearch = new Button { Text = "검색", Size = new Size(55, 22) };
        btnSearch.Click += (s, e) => SearchMsku();
        searchPanel.Controls.Add(_skuSearchBox);
        searchPanel.Controls.Add(btnSearch);

        _skuResultList = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.One };
        _skuResultList.SelectedIndexChanged += OnSkuResultSelected;

        var cskuLabel = new Label { Text = "CSKU 목록 (이 채널):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(2, 0, 0, 0) };

        _cskuListBox = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.One };
        _cskuListBox.SelectedIndexChanged += OnCskuSelected;

        rightLayout.Controls.Add(_recentPanel, 0, 0);
        rightLayout.Controls.Add(searchPanel, 0, 1);
        rightLayout.Controls.Add(_skuResultList, 0, 2);
        rightLayout.Controls.Add(cskuLabel, 0, 3);
        rightLayout.Controls.Add(_cskuListBox, 0, 4);
        mainSplit.Panel2.Controls.Add(rightLayout);

        outer.Controls.Add(mainSplit, 0, 1);

        // Row 1 (alt): "원가 정보 없음" 전용 안내 패널. mainSplit과 같은 셀에 두고 Visible로 전환한다.
        _orphanMessageLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10),
            ForeColor = Color.DarkRed,
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
        };
        _orphanPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
        _orphanPanel.Controls.Add(_orphanMessageLabel);
        outer.Controls.Add(_orphanPanel, 0, 1);

        // Row 2: footer
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4, 4, 0, 0) };
        _saveCskuBtn = new Button { Text = "저장 — CSKU 매핑", Size = new Size(145, 26), Enabled = false };
        _saveMskuBtn = new Button { Text = "MSKU로 저장 (정산용)", Size = new Size(160, 26), Enabled = false };
        _assignMasterSkuBtn = new Button { Text = "이 CSKU에 마스터SKU 연결하기", AutoSize = true, Visible = false };
        _skipBtn = new Button { Text = "건너뛰기 >", Size = new Size(90, 26) };
        _statusLabel = new Label { AutoSize = true, Padding = new Padding(8, 6, 0, 0) };

        _saveCskuBtn.Click += OnSaveCskuClick;
        _saveMskuBtn.Click += OnSaveMskuClick;
        _assignMasterSkuBtn.Click += OnAssignMasterSkuClick;
        _skipBtn.Click += (s, e) => Skipped?.Invoke();

        footer.Controls.Add(_saveCskuBtn);
        footer.Controls.Add(_saveMskuBtn);
        footer.Controls.Add(_assignMasterSkuBtn);
        footer.Controls.Add(_skipBtn);
        footer.Controls.Add(_statusLabel);
        outer.Controls.Add(footer, 0, 2);

        Controls.Add(outer);
    }

    /// <summary>
    /// "이 CSKU에 마스터SKU 연결하기" — 기존 CSKU(매핑 규칙은 그대로 둠)의 Msku만 실제 등록된
    /// 마스터SKU로 교체한다. ChannelCskuForm의 [마스터SKU 지정/변경(정식 등록)]과 동일한 다이얼로그를
    /// 재사용해, 검증 없이 저장되는 경로를 새로 만들지 않는다.
    /// </summary>
    private void OnAssignMasterSkuClick(object? sender, EventArgs e)
    {
        if (!_orphanMode || _orphanChannelCode == null || _orphanCskuCode == null) return;

        var existing = _cskuRepo.GetByChannelAndCskuCode(_orphanChannelCode, _orphanCskuCode);
        if (existing == null)
        {
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = "CSKU를 찾을 수 없습니다(이미 삭제되었을 수 있습니다).";
            return;
        }

        var channelName = _channelRepo.GetAll().FirstOrDefault(c => c.ChannelCode == _orphanChannelCode)?.ChannelName ?? _orphanChannelCode;
        using var dlg = new AssignMasterSkuDialog(existing.Msku, existing.InvoiceDisplayName, existing.CskuCode, channelName);
        if (FormManager.ShowDialogSafe(dlg, FindForm()) != DialogResult.OK || dlg.SelectedSku == null) return;

        var oldCskuCode = existing.CskuCode;
        var codeChanged = !string.Equals(oldCskuCode, dlg.SelectedCskuCode, StringComparison.Ordinal);
        existing.Msku = dlg.SelectedSku;
        existing.CskuCode = dlg.SelectedCskuCode;
        _cskuRepo.RenameCsku(_orphanChannelCode, oldCskuCode, existing);
        if (codeChanged) _mappingRepo.RetargetRules(_orphanChannelCode, oldCskuCode, existing.CskuCode);

        _statusLabel.ForeColor = Color.DarkGreen;
        _statusLabel.Text = codeChanged
            ? $"'{oldCskuCode}' → '{existing.CskuCode}'(으)로 정식 등록하고 마스터SKU '{dlg.SelectedSku}'를 연결했습니다."
            : $"'{oldCskuCode}'에 마스터SKU '{dlg.SelectedSku}'를 연결했습니다.";
        RuleSaved?.Invoke();
    }

    private void OnAddConditionRow(object? sender, EventArgs e)
    {
        if (_conditionGrid.DataSource is not System.Data.DataTable dt) return;
        if (dt.Rows.Count > 0)
        {
            var last = dt.Rows[dt.Rows.Count - 1];
            dt.Rows.Add(last["Field"], last["Operator"], "", last["Logic"]);
        }
        else
        {
            dt.Rows.Add("ProductName", "Contains", "", "And");
        }
    }

    private void OnRemoveConditionRow(object? sender, EventArgs e)
    {
        if (_conditionGrid.DataSource is not System.Data.DataTable dt) return;
        if (_conditionGrid.CurrentRow == null || dt.Rows.Count <= 1) return;
        var idx = _conditionGrid.CurrentRow.Index;
        if (idx >= 0 && idx < dt.Rows.Count)
            dt.Rows.RemoveAt(idx);
    }

    private record MskuItem(string Sku, string Name)
    {
        public override string ToString() => $"{Sku}  {Name}";
    }

    private record CskuItem(string CskuCode, string Msku, decimal SupplyPrice, string? InvoiceDisplayName)
    {
        public override string ToString() => $"{CskuCode}  {SupplyPrice:N0}원  {InvoiceDisplayName ?? ""}";
    }
}

using MiniERP2.Database;
using MiniERP2.Models;

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

    private string _channelCode = "";
    private bool _settlementMode;

    private readonly List<string> _recentMskus = new();
    private const int MaxRecentMskus = 5;

    private Label _infoLabel = new();
    private DataGridView _conditionGrid = new();
    private TextBox _skuSearchBox = new();
    private ListBox _skuResultList = new();
    private ListBox _cskuListBox = new();
    private FlowLayoutPanel _recentPanel = new();
    private Button _saveCskuBtn = new();
    private Button _saveMskuBtn = new();
    private Button _skipBtn = new();
    private Label _statusLabel = new();

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

    public void LoadItem(string productName, string optionName, int qty, decimal? revenue)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(productName)) parts.Add($"상품명: {productName}");
        if (!string.IsNullOrWhiteSpace(optionName)) parts.Add($"옵션명: {optionName}");
        parts.Add($"수량: {qty:N0}");
        if (revenue.HasValue && revenue.Value > 0) parts.Add($"매출액: {revenue.Value:N0}");
        _infoLabel.Text = string.Join("  |  ", parts);

        BuildDefaultConditions(productName, optionName);

        _skuSearchBox.Text = "";
        _skuResultList.Items.Clear();
        _cskuListBox.Items.Clear();
        _saveCskuBtn.Enabled = false;
        _saveMskuBtn.Enabled = false;
        _statusLabel.Text = "";
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
            if (!Enum.TryParse<StdField>(fieldStr, out var field)) continue;
            if (!Enum.TryParse<ConditionOperator>(opStr, out var op)) continue;
            if (!Enum.TryParse<ConditionLogic>(logicStr, out var logic)) logic = ConditionLogic.And;

            details.Add(new MappingConditionDetail
            {
                HeaderField = field,
                Operator = op,
                TargetValue = value,
                Logic = logic,
            });
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
        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
        };
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
        fieldCol.Items.AddRange("ProductName", "OptionName", "Quantity");

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

        // Set splitter after controls are added
        mainSplit.Panel1MinSize = 250;
        mainSplit.Panel2MinSize = 200;

        // Row 2: footer
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4, 4, 0, 0) };
        _saveCskuBtn = new Button { Text = "저장 — CSKU 매핑", Size = new Size(145, 26), Enabled = false };
        _saveMskuBtn = new Button { Text = "MSKU로 저장 (정산용)", Size = new Size(160, 26), Enabled = false };
        _skipBtn = new Button { Text = "건너뛰기 >", Size = new Size(90, 26) };
        _statusLabel = new Label { AutoSize = true, Padding = new Padding(8, 6, 0, 0) };

        _saveCskuBtn.Click += OnSaveCskuClick;
        _saveMskuBtn.Click += OnSaveMskuClick;
        _skipBtn.Click += (s, e) => Skipped?.Invoke();

        footer.Controls.Add(_saveCskuBtn);
        footer.Controls.Add(_saveMskuBtn);
        footer.Controls.Add(_skipBtn);
        footer.Controls.Add(_statusLabel);
        outer.Controls.Add(footer, 0, 2);

        Controls.Add(outer);
    }

    private void OnAddConditionRow(object? sender, EventArgs e)
    {
        if (_conditionGrid.DataSource is System.Data.DataTable dt)
            dt.Rows.Add("ProductName", "Contains", "", "And");
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

using System.ComponentModel;
using System.Text.Json;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 기획서 5.4절 '채널 설정 창'
/// </summary>
public class ChannelConfigForm : Form
{
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly CourierRepository _courierRepository = new();
    private readonly SalesChannelLegacyMigrationService _legacyMigrationService = new();

    private List<SalesChannel> _channels = new();
    private List<ChannelConfig> _channelConfigs = new();
    private ChannelConfig? _currentConfig;

    private PropertyGrid _propertyGrid = new();
    private TreeView _channelTreeView = new();
    private DataGridView _orderMappingGrid = new();
    private DataGridView _settlementMappingGrid = new();
    private DataGridView _courierOverrideGrid = new();

    private static readonly StdField[] OrderMappingFields =
    [
        StdField.ProductNo, StdField.ProductName, StdField.OptionName, StdField.Quantity,
        StdField.Recipient, StdField.Phone, StdField.Address, StdField.DeliveryMessage, StdField.OrderDate,
    ];

    private static readonly StdField[] SettlementMappingFields =
    [
        StdField.ProductName, StdField.OptionName, StdField.Quantity,
        StdField.SettlementAmount, StdField.ShippingFee, StdField.HandlingFee,
    ];

    public ChannelConfigForm()
    {
        InitializeComponent();
        LoadData();
    }

    private void InitializeComponent()
    {
        Text = "채널 설정";
        Size = new Size(1024, 768);

        // Main Layout
        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Left Panel (Channel List)
        var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _channelTreeView = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            ItemHeight = 22,
        };
        _channelTreeView.AfterSelect += OnChannelSelected;
        SetupTreeViewContextMenu();

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(5) };
        var btnAdd = new Button { Text = "추가", Width = 70 };
        var btnDelete = new Button { Text = "삭제", Width = 70 };
        var btnSave = new Button { Text = "저장", Width = 70 };

        btnAdd.Click += OnAddClick;
        btnDelete.Click += OnDeleteClick;
        btnSave.Click += OnSaveClick;

        buttonPanel.Controls.Add(btnAdd);
        buttonPanel.Controls.Add(btnDelete);
        buttonPanel.Controls.Add(btnSave);

        var courierPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(5) };
        var btnCourier = new Button { Text = "택배사 양식 관리", Width = 150 };
        btnCourier.Click += (s, e) => FormManager.Show<CourierConfigForm>();
        courierPanel.Controls.Add(btnCourier);

        var legacyImportPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(5) };
        var btnLegacyImport = new Button { Text = "SalesManagerV2 채널 가져오기", Width = 180 };
        btnLegacyImport.Click += OnLegacyChannelImportClick;
        legacyImportPanel.Controls.Add(btnLegacyImport);

        leftPanel.Controls.Add(_channelTreeView, 0, 0);
        leftPanel.Controls.Add(buttonPanel, 0, 1);
        leftPanel.Controls.Add(courierPanel, 0, 2);
        leftPanel.Controls.Add(legacyImportPanel, 0, 3);

        // Right Panel (Tabs: 기본 정보 / 발주서 매핑 / 정산서 매핑)
        var rightTabControl = new TabControl { Dock = DockStyle.Fill };
        rightTabControl.TabPages.Add(CreateBasicInfoTab());
        rightTabControl.TabPages.Add(CreateFieldMappingTab("발주서 매핑", _orderMappingGrid));
        rightTabControl.TabPages.Add(CreateFieldMappingTab("정산서 매핑", _settlementMappingGrid));
        rightTabControl.TabPages.Add(CreateCourierOverrideTab());

        mainLayout.Controls.Add(leftPanel, 0, 0);
        mainLayout.Controls.Add(rightTabControl, 1, 0);
        Controls.Add(mainLayout);
    }

    private TabPage CreateBasicInfoTab()
    {
        var tabPage = new TabPage("기본 정보");
        _propertyGrid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            HelpVisible = true,
            ToolbarVisible = true,
            PropertySort = PropertySort.Categorized,
        };
        _propertyGrid.PropertyValueChanged += OnConfigPropertyValueChanged;
        tabPage.Controls.Add(_propertyGrid);
        return tabPage;
    }

    /// <summary>
    /// PropertyGrid에서 채널 이름을 수정하면, 좌측 트리가 보여주는 SalesChannel.ChannelName도
    /// 함께 갱신하고 DB에 반영한다(둘은 서로 다른 저장소라 자동으로 동기화되지 않음).
    /// </summary>
    private void OnConfigPropertyValueChanged(object? sender, PropertyValueChangedEventArgs e)
    {
        if (_currentConfig == null) return;
        if (e.ChangedItem.PropertyDescriptor?.Name != nameof(ChannelConfig.ChannelName)) return;

        var channel = _channels.FirstOrDefault(c => c.ChannelCode == _currentConfig.ChannelCode);
        if (channel == null) return;

        channel.ChannelName = _currentConfig.ChannelName;
        _salesChannelRepository.Upsert(channel);

        var selectedCode = _currentConfig.ChannelCode;
        PopulateTreeView();
        SelectChannelByCode(selectedCode);
    }

    private TabPage CreateFieldMappingTab(string title, DataGridView grid)
    {
        var tabPage = new TabPage(title);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnHelp = new Button { Text = "도움말", Size = new Size(80, 25) };
        btnHelp.Click += (s, e) => { using var dialog = new FieldMappingHelpDialog(); dialog.ShowDialog(this); };
        var btnLoadSample = new Button { Text = "샘플 파일 불러오기", AutoSize = true };
        toolbar.Controls.Add(btnHelp);
        toolbar.Controls.Add(btnLoadSample);

        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Label", HeaderText = "표준 필드", DataPropertyName = "Label", Width = 130, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "SheetName", HeaderText = "시트 이름", DataPropertyName = "SheetName", Width = 110 },
            new DataGridViewTextBoxColumn { Name = "HeaderRow", HeaderText = "헤더 행", DataPropertyName = "HeaderRow", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "Column", HeaderText = "열(헤더 텍스트)", DataPropertyName = "Column", Width = 140 },
            new DataGridViewTextBoxColumn { Name = "FixedValue", HeaderText = "고정값(선택)", DataPropertyName = "FixedValue", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        grid.CellValueChanged += (s, e) => OnFieldMappingGridCellChanged(grid, e);

        // 우측에 샘플 엑셀의 시트/헤더 행을 직접 보면서 "열"에 입력할 헤더 텍스트를 확인할 수 있는 미리보기 패널
        var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 620 };
        grid.Dock = DockStyle.Fill;
        splitContainer.Panel1.Controls.Add(grid);
        splitContainer.Panel2.Controls.Add(CreateSamplePreviewPanel(grid, btnLoadSample));

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(splitContainer, 0, 1);
        tabPage.Controls.Add(layout);
        return tabPage;
    }

    /// <summary>
    /// 샘플 엑셀 파일을 불러와 시트/헤더 행을 선택하면 그 행의 헤더 텍스트 목록을 보여주는 미리보기 패널입니다.
    /// 목록 항목을 더블클릭하면 그리드에서 현재 선택된 행의 "열" 칸에 바로 입력됩니다.
    /// </summary>
    private Control CreateSamplePreviewPanel(DataGridView grid, Button btnLoadSample)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleLabel = new Label { Text = "엑셀 헤더 미리보기 (더블클릭하면 선택한 행의 '열'에 적용)", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };

        var sheetPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        sheetPanel.Controls.Add(new Label { Text = "시트:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
        var sheetCombo = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        sheetPanel.Controls.Add(sheetCombo);

        var headerRowPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        headerRowPanel.Controls.Add(new Label { Text = "헤더 행:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
        var headerRowInput = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 1, Width = 60 };
        headerRowPanel.Controls.Add(headerRowInput);

        var previewList = new ListBox { Dock = DockStyle.Fill };

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(sheetPanel, 0, 1);
        layout.Controls.Add(headerRowPanel, 0, 2);
        layout.Controls.Add(previewList, 0, 3);

        ExcelPackage? samplePackage = null;

        void RefreshPreviewList()
        {
            previewList.Items.Clear();
            if (samplePackage == null || sheetCombo.SelectedItem is not string sheetName) return;

            var sheet = samplePackage.Workbook.Worksheets[sheetName];
            var headerRow = (int)headerRowInput.Value;
            if (sheet?.Dimension == null || headerRow > sheet.Dimension.End.Row) return;

            for (int col = 1; col <= sheet.Dimension.End.Column; col++)
            {
                var header = sheet.Cells[headerRow, col].Value?.ToString();
                if (!string.IsNullOrWhiteSpace(header))
                {
                    previewList.Items.Add(header);
                }
            }
        }

        sheetCombo.SelectedIndexChanged += (s, e) => RefreshPreviewList();
        headerRowInput.ValueChanged += (s, e) => RefreshPreviewList();

        btnLoadSample.Click += (s, e) =>
        {
            using var ofd = new OpenFileDialog { Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*", Title = "샘플 파일을 선택하세요" };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var opened = ExcelFileOpener.OpenWithPasswordPrompt(ofd.FileName, this);
                if (opened == null) return;

                samplePackage?.Dispose();
                samplePackage = opened;

                sheetCombo.Items.Clear();
                sheetCombo.Items.AddRange(samplePackage.Workbook.Worksheets.Select(w => w.Name).Cast<object>().ToArray());
                if (sheetCombo.Items.Count > 0) sheetCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        Disposed += (s, e) => samplePackage?.Dispose();

        // 미리보기 항목을 더블클릭하면 그리드에서 현재 선택된 행의 "열" 칸에 바로 채워준다.
        // 미리보기에서 고른 헤더 행 번호도 함께 "헤더 행" 칸에 반영해, 미리보기에서 2/3/4행으로
        // 바꿔 헤더를 찾아도 매핑 설정의 헤더 행이 기본값 1로 남아있는 문제를 막는다.
        previewList.DoubleClick += (s, e) =>
        {
            if (previewList.SelectedItem is not string header) return;

            var rowIndex = grid.CurrentCell?.RowIndex ?? -1;
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;

            if (grid.IsCurrentCellInEditMode) grid.EndEdit();
            grid.Rows[rowIndex].Cells["Column"].Value = header;
            grid.Rows[rowIndex].Cells["HeaderRow"].Value = (int)headerRowInput.Value;
        };

        return layout;
    }

    private void OnFieldMappingGridCellChanged(DataGridView grid, DataGridViewCellEventArgs e)
    {
        if (_currentConfig == null || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
        if (grid.Rows[e.RowIndex].DataBoundItem is not FieldMappingRow row) return;

        var dict = grid == _orderMappingGrid ? _currentConfig.OrderFieldMappings : _currentConfig.SettlementFieldMappings;
        var inUse = !string.IsNullOrWhiteSpace(row.Column) || !string.IsNullOrWhiteSpace(row.FixedValue);

        if (!inUse)
        {
            dict.Remove(row.StdField);
        }
        else
        {
            dict[row.StdField] = new FieldMapping
            {
                SheetName = row.SheetName,
                HeaderRow = row.HeaderRow <= 0 ? 1 : row.HeaderRow,
                Column = row.Column,
                FixedValue = row.FixedValue,
            };
        }
    }

    private void LoadFieldMappingGrids(ChannelConfig config)
    {
        _currentConfig = config;
        _orderMappingGrid.DataSource = BuildFieldMappingRows(OrderMappingFields, config.OrderFieldMappings);
        _settlementMappingGrid.DataSource = BuildFieldMappingRows(SettlementMappingFields, config.SettlementFieldMappings);
        LoadCourierOverrideGrid(config);
    }

    private void ClearFieldMappingGrids()
    {
        _currentConfig = null;
        _orderMappingGrid.DataSource = null;
        _settlementMappingGrid.DataSource = null;
        _courierOverrideGrid.DataSource = null;
    }

    private TabPage CreateCourierOverrideTab()
    {
        var tabPage = new TabPage("택배사 출력 고정값");

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        toolbar.Controls.Add(new Label
        {
            Text = "같은 택배사를 쓰더라도 이 채널에서는 특정 헤더에 항상 아래 고정값을 출력합니다(예: 도착지 코드).",
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0),
        });

        _courierOverrideGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
        };

        var courierColumn = new DataGridViewComboBoxColumn { Name = "CourierName", HeaderText = "택배사", DataPropertyName = "CourierName", Width = 180, FlatStyle = FlatStyle.Flat };
        var headerColumn = new DataGridViewComboBoxColumn { Name = "Header", HeaderText = "헤더", DataPropertyName = "Header", Width = 180, FlatStyle = FlatStyle.Flat };
        var fixedValueColumn = new DataGridViewTextBoxColumn { Name = "FixedValue", HeaderText = "고정값", DataPropertyName = "FixedValue", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };

        courierColumn.Items.AddRange(_courierRepository.GetAll().Select(c => c.CourierName).Cast<object>().ToArray());
        headerColumn.Items.AddRange(GetAllKnownCourierHeaders().Cast<object>().ToArray());

        _courierOverrideGrid.Columns.Add(courierColumn);
        _courierOverrideGrid.Columns.Add(headerColumn);
        _courierOverrideGrid.Columns.Add(fixedValueColumn);

        // 드롭다운 후보 외에 직접 입력도 허용한다.
        _courierOverrideGrid.EditingControlShowing += (s, e) =>
        {
            if (_courierOverrideGrid.CurrentCell?.OwningColumn is DataGridViewComboBoxColumn && e.Control is ComboBox comboBox)
            {
                comboBox.DropDownStyle = ComboBoxStyle.DropDown;
            }
        };
        _courierOverrideGrid.CellValueChanged += (s, e) => SyncCourierOverrides();
        _courierOverrideGrid.RowsAdded += (s, e) => SyncCourierOverrides();
        _courierOverrideGrid.RowsRemoved += (s, e) => SyncCourierOverrides();

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_courierOverrideGrid, 0, 1);
        tabPage.Controls.Add(layout);
        return tabPage;
    }

    private List<string> GetAllKnownCourierHeaders()
    {
        var headers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var courier in _courierRepository.GetAll())
        {
            try
            {
                var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(courier.HeaderMappingJson);
                if (mapping != null)
                {
                    foreach (var header in mapping.Keys) headers.Add(header);
                }
            }
            catch (JsonException)
            {
                // 손상된 데이터는 건너뜀
            }
        }
        return headers.ToList();
    }

    private void LoadCourierOverrideGrid(ChannelConfig config)
    {
        var rows = config.CourierHeaderOverrides
            .Select(o => new CourierOverrideRow { CourierName = o.CourierName, Header = o.Header, FixedValue = o.FixedValue })
            .ToList();

        // DataGridViewComboBoxColumn은 Items에 없는 값을 표시하면 예외를 던지므로,
        // 저장된 값을 먼저 Items에 채워둔다(CourierConfigForm과 동일한 안전장치).
        EnsureComboItemsInclude(_courierOverrideGrid, "CourierName", rows.Select(r => r.CourierName));
        EnsureComboItemsInclude(_courierOverrideGrid, "Header", rows.Select(r => r.Header));

        _courierOverrideGrid.DataSource = new BindingList<CourierOverrideRow>(rows);
    }

    private void SyncCourierOverrides()
    {
        if (_currentConfig == null) return;

        _currentConfig.CourierHeaderOverrides = (_courierOverrideGrid.DataSource as BindingList<CourierOverrideRow>)?
            .Where(r => !string.IsNullOrWhiteSpace(r.CourierName) && !string.IsNullOrWhiteSpace(r.Header))
            .Select(r => new CourierHeaderOverride { CourierName = r.CourierName, Header = r.Header, FixedValue = r.FixedValue ?? string.Empty })
            .ToList() ?? [];
    }

    private static void EnsureComboItemsInclude(DataGridView grid, string columnName, IEnumerable<string> values)
    {
        if (grid.Columns[columnName] is not DataGridViewComboBoxColumn column) return;

        var existing = new HashSet<string>(column.Items.Cast<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value) || !existing.Add(value)) continue;
            column.Items.Add(value);
        }
    }

    private class CourierOverrideRow
    {
        public string CourierName { get; set; } = string.Empty;
        public string Header { get; set; } = string.Empty;
        public string FixedValue { get; set; } = string.Empty;
    }

    private static BindingList<FieldMappingRow> BuildFieldMappingRows(StdField[] fields, Dictionary<StdField, FieldMapping> dict)
    {
        var rows = new List<FieldMappingRow>();
        foreach (var field in fields)
        {
            dict.TryGetValue(field, out var mapping);
            rows.Add(new FieldMappingRow
            {
                StdField = field,
                Label = GetStdFieldLabel(field),
                SheetName = mapping?.SheetName,
                HeaderRow = mapping?.HeaderRow ?? 1,
                Column = mapping?.Column,
                FixedValue = mapping?.FixedValue,
            });
        }
        return new BindingList<FieldMappingRow>(rows);
    }

    private static string GetStdFieldLabel(StdField field) => field switch
    {
        StdField.ProductName => "상품명",
        StdField.OptionName => "옵션명",
        StdField.ProductNo => "주문번호",
        StdField.Quantity => "수량",
        StdField.SettlementAmount => "정산액",
        StdField.ShippingFee => "배송비",
        StdField.HandlingFee => "입출고비",
        StdField.Recipient => "수취인",
        StdField.Phone => "연락처",
        StdField.Address => "주소",
        StdField.DeliveryMessage => "배송메세지",
        StdField.OrderDate => "발주일(누적발주서용)",
        _ => field.ToString(),
    };

    private class FieldMappingRow
    {
        public StdField StdField { get; set; }
        public string Label { get; set; } = string.Empty;
        public string? SheetName { get; set; }
        public int HeaderRow { get; set; } = 1;
        public string? Column { get; set; }
        public string? FixedValue { get; set; }
    }

    private void LoadData()
    {
        _channels = _salesChannelRepository.GetAll();
        _channelConfigs = _channelConfigService.Load();
        PopulateTreeView();
    }

    private void PopulateTreeView()
    {
        _channelTreeView.Nodes.Clear();

        var favoritesNode = new TreeNode("⭐ 즐겨찾기") { Tag = "GROUP_FAVORITES" };
        var groups = _channels.GroupBy(c => c.GroupName ?? "미분류").ToDictionary(g => g.Key, g => g.ToList());

        // 즐겨찾기 채널 추가
        foreach (var channel in _channels.Where(c => c.IsFavorite).OrderBy(c => c.DisplayOrder).ThenBy(c => c.ChannelName))
        {
            favoritesNode.Nodes.Add(new TreeNode(channel.ChannelName) { Tag = channel });
        }
        if (favoritesNode.Nodes.Count > 0)
        {
            _channelTreeView.Nodes.Add(favoritesNode);
        }

        // 그룹별 채널 추가
        foreach (var group in groups.OrderBy(g => g.Key))
        {
            var groupNode = new TreeNode(group.Key) { Tag = $"GROUP_{group.Key}" };
            foreach (var channel in group.Value.OrderBy(c => c.DisplayOrder).ThenBy(c => c.ChannelName))
            {
                groupNode.Nodes.Add(new TreeNode(channel.ChannelName) { Tag = channel });
            }
            _channelTreeView.Nodes.Add(groupNode);
        }

        _channelTreeView.ExpandAll();
    }

    private void SetupTreeViewContextMenu()
    {
        var contextMenu = new ContextMenuStrip();
        var favoriteItem = new ToolStripMenuItem("즐겨찾기에 추가/제거", null, OnFavoriteClick);
        var renameGroupItem = new ToolStripMenuItem("그룹 이름 변경", null, OnRenameGroupClick);
        var deleteGroupItem = new ToolStripMenuItem("그룹 삭제", null, OnDeleteGroupClick);

        contextMenu.Items.AddRange(new ToolStripItem[] { favoriteItem, new ToolStripSeparator(), renameGroupItem, deleteGroupItem });

        contextMenu.Opening += (s, e) =>
        {
            var selectedNode = _channelTreeView.SelectedNode;
            bool isChannel = selectedNode?.Tag is SalesChannel;
            bool isGroup = selectedNode?.Tag is string tag && tag.StartsWith("GROUP_");

            favoriteItem.Visible = isChannel;
            renameGroupItem.Visible = isGroup && selectedNode?.Text != "미분류" && selectedNode?.Text != "⭐ 즐겨찾기";
            deleteGroupItem.Visible = isGroup && selectedNode?.Text != "미분류" && selectedNode?.Text != "⭐ 즐겨찾기";
        };

        _channelTreeView.ContextMenuStrip = contextMenu;
    }

    private void OnChannelSelected(object? sender, EventArgs e)
    {
        if (_channelTreeView.SelectedNode?.Tag is not SalesChannel selectedChannel)
        {
            _propertyGrid.SelectedObject = null;
            ClearFieldMappingGrids();
            return;
        }

        var config = _channelConfigs.FirstOrDefault(c => c.ChannelCode == selectedChannel.ChannelCode);
        if (config == null)
        {
            config = new ChannelConfig { ChannelCode = selectedChannel.ChannelCode, ChannelName = selectedChannel.ChannelName };
            _channelConfigs.Add(config);
        }

        _propertyGrid.SelectedObject = config;
        LoadFieldMappingGrids(config);
    }

    /// <summary>
    /// 트리에서 지정된 채널 코드를 찾아 선택합니다. 다른 화면(OFS, 마감/이익분석)에서
    /// 채널 설정이 없어 이 창으로 안내될 때 해당 채널을 바로 보여주기 위해 사용합니다.
    /// </summary>
    public void SelectChannelByCode(string channelCode)
    {
        var node = FindChannelNode(channelCode);
        if (node == null) return;

        _channelTreeView.SelectedNode = node;
        node.EnsureVisible();
    }

    private TreeNode? FindChannelNode(string channelCode)
    {
        foreach (TreeNode topNode in _channelTreeView.Nodes)
        {
            foreach (TreeNode childNode in topNode.Nodes)
            {
                if (childNode.Tag is SalesChannel sc && sc.ChannelCode == channelCode)
                {
                    return childNode;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// SalesManagerV2(레거시 Python 도구)의 config 폴더에서 channels_config.json을 읽어 채널별
    /// 정산서 매핑/환율/채널유형/쿠팡그로스 보조소스를 이식한다. 채널명이 일치하는 기존 채널은
    /// 설정을 갱신하고, 없는 채널명은 새로 만든다(코드는 ChannelCodeGenerator로 자동 부여 —
    /// 레거시의 임의 코드를 그대로 쓰지 않음). 레거시는 정산서만 다루는 도구라 발주서 매핑
    /// (수취인/연락처 등)은 이관 대상에 없다 — 별도로 설정해야 한다.
    /// </summary>
    private void OnLegacyChannelImportClick(object? sender, EventArgs e)
    {
        using var folderDialog = new FolderBrowserDialog { Description = "SalesManagerV2의 config 폴더(channels_config.json이 있는 폴더)를 선택하세요" };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

        if (MessageBox.Show(
                "channels_config.json의 채널별 정산서 매핑/환율/채널유형/쿠팡그로스 보조소스를 이식합니다.\n" +
                "채널명이 일치하는 기존 채널은 설정이 덮어써지고, 없는 채널명은 새로 만들어집니다. 계속하시겠습니까?",
                "이관 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var result = _legacyMigrationService.Migrate(folderDialog.SelectedPath);
            LoadData();

            var message = $"신규 채널 {result.CreatedChannels.Count}개, 기존 채널 갱신 {result.UpdatedChannels.Count}개를 이관했습니다.";
            if (result.CreatedChannels.Count > 0) message += $"\n\n신규: {string.Join(", ", result.CreatedChannels)}";
            if (result.UpdatedChannels.Count > 0) message += $"\n갱신: {string.Join(", ", result.UpdatedChannels)}";
            if (result.UnsupportedConditionalFields.Count > 0)
            {
                message += $"\n\n다음 항목은 레거시의 조건부 값 추출 기능을 써서 자동 이관하지 못했습니다(직접 확인 필요):\n{string.Join(", ", result.UnsupportedConditionalFields)}";
            }
            if (result.Warnings.Count > 0) message += $"\n\n{string.Join("\n", result.Warnings)}";

            MessageBox.Show(message, "이관 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"이관 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        using var dialog = new AddChannelDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var newChannelCode = ChannelCodeGenerator.GenerateNext(_channels.Select(c => c.ChannelCode));
        var newChannel = new SalesChannel { ChannelCode = newChannelCode, ChannelName = dialog.ChannelName };
        _salesChannelRepository.Upsert(newChannel);

        LoadData();
        SelectChannelByCode(newChannelCode);
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        if (_channelTreeView.SelectedNode?.Tag is not SalesChannel selectedChannel) return;

        var result = MessageBox.Show($"채널 '{selectedChannel.ChannelName}'을(를) 삭제하시겠습니까?\n관련된 모든 설정이 제거됩니다.", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        // Delete from DB
        _salesChannelRepository.Delete(selectedChannel.ChannelCode);

        // Delete from config list
        var configToRemove = _channelConfigs.FirstOrDefault(c => c.ChannelCode == selectedChannel.ChannelCode);
        if (configToRemove != null)
        {
            _channelConfigs.Remove(configToRemove);
        }

        _channelConfigService.Save(_channelConfigs);
        LoadData();
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        try
        {
            _channelConfigService.Save(_channelConfigs);
            MessageBox.Show("채널 설정이 성공적으로 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnFavoriteClick(object? sender, EventArgs e)
    {
        if (_channelTreeView.SelectedNode?.Tag is not SalesChannel selectedChannel) return;

        selectedChannel.IsFavorite = !selectedChannel.IsFavorite;
        _salesChannelRepository.Upsert(selectedChannel);
        PopulateTreeView();
    }

    private void OnRenameGroupClick(object? sender, EventArgs e)
    {
        if (_channelTreeView.SelectedNode?.Tag is not string tag || !tag.StartsWith("GROUP_")) return;

        var oldGroupName = _channelTreeView.SelectedNode.Text;
        string newGroupName = Microsoft.VisualBasic.Interaction.InputBox($"'{oldGroupName}' 그룹의 새 이름을 입력하세요:", "그룹 이름 변경", oldGroupName);

        if (string.IsNullOrWhiteSpace(newGroupName) || oldGroupName == newGroupName) return;

        var channelsInGroup = _channels.Where(c => c.GroupName == oldGroupName);
        foreach (var channel in channelsInGroup)
        {
            channel.GroupName = newGroupName;
            _salesChannelRepository.Upsert(channel);
        }
        PopulateTreeView();
    }

    private void OnDeleteGroupClick(object? sender, EventArgs e)
    {
        if (_channelTreeView.SelectedNode?.Tag is not string tag || !tag.StartsWith("GROUP_")) return;

        var groupName = _channelTreeView.SelectedNode.Text;
        var result = MessageBox.Show($"그룹 '{groupName}'을(를) 삭제하시겠습니까?\n그룹에 속한 채널들은 '미분류' 그룹으로 이동합니다.", "그룹 삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        var channelsInGroup = _channels.Where(c => c.GroupName == groupName);
        foreach (var channel in channelsInGroup)
        {
            channel.GroupName = null; // '미분류'로 이동
            _salesChannelRepository.Upsert(channel);
        }
        PopulateTreeView();
    }
}

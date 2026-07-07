using System.ComponentModel;
using System.Text.Json;
using MiniERP2.Config;
using MiniERP2.Controls;
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
    private Label _statusLabel = new();
    private TreeView _channelTreeView = new();
    /// <summary>
    /// TreeView는 기본적으로 단일 선택만 지원해서, Ctrl+클릭으로 여러 채널 노드를 함께 선택할 수
    /// 있게 직접 추적한다(소속 그룹과 무관하게 채널만 담음 — 그룹 노드는 다중선택 대상이 아니다).
    /// </summary>
    private readonly List<TreeNode> _selectedChannelNodes = new();
    private DataGridView _orderMappingGrid = new();
    private DataGridView _settlementMappingGrid = new();
    private DataGridView _courierOverrideGrid = new();
    private DataGridView _auxSourceGrid = new();
    private ListBox _adLayoutListBox = new();
    private Button _btnAdLayoutAdd = new();
    private Button _btnAdLayoutDelete = new();
    private TextBox _adMatchColumnsTextBox = new();
    private DataGridView _adFieldMappingGrid = new();
    private AdFileLayout? _currentAdLayout;
    private CheckBox _cfsFeeEnabledCheckBox = new();
    private PropertyGrid _cfsFeePropertyGrid = new();

    private static readonly StdField[] OrderMappingFields =
    [
        StdField.ProductNo, StdField.ProductName, StdField.OptionName, StdField.Quantity,
        StdField.Recipient, StdField.Phone, StdField.Address, StdField.DeliveryMessage, StdField.OrderDate,
    ];

    /// <summary>
    /// 쿠팡그로스 외 모든 채널의 정산서 매핑 표준필드(2026-06-28 사용자 요청으로 전면 교체).
    /// 매핑유무/채널/상품그룹/매핑SKU는 채널설정이 아니라 마감/이익분석 화면에서 자동으로
    /// 채워지는 값이라 여기 목록에는 없다. 이익액은 일부러 빠져있다 — 이 앱이 원가기반으로
    /// 자동 계산하므로 정산파일에서 읽어올 필요가 없다(마감/이익분석 결과에서만 도출됨).
    /// 정산액은 손익계산의 기준값으로는 계속 쓰이지만, 식별용 그리드에는 더 이상 노출하지 않는다.
    /// </summary>
    private static readonly StdField[] SettlementMappingFieldsDefault =
    [
        StdField.ProductName, StdField.OptionName, StdField.Quantity,
        StdField.Revenue, StdField.ShippingFee, StdField.SettlementAmount, StdField.TrackingNo,
    ];

    /// <summary>쿠팡그로스는 사용자 요청에 따라 기존 6필드/손익공식을 그대로 유지한다.</summary>
    private static readonly StdField[] SettlementMappingFieldsCoupangGrowth =
    [
        StdField.ProductName, StdField.OptionName, StdField.Quantity,
        StdField.Revenue, StdField.SettlementAmount, StdField.ShippingFee, StdField.HandlingFee,
    ];

    /// <summary>
    /// 쿠팡로켓 전용 필드: TaxNo(계산서번호)를 추가로 매핑한다.
    /// TaxDate(작성일자)는 계산서발행내역 JOIN으로 자동 채워지므로 여기서는 지정하지 않는다.
    /// </summary>
    private static readonly StdField[] SettlementMappingFieldsCoupangRocket =
    [
        StdField.ProductName, StdField.OptionName, StdField.Quantity,
        StdField.Revenue, StdField.SettlementAmount, StdField.TrackingNo, StdField.TaxNo,
    ];

    private static readonly (AdStdField Field, string Label)[] AdMappingFields =
    [
        (AdStdField.ProductName, "상품명"),
        (AdStdField.ProductId, "상품번호/캠페인"),
        (AdStdField.OptionName, "옵션명"),
        (AdStdField.Cost, "광고비"),
        (AdStdField.Extra1, "추가항목1"),
        (AdStdField.Extra2, "추가항목2"),
        (AdStdField.Note1, "비고1"),
        (AdStdField.Note2, "비고2"),
        (AdStdField.Note3, "비고3"),
    ];

    private static StdField[] ResolveSettlementMappingFields(ChannelConfig config) => config.ChannelType switch
    {
        ChannelType.CoupangGrowth => SettlementMappingFieldsCoupangGrowth,
        ChannelType.CoupangRocket => SettlementMappingFieldsCoupangRocket,
        _ => SettlementMappingFieldsDefault,
    };

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
            DrawMode = TreeViewDrawMode.OwnerDrawText,
            AllowDrop = true,
        };
        _channelTreeView.AfterSelect += OnChannelSelected;
        _channelTreeView.DrawNode += OnChannelTreeDrawNode;
        _channelTreeView.NodeMouseClick += OnChannelTreeNodeMouseClick;
        _channelTreeView.ItemDrag += OnChannelTreeItemDrag;
        _channelTreeView.DragEnter += OnChannelTreeDragEnter;
        _channelTreeView.DragOver += OnChannelTreeDragOver;
        _channelTreeView.DragDrop += OnChannelTreeDragDrop;
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
        _statusLabel = new Label { AutoSize = true, Padding = new Padding(10, 7, 0, 0), ForeColor = Color.DarkGreen };
        buttonPanel.Controls.Add(_statusLabel);

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
        rightTabControl.TabPages.Add(CreateAuxSourceTab());
        rightTabControl.TabPages.Add(CreateAdFieldMappingTab());
        rightTabControl.TabPages.Add(CreateCfsFeeTab());

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

        // 채널유형(특히 쿠팡그로스 ↔ 그 외)에 따라 정산서 매핑의 표준필드 목록 자체가 달라지므로,
        // 유형을 바꾸면 그 즉시 정산서 매핑 그리드를 새 필드 목록으로 다시 그린다.
        if (e.ChangedItem.PropertyDescriptor?.Name == nameof(ChannelConfig.ChannelType))
        {
            _settlementMappingGrid.DataSource = BuildFieldMappingRows(ResolveSettlementMappingFields(_currentConfig), _currentConfig.SettlementFieldMappings);
        }

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
            using var ofd = new OpenFileDialog { Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|All files (*.*)|*.*", Title = "샘플 파일을 선택하세요" };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                var opened = Path.GetExtension(ofd.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                    ? CsvWorkbookReader.LoadAsPackage(ofd.FileName)
                    : ExcelFileOpener.OpenWithPasswordPrompt(ofd.FileName, this);
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
        _settlementMappingGrid.DataSource = BuildFieldMappingRows(ResolveSettlementMappingFields(config), config.SettlementFieldMappings);
        LoadCourierOverrideGrid(config);
        LoadAuxSourceGrid(config);
        LoadAdFieldMappingGrid(config);
        LoadCfsFeeTab(config);
    }

    private void ClearFieldMappingGrids()
    {
        _currentConfig = null;
        _orderMappingGrid.DataSource = null;
        _settlementMappingGrid.DataSource = null;
        _courierOverrideGrid.DataSource = null;
        _auxSourceGrid.DataSource = null;
        _adLayoutListBox.Items.Clear();
        _adMatchColumnsTextBox.Text = string.Empty;
        _adFieldMappingGrid.DataSource = null;
        _currentAdLayout = null;
        _cfsFeeEnabledCheckBox.Checked = false;
        _cfsFeePropertyGrid.SelectedObject = null;
        _cfsFeePropertyGrid.Enabled = false;
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

        _courierOverrideGrid = new ExcelLikeDataGridView
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

    // ─── 보조 소스(GrowthAuxSource) 탭 ───────────────────────────────────────

    private TabPage CreateAuxSourceTab()
    {
        var tabPage = new TabPage("보조 소스");

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        toolbar.Controls.Add(new Label
        {
            Text = "쿠팡그로스 등 정산파일에서 배송비/입출고비가 별도 시트에 있을 때 연결 설정입니다.\n" +
                   "키 컬럼 헤더: 메인 시트와 보조 시트에 공통으로 있는 식별 열(예: 옵션ID). " +
                   "값 컬럼 헤더: 보조 시트에서 가져올 금액이 있는 열.",
            AutoSize = false,
            Dock = DockStyle.Fill,
        });

        _auxSourceGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
        };

        var targetFieldValues = Enum.GetValues<StdField>().Cast<object>().ToArray();

        _auxSourceGrid.Columns.AddRange(
            new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "활성화", DataPropertyName = "Enabled", Width = 55 },
            new DataGridViewComboBoxColumn
            {
                Name = "TargetStdField", HeaderText = "대상 표준 필드", DataPropertyName = "TargetStdField",
                Width = 140, FlatStyle = FlatStyle.Flat,
                Items = { StdField.ShippingFee, StdField.HandlingFee, StdField.SettlementAmount },
            },
            new DataGridViewTextBoxColumn { Name = "SheetName", HeaderText = "시트 이름", DataPropertyName = "SheetName", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "HeaderRow", HeaderText = "헤더 행", DataPropertyName = "HeaderRow", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "KeyHeader", HeaderText = "키 컬럼 헤더", DataPropertyName = "KeyHeader", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "ValueHeader", HeaderText = "값 컬럼 헤더", DataPropertyName = "ValueHeader", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );

        _auxSourceGrid.DataError += (s, e) => e.ThrowException = false;
        _auxSourceGrid.CellValueChanged += (s, e) => SyncAuxSources();
        _auxSourceGrid.RowsAdded += (s, e) => SyncAuxSources();
        _auxSourceGrid.RowsRemoved += (s, e) => SyncAuxSources();

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_auxSourceGrid, 0, 1);
        tabPage.Controls.Add(layout);
        return tabPage;
    }

    private void LoadAuxSourceGrid(ChannelConfig config)
    {
        var rows = config.GrowthAuxSources
            .Select(a => new AuxSourceRow
            {
                Enabled = a.Enabled,
                TargetStdField = a.TargetStdField,
                SheetName = a.SheetName ?? string.Empty,
                HeaderRow = a.HeaderRow,
                KeyHeader = a.KeyHeader ?? string.Empty,
                ValueHeader = a.ValueHeader ?? string.Empty,
            })
            .ToList();
        _auxSourceGrid.DataSource = new BindingList<AuxSourceRow>(rows);
    }

    private void SyncAuxSources()
    {
        if (_currentConfig == null) return;
        _currentConfig.GrowthAuxSources = (_auxSourceGrid.DataSource as BindingList<AuxSourceRow>)?
            .Where(r => !string.IsNullOrWhiteSpace(r.KeyHeader) && !string.IsNullOrWhiteSpace(r.ValueHeader))
            .Select(r => new GrowthAuxSource
            {
                Enabled = r.Enabled,
                TargetStdField = r.TargetStdField,
                SheetName = r.SheetName,
                HeaderRow = r.HeaderRow <= 0 ? 1 : r.HeaderRow,
                KeyHeader = r.KeyHeader,
                ValueHeader = r.ValueHeader,
            })
            .ToList() ?? [];
    }

    private class AuxSourceRow
    {
        public bool Enabled { get; set; }
        public StdField TargetStdField { get; set; } = StdField.ShippingFee;
        public string SheetName { get; set; } = string.Empty;
        public int HeaderRow { get; set; } = 1;
        public string KeyHeader { get; set; } = string.Empty;
        public string ValueHeader { get; set; } = string.Empty;
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
        StdField.Revenue => "매출액",
        StdField.TrackingNo => "실제발송송장수(원본 송장번호 열)",
        _ => field.ToString(),
    };

    private TabPage CreateCfsFeeTab()
    {
        var tabPage = new TabPage("CFS 설정");

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var note = new Label
        {
            Text = "쿠팡그로스 CFS(쿠팡풀필먼트서비스) 입출고비·배송비 파일 연동 설정입니다. " +
                   "활성화하면 마감/이익분석 파일 로드 시 CFS 파일을 자동으로 인식하여 입출고비·배송비에 배분합니다. " +
                   "(활성화 시 GrowthAuxSource의 HandlingFee/ShippingFee는 무시됩니다.)",
            Dock = DockStyle.Fill,
            AutoSize = false,
        };

        _cfsFeeEnabledCheckBox = new CheckBox { Text = "CFS 파일 연동 사용 (쿠팡그로스 전용)", AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        _cfsFeeEnabledCheckBox.CheckedChanged += OnCfsFeeEnabledChanged;

        _cfsFeePropertyGrid = new PropertyGrid { Dock = DockStyle.Fill, Enabled = false };

        layout.Controls.Add(note, 0, 0);
        layout.Controls.Add(_cfsFeeEnabledCheckBox, 0, 1);
        layout.Controls.Add(_cfsFeePropertyGrid, 0, 2);
        tabPage.Controls.Add(layout);
        return tabPage;
    }

    private void LoadCfsFeeTab(ChannelConfig config)
    {
        _cfsFeeEnabledCheckBox.CheckedChanged -= OnCfsFeeEnabledChanged;
        _cfsFeeEnabledCheckBox.Checked = config.GrowthCfsFee != null;
        _cfsFeePropertyGrid.SelectedObject = config.GrowthCfsFee;
        _cfsFeePropertyGrid.Enabled = config.GrowthCfsFee != null;
        _cfsFeeEnabledCheckBox.CheckedChanged += OnCfsFeeEnabledChanged;
    }

    private void OnCfsFeeEnabledChanged(object? sender, EventArgs e)
    {
        if (_currentConfig == null) return;
        if (_cfsFeeEnabledCheckBox.Checked)
        {
            _currentConfig.GrowthCfsFee ??= new GrowthCfsFeeConfig();
        }
        else
        {
            _currentConfig.GrowthCfsFee = null;
        }
        _cfsFeePropertyGrid.SelectedObject = _currentConfig.GrowthCfsFee;
        _cfsFeePropertyGrid.Enabled = _cfsFeeEnabledCheckBox.Checked;
        _channelConfigService.Save(_channelConfigs);
    }

    private TabPage CreateAdFieldMappingTab()
    {
        var tabPage = new TabPage("광고비 헤더 설정");

        // ── 왼쪽: 레이아웃 목록 ──────────────────────────────────
        _adLayoutListBox = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _adLayoutListBox.SelectedIndexChanged += OnAdLayoutSelected;
        _adLayoutListBox.DoubleClick += OnAdLayoutRename;

        _btnAdLayoutAdd = new Button { Text = "추가", Width = 60, Height = 28 };
        _btnAdLayoutAdd.Click += OnAdLayoutAdd;
        _btnAdLayoutDelete = new Button { Text = "삭제", Width = 60, Height = 28 };
        _btnAdLayoutDelete.Click += OnAdLayoutDelete;
        var btnRename = new Button { Text = "이름변경", Width = 80, Height = 28 };
        btnRename.Click += OnAdLayoutRename;

        var layoutBtnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 36,
            FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4, 4, 0, 0)
        };
        layoutBtnPanel.Controls.AddRange([_btnAdLayoutAdd, _btnAdLayoutDelete, btnRename]);

        var leftPanel = new Panel { Dock = DockStyle.Left, Width = 190 };
        leftPanel.Controls.Add(_adLayoutListBox);
        leftPanel.Controls.Add(layoutBtnPanel);

        var leftLabel = new Label
        {
            Text = "레이아웃 목록", Dock = DockStyle.Top, Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold)
        };
        leftPanel.Controls.Add(leftLabel);

        // ── 오른쪽: 자동탐지 조건 + 필드 매핑 ───────────────────
        _adFieldMappingGrid.AutoGenerateColumns = false;
        _adFieldMappingGrid.AllowUserToAddRows = false;
        _adFieldMappingGrid.AllowUserToDeleteRows = false;
        _adFieldMappingGrid.Dock = DockStyle.Fill;
        _adFieldMappingGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Label", HeaderText = "표준 필드", DataPropertyName = "Label", Width = 130, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "SheetName", HeaderText = "시트 이름", DataPropertyName = "SheetName", Width = 110 },
            new DataGridViewTextBoxColumn { Name = "HeaderRow", HeaderText = "헤더 행", DataPropertyName = "HeaderRow", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "Column", HeaderText = "열(헤더 텍스트)", DataPropertyName = "Column", Width = 140 },
            new DataGridViewTextBoxColumn { Name = "FixedValue", HeaderText = "고정값(선택)", DataPropertyName = "FixedValue", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _adFieldMappingGrid.CellValueChanged += (s, e) => OnAdFieldMappingGridCellChanged(e);

        var btnLoadSample = new Button { Text = "샘플 파일 불러오기", AutoSize = true };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(5) };
        toolbar.Controls.Add(btnLoadSample);

        _adMatchColumnsTextBox = new TextBox
        {
            Dock = DockStyle.Top, Height = 72, Multiline = true, ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "자동탐지 AND 조건 (한 줄에 하나씩 — 비워두면 수동 선택)"
        };
        _adMatchColumnsTextBox.Leave += OnAdMatchColumnsLeave;

        var matchLabel = new Label
        {
            Text = "자동탐지 문구 (AND 조건):",
            Dock = DockStyle.Top, Height = 20,
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(2, 0, 0, 0)
        };

        var gridSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 580 };
        gridSplit.Panel1.Controls.Add(_adFieldMappingGrid);
        gridSplit.Panel2.Controls.Add(CreateSamplePreviewPanel(_adFieldMappingGrid, btnLoadSample));

        var rightPanel = new Panel { Dock = DockStyle.Fill };
        rightPanel.Controls.Add(gridSplit);      // fill
        rightPanel.Controls.Add(matchLabel);     // top (added last → drawn below toolbar)
        rightPanel.Controls.Add(_adMatchColumnsTextBox); // top
        rightPanel.Controls.Add(toolbar);        // top

        tabPage.Controls.Add(rightPanel);
        tabPage.Controls.Add(leftPanel);
        return tabPage;
    }

    private void LoadAdFieldMappingGrid(ChannelConfig config)
    {
        _adLayoutListBox.Items.Clear();
        _adMatchColumnsTextBox.Text = string.Empty;
        _adFieldMappingGrid.DataSource = null;
        _currentAdLayout = null;

        foreach (var layout in config.AdFileLayouts)
            _adLayoutListBox.Items.Add(layout);

        _adLayoutListBox.DisplayMember = "LayoutName";

        if (_adLayoutListBox.Items.Count > 0)
            _adLayoutListBox.SelectedIndex = 0;
    }

    private void OnAdLayoutSelected(object? sender, EventArgs e)
    {
        // Flush match-columns text from previous layout before switching
        FlushMatchColumns();

        _currentAdLayout = _adLayoutListBox.SelectedItem as AdFileLayout;
        if (_currentAdLayout == null)
        {
            _adMatchColumnsTextBox.Text = string.Empty;
            _adFieldMappingGrid.DataSource = null;
            return;
        }

        _adMatchColumnsTextBox.Text = string.Join(Environment.NewLine, _currentAdLayout.MatchColumns);

        var rows = AdMappingFields.Select(f =>
        {
            _currentAdLayout.FieldMappings.TryGetValue(f.Field, out var mapping);
            return new AdFieldMappingRow
            {
                Field = f.Field,
                Label = f.Label,
                SheetName = mapping?.SheetName,
                HeaderRow = mapping?.HeaderRow ?? 1,
                Column = mapping?.Column,
                FixedValue = mapping?.FixedValue,
            };
        }).ToList();
        _adFieldMappingGrid.DataSource = new BindingList<AdFieldMappingRow>(rows);
    }

    private void OnAdMatchColumnsLeave(object? sender, EventArgs e) => FlushMatchColumns();

    private void FlushMatchColumns()
    {
        if (_currentAdLayout == null) return;
        _currentAdLayout.MatchColumns = _adMatchColumnsTextBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private void OnAdLayoutAdd(object? sender, EventArgs e)
    {
        if (_currentConfig == null) return;
        var name = PromptInput("새 레이아웃 이름을 입력하세요:", "레이아웃 추가", "새 레이아웃");
        if (name == null) return;
        var layout = new AdFileLayout { LayoutName = name };
        _currentConfig.AdFileLayouts.Add(layout);
        _adLayoutListBox.Items.Add(layout);
        _adLayoutListBox.SelectedItem = layout;
    }

    private void OnAdLayoutDelete(object? sender, EventArgs e)
    {
        if (_currentConfig == null || _currentAdLayout == null) return;
        if (MessageBox.Show($"레이아웃 '{_currentAdLayout.LayoutName}'을 삭제하시겠습니까?",
                "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _currentConfig.AdFileLayouts.Remove(_currentAdLayout);
        _adLayoutListBox.Items.Remove(_currentAdLayout);
    }

    private void OnAdLayoutRename(object? sender, EventArgs e)
    {
        if (_currentAdLayout == null) return;
        var name = PromptInput("새 이름을 입력하세요:", "이름 변경", _currentAdLayout.LayoutName);
        if (name == null || name == _currentAdLayout.LayoutName) return;
        _currentAdLayout.LayoutName = name;
        // ListBox refresh
        int idx = _adLayoutListBox.SelectedIndex;
        _adLayoutListBox.Items[idx] = _currentAdLayout;
        _adLayoutListBox.SelectedIndex = idx;
    }

    private static string? PromptInput(string prompt, string title, string defaultValue)
    {
        using var form = new Form
        {
            Text = title, Size = new Size(340, 130), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false
        };
        var lbl = new Label { Text = prompt, Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(8, 0, 0, 0) };
        var txt = new TextBox { Text = defaultValue, Dock = DockStyle.Top, Margin = new Padding(8) };
        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        var btnOk = new Button { Text = "확인", DialogResult = DialogResult.OK, Width = 70 };
        var btnCancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 70 };
        btnPanel.Controls.AddRange([btnCancel, btnOk]);
        form.Controls.AddRange([lbl, txt, btnPanel]);
        form.AcceptButton = btnOk; form.CancelButton = btnCancel;
        txt.SelectAll();
        return form.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : null;
    }

    private void OnAdFieldMappingGridCellChanged(DataGridViewCellEventArgs e)
    {
        if (_currentAdLayout == null || e.RowIndex < 0 || e.RowIndex >= _adFieldMappingGrid.Rows.Count) return;
        if (_adFieldMappingGrid.Rows[e.RowIndex].DataBoundItem is not AdFieldMappingRow row) return;

        var inUse = !string.IsNullOrWhiteSpace(row.Column) || !string.IsNullOrWhiteSpace(row.FixedValue);
        if (!inUse)
        {
            _currentAdLayout.FieldMappings.Remove(row.Field);
        }
        else
        {
            _currentAdLayout.FieldMappings[row.Field] = new FieldMapping
            {
                SheetName = row.SheetName,
                HeaderRow = row.HeaderRow <= 0 ? 1 : row.HeaderRow,
                Column = row.Column,
                FixedValue = row.FixedValue,
            };
        }
    }

    private class AdFieldMappingRow
    {
        public AdStdField Field { get; set; }
        public string Label { get; set; } = string.Empty;
        public string? SheetName { get; set; }
        public int HeaderRow { get; set; } = 1;
        public string? Column { get; set; }
        public string? FixedValue { get; set; }
    }

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
        // 다시 그리면 기존 TreeNode 인스턴스가 전부 새로 만들어지므로, 이전 노드를 참조하던
        // 다중선택 상태를 같이 비운다(안 비우면 더 이상 트리에 없는 노드를 계속 강조하게 됨).
        _selectedChannelNodes.Clear();
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
        var moveToGroupItem = new ToolStripMenuItem("그룹 이동...", null, OnMoveChannelToGroupClick);
        var renameGroupItem = new ToolStripMenuItem("그룹 이름 변경", null, OnRenameGroupClick);
        var deleteGroupItem = new ToolStripMenuItem("그룹 삭제", null, OnDeleteGroupClick);

        contextMenu.Items.AddRange(new ToolStripItem[] { favoriteItem, moveToGroupItem, new ToolStripSeparator(), renameGroupItem, deleteGroupItem });

        contextMenu.Opening += (s, e) =>
        {
            var selectedNode = _channelTreeView.SelectedNode;
            var selectedChannelCount = GetSelectedChannels().Count;
            bool isGroup = _selectedChannelNodes.Count == 0 && selectedNode?.Tag is string tag && tag.StartsWith("GROUP_");

            favoriteItem.Visible = selectedChannelCount > 0;
            favoriteItem.Text = selectedChannelCount > 1 ? $"즐겨찾기에 추가/제거 ({selectedChannelCount}개)" : "즐겨찾기에 추가/제거";
            moveToGroupItem.Visible = selectedChannelCount > 0;
            moveToGroupItem.Text = selectedChannelCount > 1 ? $"그룹 이동... ({selectedChannelCount}개)" : "그룹 이동...";
            renameGroupItem.Visible = isGroup && selectedNode?.Text != "미분류" && selectedNode?.Text != "⭐ 즐겨찾기";
            deleteGroupItem.Visible = isGroup && selectedNode?.Text != "미분류" && selectedNode?.Text != "⭐ 즐겨찾기";
        };

        _channelTreeView.ContextMenuStrip = contextMenu;
    }

    /// <summary>Ctrl+클릭으로 다중선택된 채널이 있으면 그걸, 없으면 지금 단일선택된 채널 1개를 반환한다.</summary>
    private List<SalesChannel> GetSelectedChannels()
    {
        if (_selectedChannelNodes.Count > 0)
        {
            return _selectedChannelNodes.Select(n => n.Tag).OfType<SalesChannel>().ToList();
        }
        return _channelTreeView.SelectedNode?.Tag is SalesChannel channel ? [channel] : [];
    }

    private void OnChannelTreeDrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        var node = e.Node!;
        bool isSelected = _selectedChannelNodes.Contains(node) || node == _channelTreeView.SelectedNode;

        var backColor = isSelected ? SystemColors.Highlight : _channelTreeView.BackColor;
        var foreColor = isSelected ? SystemColors.HighlightText : _channelTreeView.ForeColor;

        using var backBrush = new SolidBrush(backColor);
        e.Graphics.FillRectangle(backBrush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, node.Text, node.NodeFont ?? _channelTreeView.Font, e.Bounds, foreColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        e.DrawDefault = false;
    }

    /// <summary>
    /// Ctrl+클릭으로 채널 노드를 다중선택에 추가/제거한다(여러 채널을 한 그룹으로 옮기기 위함).
    /// 그룹 노드를 클릭하거나 Ctrl 없이 클릭하면 다중선택을 비워 일반 단일선택으로 되돌린다.
    /// </summary>
    private void OnChannelTreeNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node.Tag is not SalesChannel)
        {
            if (_selectedChannelNodes.Count > 0)
            {
                _selectedChannelNodes.Clear();
                _channelTreeView.Invalidate();
            }
            return;
        }

        if (ModifierKeys.HasFlag(Keys.Control))
        {
            if (!_selectedChannelNodes.Remove(e.Node))
            {
                _selectedChannelNodes.Add(e.Node);
            }
        }
        else
        {
            _selectedChannelNodes.Clear();
        }
        _channelTreeView.Invalidate();
    }

    /// <summary>다중선택된 채널(또는 드래그를 시작한 채널 1개)을 드래그 데이터로 싣는다.</summary>
    private void OnChannelTreeItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not TreeNode node || node.Tag is not SalesChannel) return;

        var channels = GetSelectedChannels();
        if (channels.Count == 0) return;

        _channelTreeView.DoDragDrop(channels, DragDropEffects.Move);
    }

    private void OnChannelTreeDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(List<SalesChannel>)) == true ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void OnChannelTreeDragOver(object? sender, DragEventArgs e)
    {
        var targetNode = _channelTreeView.GetNodeAt(_channelTreeView.PointToClient(new Point(e.X, e.Y)));
        e.Effect = targetNode != null && e.Data?.GetDataPresent(typeof(List<SalesChannel>)) == true
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    /// <summary>
    /// 채널 노드를 그룹 노드 위로 드래그&드롭하면 그 그룹으로 옮긴다. "⭐ 즐겨찾기" 그룹에
    /// 드롭하면 GroupName이 아니라 IsFavorite를 켠다(즐겨찾기는 그룹과 별개 축이라서). 채널 노드
    /// 위에 드롭하면 그 채널이 속한 그룹으로 옮긴다(그룹을 직접 못 찾아도 직관적으로 동작하게).
    /// </summary>
    private void OnChannelTreeDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(List<SalesChannel>)) is not List<SalesChannel> draggedChannels || draggedChannels.Count == 0) return;

        var targetNode = _channelTreeView.GetNodeAt(_channelTreeView.PointToClient(new Point(e.X, e.Y)));
        if (targetNode == null) return;

        if (targetNode.Tag is string favoritesTag && favoritesTag == "GROUP_FAVORITES")
        {
            foreach (var channel in draggedChannels)
            {
                channel.IsFavorite = true;
                _salesChannelRepository.Upsert(channel);
            }
            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text = $"{draggedChannels.Count}개 채널을 즐겨찾기에 추가했습니다. ({DateTime.Now:HH:mm:ss})";
        }
        else if (targetNode.Tag is string groupTag && groupTag.StartsWith("GROUP_"))
        {
            var targetGroupName = targetNode.Text == "미분류" ? null : targetNode.Text;
            foreach (var channel in draggedChannels)
            {
                channel.GroupName = targetGroupName;
                _salesChannelRepository.Upsert(channel);
            }
            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text = $"{draggedChannels.Count}개 채널을 '{targetNode.Text}' 그룹으로 옮겼습니다. ({DateTime.Now:HH:mm:ss})";
        }
        else if (targetNode.Tag is SalesChannel targetChannel)
        {
            var targetGroupName = targetChannel.GroupName;
            foreach (var channel in draggedChannels)
            {
                channel.GroupName = targetGroupName;
                _salesChannelRepository.Upsert(channel);
            }
            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text = $"{draggedChannels.Count}개 채널을 '{targetGroupName ?? "미분류"}' 그룹으로 옮겼습니다. ({DateTime.Now:HH:mm:ss})";
        }
        else
        {
            return;
        }

        PopulateTreeView();
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
        FlushMatchColumns();
        try
        {
            _channelConfigService.Save(_channelConfigs);
            // 2026-06-28 점검: 저장 직후 성공 모달을 띄우면, 마감/이익분석·매핑관리창·광고매핑에서
            // 반복 재현됐던 "모달이 안 보이게 생성되는" 경쟁 상태와 같은 위험군이라 비모달 라벨로 대체.
            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text = $"채널 설정이 저장되었습니다. ({DateTime.Now:HH:mm:ss})";
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = $"저장 중 오류가 발생했습니다: {ex.Message}";
        }
    }

    private void OnFavoriteClick(object? sender, EventArgs e)
    {
        var channels = GetSelectedChannels();
        if (channels.Count == 0) return;

        foreach (var channel in channels)
        {
            channel.IsFavorite = !channel.IsFavorite;
            _salesChannelRepository.Upsert(channel);
        }
        PopulateTreeView();
    }

    /// <summary>Ctrl+클릭으로 다중선택된 채널이 있으면 전부 같은 그룹으로 옮긴다(없으면 단일선택 1개).</summary>
    private void OnMoveChannelToGroupClick(object? sender, EventArgs e)
    {
        var channels = GetSelectedChannels();
        if (channels.Count == 0) return;

        var existingGroups = _channels
            .Select(c => c.GroupName)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct()
            .OrderBy(g => g)
            .Select(g => g!)
            .ToList();

        var description = channels.Count == 1 ? channels[0].ChannelName : $"채널 {channels.Count}개";
        var currentGroupHint = channels.Count == 1 ? channels[0].GroupName ?? string.Empty : string.Empty;

        using var dialog = new MoveChannelToGroupDialog(existingGroups, currentGroupHint, description);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        foreach (var channel in channels)
        {
            channel.GroupName = dialog.GroupName;
            _salesChannelRepository.Upsert(channel);
        }

        PopulateTreeView();
        if (channels.Count == 1) SelectChannelByCode(channels[0].ChannelCode);
        _statusLabel.ForeColor = Color.DarkGreen;
        _statusLabel.Text = $"{channels.Count}개 채널을 '{dialog.GroupName ?? "미분류"}' 그룹으로 옮겼습니다. ({DateTime.Now:HH:mm:ss})";
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

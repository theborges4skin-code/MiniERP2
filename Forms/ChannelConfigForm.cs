using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 기획서 5.4절 '채널 설정 창'
/// </summary>
public class ChannelConfigForm : Form
{
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly ChannelConfigService _channelConfigService = new();

    private List<SalesChannel> _channels = new();
    private List<ChannelConfig> _channelConfigs = new();
    private ChannelConfig? _currentConfig;

    private PropertyGrid _propertyGrid = new();
    private TreeView _channelTreeView = new();
    private DataGridView _orderMappingGrid = new();
    private DataGridView _settlementMappingGrid = new();

    private static readonly StdField[] OrderMappingFields =
    [
        StdField.ProductNo, StdField.ProductName, StdField.OptionName, StdField.Quantity,
        StdField.Recipient, StdField.Phone, StdField.Address,
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
        var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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

        leftPanel.Controls.Add(_channelTreeView, 0, 0);
        leftPanel.Controls.Add(buttonPanel, 0, 1);
        leftPanel.Controls.Add(courierPanel, 0, 2);

        // Right Panel (Tabs: 기본 정보 / 발주서 매핑 / 정산서 매핑)
        var rightTabControl = new TabControl { Dock = DockStyle.Fill };
        rightTabControl.TabPages.Add(CreateBasicInfoTab());
        rightTabControl.TabPages.Add(CreateFieldMappingTab("발주서 매핑", _orderMappingGrid));
        rightTabControl.TabPages.Add(CreateFieldMappingTab("정산서 매핑", _settlementMappingGrid));

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
        tabPage.Controls.Add(_propertyGrid);
        return tabPage;
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
        toolbar.Controls.Add(btnHelp);

        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Label", HeaderText = "표준 필드", DataPropertyName = "Label", Width = 150, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "SheetName", HeaderText = "시트 이름", DataPropertyName = "SheetName", Width = 150 },
            new DataGridViewTextBoxColumn { Name = "HeaderRow", HeaderText = "헤더 행", DataPropertyName = "HeaderRow", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "Column", HeaderText = "열(헤더 텍스트)", DataPropertyName = "Column", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        grid.CellValueChanged += (s, e) => OnFieldMappingGridCellChanged(grid, e);

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        tabPage.Controls.Add(layout);
        return tabPage;
    }

    private void OnFieldMappingGridCellChanged(DataGridView grid, DataGridViewCellEventArgs e)
    {
        if (_currentConfig == null || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
        if (grid.Rows[e.RowIndex].DataBoundItem is not FieldMappingRow row) return;

        var dict = grid == _orderMappingGrid ? _currentConfig.OrderFieldMappings : _currentConfig.SettlementFieldMappings;

        if (string.IsNullOrWhiteSpace(row.Column))
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
            };
        }
    }

    private void LoadFieldMappingGrids(ChannelConfig config)
    {
        _currentConfig = config;
        _orderMappingGrid.DataSource = BuildFieldMappingRows(OrderMappingFields, config.OrderFieldMappings);
        _settlementMappingGrid.DataSource = BuildFieldMappingRows(SettlementMappingFields, config.SettlementFieldMappings);
    }

    private void ClearFieldMappingGrids()
    {
        _currentConfig = null;
        _orderMappingGrid.DataSource = null;
        _settlementMappingGrid.DataSource = null;
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
        _ => field.ToString(),
    };

    private class FieldMappingRow
    {
        public StdField StdField { get; set; }
        public string Label { get; set; } = string.Empty;
        public string? SheetName { get; set; }
        public int HeaderRow { get; set; } = 1;
        public string? Column { get; set; }
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

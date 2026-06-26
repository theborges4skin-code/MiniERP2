using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

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

    private PropertyGrid _propertyGrid = new();
    private TreeView _channelTreeView = new();

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
        var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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

        leftPanel.Controls.Add(_channelTreeView, 0, 0);
        leftPanel.Controls.Add(buttonPanel, 0, 1);

        // Right Panel (Property Grid)
        _propertyGrid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            HelpVisible = true,
            ToolbarVisible = true,
            PropertySort = PropertySort.Categorized
        };

        mainLayout.Controls.Add(leftPanel, 0, 0);
        mainLayout.Controls.Add(_propertyGrid, 1, 0);
        Controls.Add(mainLayout);
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

        contextMenu.Items.AddRange(new ToolStripItem[] { favoriteItem, new ToolStripSeparator(), renameGroupItem, deleteGroup-Item });

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
            return;
        }

        var config = _channelConfigs.FirstOrDefault(c => c.ChannelCode == selectedChannel.ChannelCode);
        if (config == null)
        {
            config = new ChannelConfig { ChannelCode = selectedChannel.ChannelCode, ChannelName = selectedChannel.ChannelName };
            _channelConfigs.Add(config);
        }

        _propertyGrid.SelectedObject = config;
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        using var dialog = new AddChannelDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var newChannelCode = dialog.ChannelCode;
        var newChannelName = dialog.ChannelName;

        if (_channels.Any(c => c.ChannelCode.Equals(newChannelCode, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("이미 존재하는 채널 코드입니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var newChannel = new SalesChannel { ChannelCode = newChannelCode, ChannelName = newChannelName };
        _salesChannelRepository.Upsert(newChannel);

        LoadData(); // UI와 데이터를 다시 로드
        // TODO: 새로 추가된 노드를 찾아 선택하는 로직 추가
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
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 파일 로드 시 사용할 채널을 선택하는 다이얼로그입니다.
/// 그룹별 폴더 트리 + 즐겨찾기 폴더 + 검색창으로 채널이 많아져도 빠르게 찾을 수 있게 합니다.
/// 그룹을 한 줄로 세로 나열하면 목록이 길어져 구분이 어렵다는 지적에 따라, 그룹 폴더들을
/// 3열(다중 구역)로 나눠 한눈에 보이도록 배치한다.
/// </summary>
public class SelectChannelDialog : Form
{
    private const string FavKey = "__FAV__";
    private const string UnclassifiedKey = "(미분류)";
    private const int ColumnCount = 3;
    private const int ColStarW = 22;
    private const int ColCodeW = 92;
    private const int ColNameW = 145;
    private const int ColLastUsedW = 72;
    private const int LeafRowWidth = ColStarW + ColCodeW + ColNameW + ColLastUsedW;

    private readonly TextBox _searchBox = new();
    private readonly ChannelColumn[] _columns = new ChannelColumn[ColumnCount];
    private Font? _boldFont;

    private List<SalesChannel> _allChannels = new();
    private readonly HashSet<string> _expandedGroupKeys = new() { FavKey };
    private readonly string? _pinnedGroupName;
    private bool _suppressExpandEvents;
    private bool _suppressSelectionSync;

    private readonly SelectChannelDialogStateService _stateService;
    private string? _restoreChannelCode;
    private bool _restoreApplied;

    public SalesChannel? SelectedChannel { get; private set; }

    /// <summary>열 하나(헤더 + 트리)를 묶어 관리한다. 3열 배치에서는 열마다 헤더 정렬이 따로 필요하다.</summary>
    private sealed class ChannelColumn
    {
        public Panel Container { get; } = new() { Dock = DockStyle.Fill, Margin = new Padding(2, 0, 2, 0) };
        public Panel Header { get; } = new() { Dock = DockStyle.Top, Height = 24, BackColor = SystemColors.ControlLight };
        public Label Star { get; } = new() { Text = "★" };
        public Label Code { get; } = new() { Text = "채널코드" };
        public Label Name { get; } = new() { Text = "채널명" };
        public Label LastUsed { get; } = new() { Text = "마지막 사용" };
        public TreeView Tree { get; } = new() { Dock = DockStyle.Fill };
    }

    /// <param name="pinnedGroupName">지정하면 해당 이름의 채널 그룹 폴더를 첫 번째 열 맨 위에 고정하고
    /// 항상 펼쳐진 상태로 유지한다(사용자가 접어도 즉시 다시 펼쳐짐). 특정 창에서 자주 쓰는
    /// 그룹(예: 광고 매핑 창의 "온라인")을 매번 찾아 펼치지 않아도 되게 하기 위함이다.</param>
    public SelectChannelDialog(string? pinnedGroupName = null, SelectChannelDialogStateService? stateService = null)
    {
        _pinnedGroupName = pinnedGroupName;
        _stateService = stateService ?? new SelectChannelDialogStateService();
        RestoreLastState();
        if (_pinnedGroupName != null) _expandedGroupKeys.Add(_pinnedGroupName);
        InitializeComponent();
        LoadChannels();
    }

    /// <summary>지난번에 열었던 위치(펼쳐둔 폴더 + 마지막 선택 채널)를 불러온다.</summary>
    private void RestoreLastState()
    {
        var state = _stateService.Load();
        if (!state.HasState) return;

        // 저장된 상태가 있으면 기본값(즐겨찾기 펼침)이 아니라 그때 접어둔 모습 그대로 재현한다.
        _expandedGroupKeys.Clear();
        foreach (var key in state.ExpandedGroups)
        {
            if (!string.IsNullOrEmpty(key)) _expandedGroupKeys.Add(key);
        }
        _restoreChannelCode = state.LastChannelCode;
    }

    private void SaveLastState()
    {
        _stateService.Save(new SelectChannelDialogState
        {
            ExpandedGroups = _expandedGroupKeys.ToList(),
            LastChannelCode = (CurrentNode?.Tag as SalesChannel)?.ChannelCode ?? _restoreChannelCode,
            HasState = true
        });
    }

    /// <summary>3개 열 중 현재 선택이 살아있는 노드. 선택은 항상 한 열에서만 유지된다.</summary>
    private TreeNode? CurrentNode =>
        _columns.Select(c => c.Tree.SelectedNode).FirstOrDefault(n => n != null);

    private void InitializeComponent()
    {
        Text = "채널 선택";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(1120, 640);
        MinimumSize = new Size(760, 400);

        _boldFont = new Font(Font, FontStyle.Bold);

        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 6, 4) };
        var searchLabel = new Label { Text = "🔍 검색", AutoSize = true, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 4, 6, 0) };
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.TextChanged += (_, _) => ApplyFilter(_searchBox.Text);
        searchPanel.Controls.Add(_searchBox);
        searchPanel.Controls.Add(searchLabel);

        var columnLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = ColumnCount,
            RowCount = 1,
            Padding = new Padding(4, 2, 4, 2)
        };
        for (int i = 0; i < ColumnCount; i++)
        {
            columnLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / ColumnCount));
            var column = CreateColumn();
            _columns[i] = column;
            columnLayout.Controls.Add(column.Container, i, 0);
        }

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 46,
            Padding = new Padding(6)
        };
        var btnOk = new Button { Text = "확인", Width = 80 };
        var btnCancel = new Button { Text = "취소", Width = 80 };
        var btnNewChannel = new Button { Text = "신규 채널 바로 추가...", AutoSize = true };

        btnOk.Click += OnOkClick;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnNewChannel.Click += OnNewChannelClick;

        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);
        buttonPanel.Controls.Add(btnNewChannel);

        // Dock 컨트롤은 나중에 추가된 것이 바깥쪽에 붙으므로 Fill을 먼저 넣는다.
        Controls.Add(columnLayout);
        Controls.Add(searchPanel);
        Controls.Add(buttonPanel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Shown += (_, _) => AlignHeaders();
    }

    private ChannelColumn CreateColumn()
    {
        var column = new ChannelColumn();

        foreach (var lbl in new[] { column.Star, column.Code, column.Name, column.LastUsed })
        {
            lbl.Font = _boldFont;
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            lbl.Height = column.Header.Height;
            column.Header.Controls.Add(lbl);
        }

        var tree = column.Tree;
        tree.HideSelection = false;
        tree.ShowLines = true;
        tree.ItemHeight = 24;
        tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
        tree.DrawNode += OnTreeDrawNode;
        tree.AfterExpand += OnTreeAfterExpand;
        tree.AfterCollapse += OnTreeAfterCollapse;
        tree.AfterSelect += OnTreeAfterSelect;
        tree.NodeMouseDoubleClick += OnTreeNodeDoubleClick;
        tree.KeyDown += OnTreeKeyDown;

        column.Container.Controls.Add(tree);
        column.Container.Controls.Add(column.Header);
        return column;
    }

    private void LoadChannels()
    {
        _allChannels = new SalesChannelRepository().GetAll().ToList();
        BuildTree(null);
    }

    private void ApplyFilter(string? filter) => BuildTree(filter);

    private void BuildTree(string? filter)
    {
        foreach (var column in _columns)
        {
            column.Tree.BeginUpdate();
            column.Tree.Nodes.Clear();
        }

        bool hasFilter = !string.IsNullOrWhiteSpace(filter);
        IEnumerable<SalesChannel> pool = _allChannels;
        if (hasFilter)
        {
            pool = _allChannels.Where(c =>
                (!string.IsNullOrEmpty(c.ChannelCode) && c.ChannelCode.Contains(filter!, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.ChannelName) && c.ChannelName.Contains(filter!, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.GroupName) && c.GroupName.Contains(filter!, StringComparison.OrdinalIgnoreCase)));
        }
        var poolList = pool.ToList();

        var groupNodes = new List<TreeNode>();

        var favorites = poolList.Where(c => c.IsFavorite)
            .OrderByDescending(c => c.LastUsedDate ?? DateTime.MinValue)
            .ThenBy(c => c.ChannelName)
            .ToList();
        if (favorites.Count > 0)
        {
            var favNode = new TreeNode($"⭐ 즐겨찾기 ({favorites.Count})") { Name = FavKey };
            foreach (var ch in favorites) favNode.Nodes.Add(CreateLeafNode(ch));
            groupNodes.Add(favNode);
        }

        var groups = poolList
            .GroupBy(c => string.IsNullOrWhiteSpace(c.GroupName) ? UnclassifiedKey : c.GroupName!)
            .OrderBy(g => g.Key == _pinnedGroupName ? 0 : 1)
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

        foreach (var g in groups)
        {
            var ordered = g.OrderByDescending(c => c.IsFavorite)
                .ThenByDescending(c => c.LastUsedDate ?? DateTime.MinValue)
                .ThenBy(c => c.ChannelName)
                .ToList();
            var groupNode = new TreeNode($"📁 {g.Key} ({ordered.Count})") { Name = g.Key };
            foreach (var ch in ordered) groupNode.Nodes.Add(CreateLeafNode(ch));
            groupNodes.Add(groupNode);
        }

        DistributeToColumns(groupNodes, hasFilter);

        _suppressExpandEvents = true;
        foreach (var column in _columns)
        {
            foreach (TreeNode n in column.Tree.Nodes)
            {
                if (hasFilter || _expandedGroupKeys.Contains(n.Name)) n.Expand();
            }
        }
        _suppressExpandEvents = false;

        SelectDefaultNode(hasFilter);

        foreach (var column in _columns) column.Tree.EndUpdate();

        AlignHeaders();
    }

    /// <summary>
    /// 그룹 폴더들을 3열로 나눠 담는다. 신문 단처럼 순서는 그대로 두고(1열 → 2열 → 3열),
    /// 각 열이 차지하는 줄 수(폴더 1줄 + 펼쳐져 있으면 그 안의 채널 줄 수)가 비슷해지는
    /// 지점에서 다음 열로 넘긴다. 접고 펴는 것만으로 열 구성이 요동치지 않도록, 재배치는
    /// 트리를 다시 그릴 때(창을 열 때/검색어가 바뀔 때)만 한다.
    /// </summary>
    private void DistributeToColumns(List<TreeNode> groupNodes, bool hasFilter)
    {
        int Weight(TreeNode n) => 1 + (hasFilter || _expandedGroupKeys.Contains(n.Name) ? n.Nodes.Count : 0);

        int totalWeight = groupNodes.Sum(Weight);
        int target = Math.Max(1, (int)Math.Ceiling(totalWeight / (double)ColumnCount));

        int col = 0;
        int colWeight = 0;
        for (int i = 0; i < groupNodes.Count; i++)
        {
            var node = groupNodes[i];
            int weight = Weight(node);
            int remainingNodes = groupNodes.Count - i;
            int remainingCols = ColumnCount - col;

            // 이 그룹의 절반 이상이 목표치를 넘치면 다음 열로 넘긴다(절반 기준이라 목표치를
            // 살짝 넘는 그룹은 그대로 두고, 크게 넘치는 그룹만 다음 열에서 시작한다).
            bool overflow = colWeight > 0 && colWeight + (weight / 2) > target;
            // 남은 그룹 수가 남은 열 수 이하이면, 빈 열이 생기지 않도록 한 열에 하나씩 배치한다.
            bool needSpread = colWeight > 0 && remainingNodes <= remainingCols;
            if (col < ColumnCount - 1 && (overflow || needSpread))
            {
                col++;
                colWeight = 0;
            }

            _columns[col].Tree.Nodes.Add(node);
            colWeight += weight;
        }
    }

    private TreeNode CreateLeafNode(SalesChannel ch)
    {
        // TreeView는 owner-draw 모드에서도 노드 Bounds를 Text의 렌더링 폭으로 계산하므로,
        // 별점/코드/이름/최근사용일까지 커버하도록 Text를 공백으로 패딩해 잘림·잔상을 방지한다.
        var display = $"{ch.ChannelCode} {ch.ChannelName}";
        var padded = PadForLeafWidth(display);
        return new TreeNode(padded) { Tag = ch };
    }

    private string PadForLeafWidth(string text)
    {
        int width = TextRenderer.MeasureText(text, Font).Width;
        if (width >= LeafRowWidth) return text;

        int spaceWidth = Math.Max(1, TextRenderer.MeasureText(" ", Font).Width);
        int extraSpaces = (LeafRowWidth - width) / spaceWidth + 4;
        return text + new string(' ', extraSpaces);
    }

    private void SelectDefaultNode(bool hasFilter)
    {
        TreeNode? target;
        if (hasFilter)
        {
            target = AllGroupNodes().FirstOrDefault()?.Nodes.Cast<TreeNode>().FirstOrDefault();
        }
        else if (!_restoreApplied && FindLeafNode(_restoreChannelCode) is { } restored)
        {
            // 창을 처음 열 때 한 번만 지난번 위치로 복원한다. 이후 검색어를 지워 다시 그릴 때는
            // 사용자가 그 사이에 고른 채널을 덮어쓰지 않도록 기본 선택 규칙으로 돌아간다.
            _restoreApplied = true;
            ExpandGroupNode(restored.Parent);
            target = restored;
        }
        else
        {
            var favNode = AllGroupNodes().FirstOrDefault(n => n.Name == FavKey);
            if (favNode != null && favNode.Nodes.Count > 0)
            {
                target = favNode.Nodes[0];
            }
            else
            {
                var best = _allChannels.OrderByDescending(c => c.LastUsedDate ?? DateTime.MinValue).FirstOrDefault();
                target = null;
                if (best != null)
                {
                    var groupKey = string.IsNullOrWhiteSpace(best.GroupName) ? UnclassifiedKey : best.GroupName!;
                    var groupNode = AllGroupNodes().FirstOrDefault(n => n.Name == groupKey);
                    if (groupNode != null)
                    {
                        target = groupNode.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Tag is SalesChannel c && c.ChannelCode == best.ChannelCode);
                        if (target != null)
                        {
                            _suppressExpandEvents = true;
                            groupNode.Expand();
                            _expandedGroupKeys.Add(groupKey);
                            _suppressExpandEvents = false;
                        }
                    }
                }
            }
        }

        if (target != null)
        {
            SelectNode(target);
            target.EnsureVisible();
        }
    }

    /// <summary>세 열의 모든 그룹(최상위) 노드를 1열 → 2열 → 3열 순서로 훑는다.</summary>
    private IEnumerable<TreeNode> AllGroupNodes() =>
        _columns.SelectMany(c => c.Tree.Nodes.Cast<TreeNode>());

    /// <summary>채널 코드로 리프 노드를 찾는다. 즐겨찾기 폴더에도 같은 채널이 중복 표시되므로,
    /// 이미 펼쳐져 있는 폴더 안의 노드를 우선해 지난번에 보던 위치를 그대로 재현한다.</summary>
    private TreeNode? FindLeafNode(string? channelCode)
    {
        if (string.IsNullOrEmpty(channelCode)) return null;

        var matches = AllGroupNodes()
            .SelectMany(group => group.Nodes.Cast<TreeNode>())
            .Where(n => n.Tag is SalesChannel c && c.ChannelCode == channelCode)
            .ToList();

        return matches.FirstOrDefault(n => _expandedGroupKeys.Contains(n.Parent.Name)) ?? matches.FirstOrDefault();
    }

    private void ExpandGroupNode(TreeNode? groupNode)
    {
        if (groupNode == null || groupNode.IsExpanded) return;

        _suppressExpandEvents = true;
        groupNode.Expand();
        _expandedGroupKeys.Add(groupNode.Name);
        _suppressExpandEvents = false;
    }

    private void SelectNode(TreeNode node)
    {
        if (node.TreeView != null) node.TreeView.SelectedNode = node;
    }

    /// <summary>
    /// 선택은 항상 한 열에만 남긴다. HideSelection=false라 그냥 두면 포커스를 잃은 열에도
    /// 이전 선택이 계속 보여 "지금 무엇을 고른 상태인지" 헷갈리기 때문이다.
    /// </summary>
    private void OnTreeAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (_suppressSelectionSync || e.Node == null) return;

        _suppressSelectionSync = true;
        foreach (var column in _columns)
        {
            if (!ReferenceEquals(column.Tree, sender)) column.Tree.SelectedNode = null;
        }
        _suppressSelectionSync = false;
    }

    private void AlignHeaders()
    {
        foreach (var column in _columns) AlignHeader(column);
    }

    private void AlignHeader(ChannelColumn column)
    {
        // 리프 노드(채널)의 실제 들여쓰기 위치를 측정해 헤더 라벨을 그 위치에 맞춘다.
        var firstGroup = column.Tree.Nodes.Cast<TreeNode>().FirstOrDefault();
        if (firstGroup == null)
        {
            // 그룹이 하나도 없는 열(그룹 수가 3개 미만이거나 검색 결과가 좁을 때)은 통째로 감춰
            // 빈 흰 상자가 남지 않게 한다.
            column.Container.Visible = false;
            return;
        }
        column.Container.Visible = true;
        if (!IsHandleCreated) return;

        bool wasCollapsed = !firstGroup.IsExpanded;
        _suppressExpandEvents = true;
        if (wasCollapsed) firstGroup.Expand();

        int leafLeft = firstGroup.Nodes.Count > 0 && firstGroup.Nodes[0].Bounds.Left > 0
            ? firstGroup.Nodes[0].Bounds.Left
            : 40;

        if (wasCollapsed && !_expandedGroupKeys.Contains(firstGroup.Name)) firstGroup.Collapse();
        _suppressExpandEvents = false;

        column.Star.Left = leafLeft;
        column.Star.Width = ColStarW;
        column.Code.Left = leafLeft + ColStarW;
        column.Code.Width = ColCodeW;
        column.Name.Left = leafLeft + ColStarW + ColCodeW;
        column.Name.Width = ColNameW;
        column.LastUsed.Left = leafLeft + ColStarW + ColCodeW + ColNameW;
        column.LastUsed.Width = Math.Max(60, column.Header.Width - column.LastUsed.Left);
    }

    private void OnTreeDrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        e.DrawDefault = false;
        var g = e.Graphics;
        var tree = (TreeView)sender!;
        bool selected = e.State.HasFlag(TreeNodeStates.Selected);
        var textColor = selected ? SystemColors.HighlightText : SystemColors.WindowText;
        var font = tree.Font;

        // OwnerDrawText 모드에서는 배경도 직접 지워야 한다. 그러지 않으면 선택/재도색 시
        // 이전 프레임 픽셀 위에 겹쳐 그려져 글자가 깨진 것처럼 보인다.
        var bgColor = selected ? SystemColors.Highlight : tree.BackColor;
        using (var bgBrush = new SolidBrush(bgColor))
            g.FillRectangle(bgBrush, e.Bounds);

        if (e.Node.Tag is SalesChannel ch)
        {
            int x = e.Bounds.Left;
            int y = e.Bounds.Top;
            int h = e.Bounds.Height;

            if (ch.IsFavorite)
            {
                var starColor = selected ? textColor : Color.Goldenrod;
                TextRenderer.DrawText(g, "★", font, new Rectangle(x, y, ColStarW, h), starColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            x += ColStarW;

            TextRenderer.DrawText(g, ch.ChannelCode, font, new Rectangle(x, y, ColCodeW, h), textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            x += ColCodeW;

            TextRenderer.DrawText(g, ch.ChannelName, font, new Rectangle(x, y, ColNameW, h), textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            x += ColNameW;

            var lastUsed = ch.LastUsedDate.HasValue ? ch.LastUsedDate.Value.ToString("yyyy-MM-dd") : "-";
            var dateColor = selected ? textColor : Color.Gray;
            TextRenderer.DrawText(g, lastUsed, font, new Rectangle(x, y, ColLastUsedW, h), dateColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        else
        {
            TextRenderer.DrawText(g, e.Node.Text, _boldFont, e.Bounds, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    private void OnTreeAfterExpand(object? sender, TreeViewEventArgs e)
    {
        if (_suppressExpandEvents || e.Node.Parent != null) return;
        _expandedGroupKeys.Add(e.Node.Name);
    }

    private void OnTreeAfterCollapse(object? sender, TreeViewEventArgs e)
    {
        if (_suppressExpandEvents || e.Node.Parent != null) return;
        if (e.Node.Name == _pinnedGroupName)
        {
            // 고정 그룹은 "열린 상태로 고정"이 요구사항이라 접히는 즉시 다시 펼친다.
            _suppressExpandEvents = true;
            e.Node.Expand();
            _suppressExpandEvents = false;
            return;
        }
        _expandedGroupKeys.Remove(e.Node.Name);
    }

    private void OnTreeNodeDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node.Tag is SalesChannel) ConfirmSelection();
        else e.Node.Toggle();
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        var node = (sender as TreeView)?.SelectedNode;
        if (node == null) return;
        if (node.Tag is SalesChannel) ConfirmSelection();
        else node.Toggle();
        e.Handled = true;
    }

    private void OnOkClick(object? sender, EventArgs e) => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (CurrentNode?.Tag is not SalesChannel ch) return;
        SelectedChannel = ch;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnNewChannelClick(object? sender, EventArgs e)
    {
        var configForm = Application.OpenForms.OfType<ChannelConfigForm>().FirstOrDefault() ?? new ChannelConfigForm();
        if (!configForm.Visible) configForm.Show();
        configForm.BringToFront();

        // 채널 설정 창에서 채널을 추가/설정하고 돌아오면 다시 시도할 수 있도록 이 다이얼로그는 닫는다.
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 확인/취소 어느 쪽으로 닫혀도 폴더를 펼쳐둔 모습과 마지막으로 본 채널은 남긴다.
        SaveLastState();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _boldFont?.Dispose();
        base.Dispose(disposing);
    }
}

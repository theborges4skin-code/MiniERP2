using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 파일 로드 시 사용할 채널을 선택하는 다이얼로그입니다.
/// 그룹별 폴더 트리 + 즐겨찾기 폴더 + 검색창으로 채널이 많아져도 빠르게 찾을 수 있게 합니다.
/// </summary>
public class SelectChannelDialog : Form
{
    private const string FavKey = "__FAV__";
    private const string UnclassifiedKey = "(미분류)";
    private const int ColStarW = 24;
    private const int ColCodeW = 110;
    private const int ColNameW = 190;
    private const int ColLastUsedW = 100;
    private const int LeafRowWidth = ColStarW + ColCodeW + ColNameW + ColLastUsedW;

    private readonly TextBox _searchBox = new();
    private readonly Panel _headerPanel = new();
    private readonly Label _hdrStar = new();
    private readonly Label _hdrCode = new();
    private readonly Label _hdrName = new();
    private readonly Label _hdrLastUsed = new();
    private readonly TreeView _tree = new();
    private Font? _boldFont;

    private List<SalesChannel> _allChannels = new();
    private readonly HashSet<string> _expandedGroupKeys = new() { FavKey };
    private readonly string? _pinnedGroupName;
    private bool _suppressExpandEvents;

    public SalesChannel? SelectedChannel { get; private set; }

    /// <param name="pinnedGroupName">지정하면 해당 이름의 채널 그룹 폴더를 트리 맨 위에 고정하고
    /// 항상 펼쳐진 상태로 유지한다(사용자가 접어도 즉시 다시 펼쳐짐). 특정 창에서 자주 쓰는
    /// 그룹(예: 광고 매핑 창의 "온라인")을 매번 찾아 펼치지 않아도 되게 하기 위함이다.</param>
    public SelectChannelDialog(string? pinnedGroupName = null)
    {
        _pinnedGroupName = pinnedGroupName;
        if (_pinnedGroupName != null) _expandedGroupKeys.Add(_pinnedGroupName);
        InitializeComponent();
        LoadChannels();
    }

    private void InitializeComponent()
    {
        Text = "채널 선택";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(580, 520);
        MinimumSize = new Size(420, 340);

        _boldFont = new Font(Font, FontStyle.Bold);

        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(6, 4, 6, 4) };
        var searchLabel = new Label { Text = "🔍 검색", AutoSize = true, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 4, 6, 0) };
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.TextChanged += (_, _) => ApplyFilter(_searchBox.Text);
        searchPanel.Controls.Add(_searchBox);
        searchPanel.Controls.Add(searchLabel);

        _headerPanel.Dock = DockStyle.Top;
        _headerPanel.Height = 24;
        _headerPanel.BackColor = SystemColors.ControlLight;
        _hdrStar.Text = "★";
        _hdrCode.Text = "채널코드";
        _hdrName.Text = "채널명";
        _hdrLastUsed.Text = "마지막 사용";
        foreach (var lbl in new[] { _hdrStar, _hdrCode, _hdrName, _hdrLastUsed })
        {
            lbl.Font = _boldFont;
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            lbl.Height = _headerPanel.Height;
        }
        _headerPanel.Controls.Add(_hdrStar);
        _headerPanel.Controls.Add(_hdrCode);
        _headerPanel.Controls.Add(_hdrName);
        _headerPanel.Controls.Add(_hdrLastUsed);

        _tree.Dock = DockStyle.Fill;
        _tree.HideSelection = false;
        _tree.ShowLines = true;
        _tree.ItemHeight = 24;
        _tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
        _tree.DrawNode += OnTreeDrawNode;
        _tree.AfterExpand += OnTreeAfterExpand;
        _tree.AfterCollapse += OnTreeAfterCollapse;
        _tree.NodeMouseDoubleClick += OnTreeNodeDoubleClick;
        _tree.KeyDown += OnTreeKeyDown;

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

        Controls.Add(searchPanel);
        Controls.Add(_headerPanel);
        Controls.Add(_tree);
        Controls.Add(buttonPanel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Shown += (_, _) => AlignHeader();
    }

    private void LoadChannels()
    {
        _allChannels = new SalesChannelRepository().GetAll().ToList();
        BuildTree(null);
    }

    private void ApplyFilter(string? filter) => BuildTree(filter);

    private void BuildTree(string? filter)
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();

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

        var favorites = poolList.Where(c => c.IsFavorite)
            .OrderByDescending(c => c.LastUsedDate ?? DateTime.MinValue)
            .ThenBy(c => c.ChannelName)
            .ToList();
        if (favorites.Count > 0)
        {
            var favNode = new TreeNode($"⭐ 즐겨찾기 ({favorites.Count})") { Name = FavKey };
            foreach (var ch in favorites) favNode.Nodes.Add(CreateLeafNode(ch));
            _tree.Nodes.Add(favNode);
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
            _tree.Nodes.Add(groupNode);
        }

        _suppressExpandEvents = true;
        foreach (TreeNode n in _tree.Nodes)
        {
            if (hasFilter || _expandedGroupKeys.Contains(n.Name)) n.Expand();
        }
        _suppressExpandEvents = false;

        SelectDefaultNode(hasFilter);

        _tree.EndUpdate();

        AlignHeader();
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
            target = _tree.Nodes.Cast<TreeNode>().FirstOrDefault()?.Nodes.Cast<TreeNode>().FirstOrDefault();
        }
        else
        {
            var favNode = _tree.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Name == FavKey);
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
                    var groupNode = _tree.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Name == groupKey);
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
            _tree.SelectedNode = target;
            target.EnsureVisible();
        }
    }

    private void AlignHeader()
    {
        // 리프 노드(채널)의 실제 들여쓰기 위치를 측정해 헤더 라벨을 그 위치에 맞춘다.
        var firstGroup = _tree.Nodes.Cast<TreeNode>().FirstOrDefault();
        if (firstGroup == null || !IsHandleCreated) return;

        bool wasCollapsed = !firstGroup.IsExpanded;
        _suppressExpandEvents = true;
        if (wasCollapsed) firstGroup.Expand();

        int leafLeft = firstGroup.Nodes.Count > 0 && firstGroup.Nodes[0].Bounds.Left > 0
            ? firstGroup.Nodes[0].Bounds.Left
            : 40;

        if (wasCollapsed && !_expandedGroupKeys.Contains(firstGroup.Name)) firstGroup.Collapse();
        _suppressExpandEvents = false;

        _hdrStar.Left = leafLeft;
        _hdrStar.Width = ColStarW;
        _hdrCode.Left = leafLeft + ColStarW;
        _hdrCode.Width = ColCodeW;
        _hdrName.Left = leafLeft + ColStarW + ColCodeW;
        _hdrName.Width = ColNameW;
        _hdrLastUsed.Left = leafLeft + ColStarW + ColCodeW + ColNameW;
        _hdrLastUsed.Width = Math.Max(80, _headerPanel.Width - _hdrLastUsed.Left);
    }

    private void OnTreeDrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        e.DrawDefault = false;
        var g = e.Graphics;
        bool selected = e.State.HasFlag(TreeNodeStates.Selected);
        var textColor = selected ? SystemColors.HighlightText : SystemColors.WindowText;
        var font = _tree.Font;

        // OwnerDrawText 모드에서는 배경도 직접 지워야 한다. 그러지 않으면 선택/재도색 시
        // 이전 프레임 픽셀 위에 겹쳐 그려져 글자가 깨진 것처럼 보인다.
        var bgColor = selected ? SystemColors.Highlight : _tree.BackColor;
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
        var node = _tree.SelectedNode;
        if (node == null) return;
        if (node.Tag is SalesChannel) ConfirmSelection();
        else node.Toggle();
        e.Handled = true;
    }

    private void OnOkClick(object? sender, EventArgs e) => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (_tree.SelectedNode?.Tag is not SalesChannel ch) return;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) _boldFont?.Dispose();
        base.Dispose(disposing);
    }
}

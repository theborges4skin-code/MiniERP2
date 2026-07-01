using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 파일 로드 시 사용할 채널을 선택하는 다이얼로그입니다.
/// </summary>
public class SelectChannelDialog : Form
{
    private readonly DataGridView _channelGrid = new();
    public SalesChannel? SelectedChannel { get; private set; }

    public SelectChannelDialog()
    {
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
        Size = new Size(580, 460);
        MinimumSize = new Size(420, 300);

        _channelGrid.Dock = DockStyle.Fill;
        _channelGrid.ReadOnly = true;
        _channelGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _channelGrid.MultiSelect = false;
        _channelGrid.AllowUserToAddRows = false;
        _channelGrid.AllowUserToDeleteRows = false;
        _channelGrid.AllowUserToResizeRows = false;
        _channelGrid.RowHeadersVisible = false;
        _channelGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _channelGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _channelGrid.CellDoubleClick += OnGridCellDoubleClick;
        _channelGrid.KeyDown += OnGridKeyDown;
        _channelGrid.ColumnHeaderMouseClick += OnColumnHeaderMouseClick;

        _channelGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "GroupName",    HeaderText = "그룹",       FillWeight = 20 });
        _channelGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ChannelCode",  HeaderText = "채널코드",   FillWeight = 20 });
        _channelGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ChannelName",  HeaderText = "채널명",     FillWeight = 35 });
        _channelGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LastUsedDate", HeaderText = "마지막 사용", FillWeight = 25 });

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

        Controls.Add(_channelGrid);
        Controls.Add(buttonPanel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void LoadChannels()
    {
        var channels = new SalesChannelRepository().GetAll()
            .OrderBy(c => c.GroupName ?? "")
            .ThenByDescending(c => c.LastUsedDate ?? DateTime.MinValue)
            .ThenBy(c => c.ChannelName)
            .ToList();

        _channelGrid.Rows.Clear();

        foreach (var ch in channels)
        {
            var lastUsed = ch.LastUsedDate.HasValue ? ch.LastUsedDate.Value.ToString("yyyy-MM-dd") : "-";
            int idx = _channelGrid.Rows.Add(ch.GroupName ?? "", ch.ChannelCode, ch.ChannelName, lastUsed);
            _channelGrid.Rows[idx].Tag = ch;
        }

        if (_channelGrid.Rows.Count > 0)
            _channelGrid.Rows[0].Selected = true;
    }

    private void OnColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var col = _channelGrid.Columns[e.ColumnIndex];
        var ascending = col.HeaderCell.SortGlyphDirection != SortOrder.Ascending;
        var propName = col.Name;

        var rows = _channelGrid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is SalesChannel).ToList();
        var channels = rows.Select(r => (SalesChannel)r.Tag!).ToList();

        IOrderedEnumerable<SalesChannel> sorted = propName switch
        {
            "ChannelCode" => ascending ? channels.OrderBy(c => c.ChannelCode) : channels.OrderByDescending(c => c.ChannelCode),
            "ChannelName" => ascending ? channels.OrderBy(c => c.ChannelName) : channels.OrderByDescending(c => c.ChannelName),
            "LastUsedDate" => ascending
                ? channels.OrderBy(c => c.LastUsedDate ?? DateTime.MinValue)
                : channels.OrderByDescending(c => c.LastUsedDate ?? DateTime.MinValue),
            _ => ascending ? channels.OrderBy(c => c.GroupName ?? "") : channels.OrderByDescending(c => c.GroupName ?? ""),
        };

        _channelGrid.Rows.Clear();
        foreach (var ch in sorted)
        {
            var lastUsed = ch.LastUsedDate.HasValue ? ch.LastUsedDate.Value.ToString("yyyy-MM-dd") : "-";
            int idx = _channelGrid.Rows.Add(ch.GroupName ?? "", ch.ChannelCode, ch.ChannelName, lastUsed);
            _channelGrid.Rows[idx].Tag = ch;
        }

        if (_channelGrid.Rows.Count > 0)
            _channelGrid.Rows[0].Selected = true;

        foreach (DataGridViewColumn c in _channelGrid.Columns)
            c.HeaderCell.SortGlyphDirection = SortOrder.None;
        col.HeaderCell.SortGlyphDirection = ascending ? SortOrder.Ascending : SortOrder.Descending;
    }

    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        ConfirmSelection();
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ConfirmSelection();
            e.Handled = true;
        }
    }

    private void OnOkClick(object? sender, EventArgs e) => ConfirmSelection();

    private void ConfirmSelection()
    {
        if (_channelGrid.SelectedRows.Count == 0) return;
        SelectedChannel = _channelGrid.SelectedRows[0].Tag as SalesChannel;
        if (SelectedChannel == null) return;
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
}

using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 배송지 주소록 관리 화면(배송지주소록_개발기획서_확정본.md §4.1, M1). CourierConfigForm과 같은
/// 마스터-디테일 골격 — 왼쪽에서 주소를 고르고 오른쪽에서 편집한 뒤 "저장"을 누르면 그 레코드
/// 1건만 즉시 커밋된다(DataManagementForm의 여러 행 일괄 스테이징 방식과 달리). 채널 태그는
/// AddressChannelTagTable에 AddressId로 연결되는 자식 레코드라, 신규 주소가 아직 AddressId를
/// 받기 전(저장 전)에는 태그를 미리 쓸 수 없다 — 그래서 "저장" 시점에 주소 upsert와 태그
/// 재작성을 한 트랜잭션으로 같이 처리한다(AddressBookRepository.Upsert 참고).
/// </summary>
public class AddressBookForm : Form
{
    private readonly AddressBookRepository _addressBookRepository = new();
    private List<AddressBookEntry> _entries = new();
    private List<SalesChannel> _channels = new();
    private int _selectedAddressId;

    private ListBox _addressListBox = new();
    private TextBox _txtLabel = new();
    private TextBox _txtReceiverName = new();
    private TextBox _txtPhone = new();
    private TextBox _txtAddress = new();
    private TextBox _txtMemo = new();
    private CheckBox _chkIsActive = new();
    private NumericUpDown _numDisplayOrder = new();
    private CheckedListBox _channelTagsList = new();
    private Label _statusLabel = new();

    public AddressBookForm()
    {
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
        LoadChannels();
        LoadEntries();
        ResetDetailForNew();
    }

    private void InitializeComponent()
    {
        Text = "배송지 주소록 관리";
        Size = new Size(900, 640);
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // 좌측: 주소 목록
        var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _addressListBox = new ListBox { Dock = DockStyle.Fill, DisplayMember = "Label" };
        _addressListBox.SelectedIndexChanged += OnAddressSelected;

        // 버튼 3개(추가/복사등록/삭제)가 한 줄에 들어가야 한다 — 좌측 열 250px 기준으로 폭·여백을 맞췄다.
        var leftButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 5, 0, 0) };
        var btnAdd = new Button { Text = "추가", Width = 72, Margin = new Padding(0, 3, 4, 3) };
        var btnCopy = new Button { Text = "복사등록", Width = 72, Margin = new Padding(0, 3, 4, 3) };
        var btnDelete = new Button { Text = "삭제", Width = 72, Margin = new Padding(0, 3, 4, 3) };
        btnAdd.Click += (s, e) => ResetDetailForNew();
        btnCopy.Click += OnCopyClick;
        btnDelete.Click += OnDeleteClick;
        leftButtonPanel.Controls.Add(btnAdd);
        leftButtonPanel.Controls.Add(btnCopy);
        leftButtonPanel.Controls.Add(btnDelete);

        leftPanel.Controls.Add(_addressListBox, 0, 0);
        leftPanel.Controls.Add(leftButtonPanel, 0, 1);

        // 우측: 선택한 주소 편집
        var rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, Padding = new Padding(10) };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var labelPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        labelPanel.Controls.Add(new Label { Text = "라벨(표시명):", AutoSize = true, Padding = new Padding(0, 6, 5, 0) });
        _txtLabel = new TextBox { Width = 300 };
        labelPanel.Controls.Add(_txtLabel);

        var receiverPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        receiverPanel.Controls.Add(new Label { Text = "수취인:", AutoSize = true, Padding = new Padding(0, 6, 5, 0) });
        _txtReceiverName = new TextBox { Width = 150 };
        receiverPanel.Controls.Add(_txtReceiverName);
        receiverPanel.Controls.Add(new Label { Text = "연락처:", AutoSize = true, Padding = new Padding(12, 6, 5, 0) });
        _txtPhone = new TextBox { Width = 150 };
        receiverPanel.Controls.Add(_txtPhone);

        // 주소/메모는 내용이 길어 FlowLayoutPanel의 고정 폭으로는 오른쪽이 잘린다 — 라벨만 AutoSize로 두고
        // 입력칸이 남는 폭을 모두 채우도록 TableLayoutPanel로 깔아 창 크기에 따라 같이 늘어나게 한다.
        var addressPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        addressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        addressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        addressPanel.Controls.Add(new Label { Text = "주소:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 5, 0) }, 0, 0);
        _txtAddress = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 5, 3) };
        addressPanel.Controls.Add(_txtAddress, 1, 0);

        var memoPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        memoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        memoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        memoPanel.Controls.Add(new Label { Text = "메모:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 5, 0) }, 0, 0);
        _txtMemo = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Margin = new Padding(0, 3, 5, 3) };
        memoPanel.Controls.Add(_txtMemo, 1, 0);

        var flagsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        _chkIsActive = new CheckBox { Text = "활성", AutoSize = true, Checked = true, Padding = new Padding(0, 6, 0, 0) };
        flagsPanel.Controls.Add(_chkIsActive);
        flagsPanel.Controls.Add(new Label { Text = "표시순서:", AutoSize = true, Padding = new Padding(12, 6, 5, 0) });
        _numDisplayOrder = new NumericUpDown { Minimum = 0, Maximum = 9999, Width = 70 };
        flagsPanel.Controls.Add(_numDisplayOrder);

        var tagsGroup = new GroupBox { Text = "채널 태그 (선택한 채널을 OFS \"배송지 불러오기\"에서 우선 노출 — 비워두면 항상 전체 노출)", Dock = DockStyle.Fill };
        _channelTagsList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        tagsGroup.Controls.Add(_channelTagsList);

        var saveButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnSave = new Button { Text = "저장", Width = 90 };
        btnSave.Click += OnSaveClick;
        saveButtonPanel.Controls.Add(btnSave);
        _statusLabel = new Label { AutoSize = true, Padding = new Padding(0, 7, 10, 0), ForeColor = Color.DarkGreen };
        saveButtonPanel.Controls.Add(_statusLabel);

        rightPanel.Controls.Add(labelPanel, 0, 0);
        rightPanel.Controls.Add(receiverPanel, 0, 1);
        rightPanel.Controls.Add(addressPanel, 0, 2);
        rightPanel.Controls.Add(memoPanel, 0, 3);
        rightPanel.Controls.Add(flagsPanel, 0, 4);
        rightPanel.Controls.Add(tagsGroup, 0, 5);
        rightPanel.Controls.Add(saveButtonPanel, 0, 6);

        mainLayout.Controls.Add(leftPanel, 0, 0);
        mainLayout.Controls.Add(rightPanel, 1, 0);
        Controls.Add(mainLayout);
    }

    private void LoadChannels()
    {
        _channels = new SalesChannelRepository().GetAll();
        _channelTagsList.Items.Clear();
        foreach (var channel in _channels) _channelTagsList.Items.Add(channel.ChannelName);
    }

    private void LoadEntries()
    {
        _entries = _addressBookRepository.GetAll();
        _addressListBox.DataSource = null;
        _addressListBox.DataSource = _entries;
    }

    private void OnAddressSelected(object? sender, EventArgs e)
    {
        if (_addressListBox.SelectedItem is not AddressBookEntry entry) return;

        _selectedAddressId = entry.AddressId;
        _txtLabel.Text = entry.Label;
        _txtReceiverName.Text = entry.ReceiverName;
        _txtPhone.Text = entry.Phone;
        _txtAddress.Text = entry.Address;
        _txtMemo.Text = entry.Memo;
        _chkIsActive.Checked = entry.IsActive;
        _numDisplayOrder.Value = Math.Clamp(entry.DisplayOrder, (int)_numDisplayOrder.Minimum, (int)_numDisplayOrder.Maximum);

        for (int i = 0; i < _channels.Count; i++)
        {
            _channelTagsList.SetItemChecked(i, entry.ChannelTags.Contains(_channels[i].ChannelCode, StringComparer.OrdinalIgnoreCase));
        }
    }

    private void ResetDetailForNew()
    {
        _addressListBox.ClearSelected();
        _selectedAddressId = 0;
        _txtLabel.Text = string.Empty;
        _txtReceiverName.Text = string.Empty;
        _txtPhone.Text = string.Empty;
        _txtAddress.Text = string.Empty;
        _txtMemo.Text = string.Empty;
        _chkIsActive.Checked = true;
        _numDisplayOrder.Value = 0;
        for (int i = 0; i < _channelTagsList.Items.Count; i++) _channelTagsList.SetItemChecked(i, false);
        _txtLabel.Focus();
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        var label = _txtLabel.Text.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            MessageBox.Show("라벨(표시명)을 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var checkedChannelCodes = _channelTagsList.CheckedIndices.Cast<int>()
            .Select(i => _channels[i].ChannelCode)
            .ToList();

        var entry = new AddressBookEntry
        {
            AddressId = _selectedAddressId,
            Label = label,
            ReceiverName = _txtReceiverName.Text.Trim(),
            Phone = _txtPhone.Text.Trim(),
            Address = _txtAddress.Text.Trim(),
            Memo = _txtMemo.Text.Trim(),
            IsActive = _chkIsActive.Checked,
            DisplayOrder = (int)_numDisplayOrder.Value,
            ChannelTags = checkedChannelCodes,
        };

        _addressBookRepository.Upsert(entry);
        LoadEntries();

        var saved = _entries.FirstOrDefault(a => a.AddressId == entry.AddressId);
        if (saved != null) _addressListBox.SelectedItem = saved;

        _statusLabel.ForeColor = Color.DarkGreen;
        _statusLabel.Text = $"저장되었습니다. ({DateTime.Now:HH:mm:ss})";
    }

    /// <summary>
    /// 기존 배송지의 모든 필드(채널 태그 포함)를 그대로 복제해 새 라벨로 저장한다.
    /// ChannelConfigForm의 "새 채널 추가 시 기존 채널 설정 복사"와 같은 형식의 다이얼로그.
    /// </summary>
    private void OnCopyClick(object? sender, EventArgs e)
    {
        if (_entries.Count == 0)
        {
            MessageBox.Show("복사할 배송지가 없습니다. 먼저 배송지를 추가하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var currentSelection = _addressListBox.SelectedItem as AddressBookEntry;
        using var dialog = new AddressBookCopyDialog(_entries, currentSelection);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) return;

        var source = dialog.SourceEntry;
        if (source == null) return;

        var copy = new AddressBookEntry
        {
            AddressId = 0,
            Label = dialog.NewLabel.Trim(),
            ReceiverName = source.ReceiverName,
            Phone = source.Phone,
            Address = source.Address,
            Memo = source.Memo,
            IsActive = source.IsActive,
            DisplayOrder = source.DisplayOrder,
            ChannelTags = new List<string>(source.ChannelTags),
        };

        var inserted = _addressBookRepository.Upsert(copy);
        LoadEntries();

        var saved = _entries.FirstOrDefault(a => a.AddressId == inserted.AddressId);
        if (saved != null)
        {
            _addressListBox.SelectedItem = saved;
        }

        _statusLabel.ForeColor = Color.DarkGreen;
        _statusLabel.Text = $"복사되었습니다. ({DateTime.Now:HH:mm:ss})";
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        if (_addressListBox.SelectedItem is not AddressBookEntry entry) return;

        var result = MessageBox.Show($"배송지 '{entry.Label}'을(를) 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        _addressBookRepository.Delete(entry.AddressId);
        LoadEntries();
        ResetDetailForNew();
    }
}

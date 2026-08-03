using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// DocPartyTable에 저장된 거래처(공급자/공급받는자) 프로필을 한눈에 보고
/// 추가·수정·삭제·기본 공급자 지정을 할 수 있는 목록 관리 창.
/// </summary>
public class PartyManagerForm : Form
{
    private readonly DocPartyRepository _partyRepo = new();
    private readonly SalesChannelRepository _channelRepo = new();
    private DataGridView _grid = new();
    private List<DocParty> _parties = new();

    public PartyManagerForm()
    {
        InitializeComponent();
        LoadData();
    }

    private void InitializeComponent()
    {
        Text = "거래처 관리";
        Size = new Size(780, 480);
        MinimumSize = new Size(600, 360);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _grid.Columns.Add("ProfileName", "프로필명");
        _grid.Columns.Add("CompanyName", "상호");
        _grid.Columns.Add("CeoName", "대표자");
        _grid.Columns.Add("RegNo", "등록번호");
        _grid.Columns.Add("Tel", "전화");
        _grid.Columns.Add("ChannelName", "연결 채널");
        _grid.Columns.Add("IsDefaultSupplier", "기본 공급자");
        _grid.Columns.Add("HasStamp", "기본 도장");
        _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditSelected(); };

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var btnAdd = new Button { Text = "추가", Width = 80 };
        var btnEdit = new Button { Text = "수정", Width = 80 };
        var btnDelete = new Button { Text = "삭제", Width = 80 };
        var btnSetDefault = new Button { Text = "기본 공급자로 설정", Width = 140 };
        var btnClose = new Button { Text = "닫기", Width = 80 };
        btnAdd.Click += (s, e) => AddNew();
        btnEdit.Click += (s, e) => EditSelected();
        btnDelete.Click += (s, e) => DeleteSelected();
        btnSetDefault.Click += (s, e) => SetSelectedAsDefault();
        btnClose.Click += (s, e) => Close();
        buttonPanel.Controls.AddRange(new Control[] { btnAdd, btnEdit, btnDelete, btnSetDefault, btnClose });

        layout.Controls.Add(_grid, 0, 0);
        layout.Controls.Add(buttonPanel, 0, 1);
        Controls.Add(layout);
    }

    private void LoadData()
    {
        _parties = _partyRepo.GetAll();
        var channelNames = _channelRepo.GetAll().ToDictionary(c => c.ChannelCode, c => c.ChannelName);

        _grid.Rows.Clear();
        foreach (var p in _parties)
        {
            string channelLabel = string.IsNullOrWhiteSpace(p.ChannelCode)
                ? ""
                : (channelNames.TryGetValue(p.ChannelCode, out var name) ? name : p.ChannelCode);
            _grid.Rows.Add(p.ProfileName, p.CompanyName, p.CeoName, p.RegNo, p.Tel, channelLabel, p.IsDefaultSupplier ? "✓" : "", string.IsNullOrWhiteSpace(p.StampImagePath) ? "" : "✓");
        }
    }

    private DocParty? SelectedParty()
    {
        int idx = _grid.CurrentRow?.Index ?? -1;
        return idx >= 0 && idx < _parties.Count ? _parties[idx] : null;
    }

    private void AddNew()
    {
        using var dlg = new PartyEditDialog(null);
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK || dlg.Result == null) return;
        _partyRepo.Save(dlg.Result);
        LoadData();
    }

    private void EditSelected()
    {
        var party = SelectedParty();
        if (party == null) { MessageBox.Show("수정할 거래처를 선택하세요.", "알림"); return; }
        using var dlg = new PartyEditDialog(party);
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK || dlg.Result == null) return;
        _partyRepo.Save(dlg.Result);
        LoadData();
    }

    private void DeleteSelected()
    {
        var party = SelectedParty();
        if (party == null) { MessageBox.Show("삭제할 거래처를 선택하세요.", "알림"); return; }
        if (MessageBox.Show($"'{party.CompanyName}'을(를) 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _partyRepo.Delete(party.Id);
        LoadData();
    }

    private void SetSelectedAsDefault()
    {
        var party = SelectedParty();
        if (party == null) { MessageBox.Show("기본 공급자로 지정할 거래처를 선택하세요.", "알림"); return; }
        _partyRepo.SetDefaultSupplier(party.Id);
        LoadData();
    }
}

/// <summary>거래처 1건을 추가/수정하는 편집 다이얼로그.</summary>
internal class PartyEditDialog : Form
{
    public DocParty? Result { get; private set; }

    private readonly int _id;
    private readonly string _channelCode;
    private readonly bool _isDefaultSupplier;
    private string? _stampImagePath;
    private readonly Label _stampLabel = new() { AutoSize = true, Padding = new Padding(6, 6, 0, 0) };

    private readonly TextBox _profileName = new() { Dock = DockStyle.Fill };
    private readonly TextBox _regNo = new() { Dock = DockStyle.Fill };
    private readonly TextBox _company = new() { Dock = DockStyle.Fill };
    private readonly TextBox _ceo = new() { Dock = DockStyle.Fill };
    private readonly TextBox _address = new() { Dock = DockStyle.Fill };
    private readonly TextBox _bizType = new() { Dock = DockStyle.Fill };
    private readonly TextBox _bizItem = new() { Dock = DockStyle.Fill };
    private readonly TextBox _tel = new() { Dock = DockStyle.Fill };
    private readonly TextBox _email = new() { Dock = DockStyle.Fill };

    public PartyEditDialog(DocParty? existing)
    {
        _id = existing?.Id ?? 0;
        _channelCode = existing?.ChannelCode ?? "";
        _isDefaultSupplier = existing?.IsDefaultSupplier ?? false;

        Text = existing == null ? "거래처 추가" : "거래처 수정";
        Size = new Size(420, 460);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 10, Padding = new Padding(10) };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(tbl, 0, "프로필명", _profileName);
        AddRow(tbl, 1, "등록번호", _regNo);
        _regNo.PlaceholderText = "000-00-00000";
        AddRow(tbl, 2, "상호", _company);
        AddRow(tbl, 3, "대표자", _ceo);
        AddRow(tbl, 4, "주소", _address);
        AddRow(tbl, 5, "업태", _bizType);
        AddRow(tbl, 6, "종목", _bizItem);
        AddRow(tbl, 7, "전화", _tel);
        AddRow(tbl, 8, "이메일", _email);

        // 여기서 지정한 도장은 문서관리 화면에서 이 거래처(주로 공급자)를 불러올 때 자동으로
        // 채워진다. 문서관리 하단의 도장 업로드는 별개이며, 그쪽에서 직접 업로드하면 그 값이
        // 우선 적용된다(이 값을 덮어쓸 뿐, 여기 저장된 기본값 자체는 바뀌지 않음).
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var stampPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var stampBtn = new Button { Text = "업로드", Size = new Size(70, 24) };
        stampBtn.Click += OnStampUploadClick;
        stampPanel.Controls.Add(stampBtn);
        stampPanel.Controls.Add(_stampLabel);
        tbl.Controls.Add(new Label { Text = "기본 도장", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 9);
        tbl.Controls.Add(stampPanel, 1, 9);

        if (existing != null)
        {
            _profileName.Text = existing.ProfileName;
            _regNo.Text = existing.RegNo;
            _company.Text = existing.CompanyName;
            _ceo.Text = existing.CeoName;
            _address.Text = existing.Address;
            _bizType.Text = existing.BizType;
            _bizItem.Text = existing.BizItem;
            _tel.Text = existing.Tel;
            _email.Text = existing.Email;
            _stampImagePath = existing.StampImagePath;
        }
        _stampLabel.Text = string.IsNullOrWhiteSpace(_stampImagePath) ? "(도장 없음)" : Path.GetFileName(_stampImagePath);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), Height = 46 };
        var btnOk = new Button { Text = "저장", DialogResult = DialogResult.OK, Size = new Size(80, 28) };
        var btnCancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Size = new Size(80, 28) };
        btnOk.Click += OnOkClick;
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnOk);

        Controls.Add(tbl);
        Controls.Add(btnPanel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private static void AddRow(TableLayoutPanel tbl, int row, string label, Control input)
    {
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        tbl.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 4, 0) }, 0, row);
        tbl.Controls.Add(input, 1, row);
    }

    private void OnStampUploadClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "이미지 (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png", Title = "기본 도장 이미지 선택" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _stampImagePath = ofd.FileName;
        _stampLabel.Text = Path.GetFileName(_stampImagePath);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_company.Text))
        {
            MessageBox.Show("상호를 입력하세요.", "알림");
            DialogResult = DialogResult.None;
            return;
        }

        Result = new DocParty
        {
            Id = _id,
            ProfileName = string.IsNullOrWhiteSpace(_profileName.Text) ? _company.Text.Trim() : _profileName.Text.Trim(),
            RegNo = _regNo.Text.Trim(),
            CompanyName = _company.Text.Trim(),
            CeoName = _ceo.Text.Trim(),
            Address = _address.Text.Trim(),
            BizType = _bizType.Text.Trim(),
            BizItem = _bizItem.Text.Trim(),
            Tel = _tel.Text.Trim(),
            Email = _email.Text.Trim(),
            ChannelCode = _channelCode,
            IsDefaultSupplier = _isDefaultSupplier,
            StampImagePath = _stampImagePath,
        };
    }
}

using MiniERP2.Config;
using MiniERP2.Models;
using MiniERP2.Services;

namespace MiniERP2.Forms;

/// <summary>
/// 자동발주처리(Gmail 자동화) 연동 설정 — Drive OAuth 클라이언트/폴더 ID/폴링 정책
/// (03_자동발주처리_설정가이드_단계별.md Part 5, 02_자동발주처리_MiniERP2연동_설계.md §2, §7).
/// </summary>
public class AutoOrderSettingsDialog : Form
{
    private readonly AutoOrderSettingsService _settingsService = new();

    private TextBox _clientIdBox = new();
    private TextBox _clientSecretBox = new();
    private TextBox _pendingFolderIdBox = new();
    private NumericUpDown _pollingIntervalBox = new();
    private CheckBox _pollOnStartupBox = new();
    private Label _authStatusLabel = new();
    private Button _btnAuthorize = new();

    public AutoOrderSettingsDialog()
    {
        InitializeComponent();
        LoadSettings();
        RefreshAuthStatus();
    }

    private void InitializeComponent()
    {
        Text = "자동발주처리 연동 설정";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(520, 400);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(14), RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _clientIdBox = new TextBox { Dock = DockStyle.Fill };
        _clientSecretBox = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        _pendingFolderIdBox = new TextBox { Dock = DockStyle.Fill };
        _pollingIntervalBox = new NumericUpDown { Dock = DockStyle.Left, Width = 80, Minimum = 5, Maximum = 240, Value = 30 };
        _pollOnStartupBox = new CheckBox { Text = "프로그램 시작 시 자동으로 확인", Dock = DockStyle.Fill, Checked = true };

        AddRow(layout, 0, "클라이언트 ID:", _clientIdBox);
        AddRow(layout, 1, "클라이언트 보안 비밀:", _clientSecretBox);
        AddRow(layout, 2, "pending 폴더 ID:", _pendingFolderIdBox);
        AddRow(layout, 3, "폴링 간격(분):", _pollingIntervalBox);
        layout.Controls.Add(_pollOnStartupBox, 1, 4);

        _authStatusLabel = new Label { Dock = DockStyle.Fill, Text = "", ForeColor = Color.DimGray, AutoSize = false };
        layout.Controls.Add(_authStatusLabel, 1, 5);

        _btnAuthorize = new Button { Text = "인증하기(브라우저 로그인)", Size = new Size(200, 30) };
        _btnAuthorize.Click += OnAuthorizeClick;
        layout.Controls.Add(_btnAuthorize, 1, 6);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnCancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 80 };
        var btnSave = new Button { Text = "저장", Width = 80 };
        btnSave.Click += OnSaveClick;
        buttonPanel.Controls.AddRange([btnCancel, btnSave]);
        layout.Controls.Add(buttonPanel, 1, 7);

        var note = new Label
        {
            Dock = DockStyle.Top,
            Text = "설정가이드(03_자동발주처리_설정가이드_단계별.md) Part 3·5를 따라 발급한 값을 입력하세요.\n" +
                   "채널설정 창에서 채널 하나를 \"자동발주(표준) 파싱 프리셋\"으로 지정해야 발주 파일 로드가 동작합니다.",
            AutoSize = false,
            Height = 46,
            ForeColor = Color.DimGray,
            Padding = new Padding(14, 8, 14, 0),
        };

        Controls.Add(layout);
        Controls.Add(note);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        _clientIdBox.Text = settings.ClientId;
        _clientSecretBox.Text = settings.ClientSecret;
        _pendingFolderIdBox.Text = settings.PendingFolderId;
        _pollingIntervalBox.Value = Math.Clamp(settings.PollingIntervalMinutes, (int)_pollingIntervalBox.Minimum, (int)_pollingIntervalBox.Maximum);
        _pollOnStartupBox.Checked = settings.PollOnStartup;
    }

    private void RefreshAuthStatus()
    {
        var client = new GoogleDriveAutoOrderClient(_settingsService);
        _authStatusLabel.Text = client.HasCachedAuthorization()
            ? "✓ 로그인되어 있습니다."
            : "아직 로그인되어 있지 않습니다. 저장 후 [인증하기]를 눌러주세요.";
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        var settings = new AutoOrderSettings
        {
            ClientId = _clientIdBox.Text.Trim(),
            ClientSecret = _clientSecretBox.Text.Trim(),
            PendingFolderId = _pendingFolderIdBox.Text.Trim(),
            PollingIntervalMinutes = (int)_pollingIntervalBox.Value,
            PollOnStartup = _pollOnStartupBox.Checked,
        };
        _settingsService.Save(settings);
        DialogResult = DialogResult.OK;
        Close();
    }

    private async void OnAuthorizeClick(object? sender, EventArgs e)
    {
        // 인증 전에 방금 입력한 값을 먼저 저장해야 클라이언트가 최신 client_id/secret으로 시도한다.
        _settingsService.Save(new AutoOrderSettings
        {
            ClientId = _clientIdBox.Text.Trim(),
            ClientSecret = _clientSecretBox.Text.Trim(),
            PendingFolderId = _pendingFolderIdBox.Text.Trim(),
            PollingIntervalMinutes = (int)_pollingIntervalBox.Value,
            PollOnStartup = _pollOnStartupBox.Checked,
        });

        _btnAuthorize.Enabled = false;
        _authStatusLabel.Text = "브라우저에서 로그인을 완료해주세요...";
        Cursor = Cursors.WaitCursor;
        try
        {
            var client = new GoogleDriveAutoOrderClient(_settingsService);
            await client.AuthorizeAsync();
            _authStatusLabel.Text = "✓ 로그인되었습니다.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"인증 중 오류가 발생했습니다.\n{ex.Message}", "인증 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshAuthStatus();
        }
        finally
        {
            Cursor = Cursors.Default;
            _btnAuthorize.Enabled = true;
        }
    }
}

using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 파일 로드 시 사용할 채널을 선택하는 다이얼로그입니다.
/// </summary>
public class SelectChannelDialog : Form
{
    private readonly ComboBox _channelComboBox = new();
    public SalesChannel? SelectedChannel { get; private set; }

    public SelectChannelDialog()
    {
        InitializeComponent();
        LoadChannels();
    }

    private void InitializeComponent()
    {
        Text = "채널 선택";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(350, 190);

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _channelComboBox.Dock = DockStyle.Fill;
        _channelComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _channelComboBox.DisplayMember = "ChannelName";

        var newChannelPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var btnNewChannel = new Button { Text = "신규 채널 바로 추가...", AutoSize = true };
        btnNewChannel.Click += OnNewChannelClick;
        newChannelPanel.Controls.Add(btnNewChannel);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnOk = new Button { Text = "확인", Width = 80 };
        var btnCancel = new Button { Text = "취소", Width = 80 };

        btnOk.Click += OnOkClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        mainLayout.Controls.Add(_channelComboBox, 0, 0);
        mainLayout.Controls.Add(newChannelPanel, 0, 1);
        mainLayout.Controls.Add(buttonPanel, 0, 2);
        Controls.Add(mainLayout);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void LoadChannels()
    {
        var repository = new SalesChannelRepository();
        _channelComboBox.DataSource = repository.GetAll();
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        SelectedChannel = _channelComboBox.SelectedItem as SalesChannel;
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
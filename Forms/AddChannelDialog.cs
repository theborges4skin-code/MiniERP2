using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 새 판매 채널을 추가하기 위한 전용 다이얼로그 폼입니다.
/// 채널 코드는 화면에 노출되지 않는 내부 식별자이므로 자동 생성하고, 채널 이름만 입력받습니다.
/// 기존 채널을 골라 그 설정(발주서/정산서 매핑 등)을 그대로 복사해 시작할 수도 있습니다.
/// </summary>
public class AddChannelDialog : Form
{
    private readonly TextBox _txtChannelName = new();
    private readonly ComboBox _cmbCopyFrom = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private const string NoCopyOption = "(복사 안 함)";

    public string ChannelName => _txtChannelName.Text;

    public SalesChannel? SourceChannel => _cmbCopyFrom.SelectedItem as SalesChannel;

    public AddChannelDialog(IEnumerable<SalesChannel> existingChannels)
    {
        InitializeComponent(existingChannels);
    }

    private void InitializeComponent(IEnumerable<SalesChannel> existingChannels)
    {
        Text = "새 채널 추가";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(380, 190);

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), RowCount = 3, ColumnCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var lblName = new Label { Text = "채널 이름:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _txtChannelName.Dock = DockStyle.Fill;

        var lblCopyFrom = new Label { Text = "설정 복사:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _cmbCopyFrom.Dock = DockStyle.Fill;
        _cmbCopyFrom.Items.Add(NoCopyOption);
        foreach (var channel in existingChannels.OrderBy(c => c.ChannelName))
        {
            _cmbCopyFrom.Items.Add(channel);
        }
        _cmbCopyFrom.DisplayMember = "ChannelName";
        _cmbCopyFrom.SelectedIndex = 0;

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnOk = new Button { Text = "확인", Width = 80 };
        var btnCancel = new Button { Text = "취소", Width = 80 };

        btnOk.Click += OnOkClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        mainLayout.Controls.Add(lblName, 0, 0);
        mainLayout.Controls.Add(_txtChannelName, 1, 0);
        mainLayout.Controls.Add(lblCopyFrom, 0, 1);
        mainLayout.Controls.Add(_cmbCopyFrom, 1, 1);
        mainLayout.SetColumnSpan(buttonPanel, 2);
        mainLayout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(mainLayout);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ChannelName))
        {
            MessageBox.Show("채널 이름을 입력해야 합니다.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

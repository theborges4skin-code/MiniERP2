namespace MiniERP2.Forms;

/// <summary>
/// 새 판매 채널을 추가하기 위한 전용 다이얼로그 폼입니다.
/// </summary>
public class AddChannelDialog : Form
{
    private readonly TextBox _txtChannelCode = new();
    private readonly TextBox _txtChannelName = new();

    public string ChannelCode => _txtChannelCode.Text.ToUpper();
    public string ChannelName => _txtChannelName.Text;

    public AddChannelDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "새 채널 추가";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(350, 180);

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), RowCount = 3, ColumnCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var lblCode = new Label { Text = "채널 코드:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _txtChannelCode.Dock = DockStyle.Fill;

        var lblName = new Label { Text = "채널 이름:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _txtChannelName.Dock = DockStyle.Fill;

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnOk = new Button { Text = "확인", Width = 80 };
        var btnCancel = new Button { Text = "취소", Width = 80 };

        btnOk.Click += OnOkClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        mainLayout.Controls.Add(lblCode, 0, 0);
        mainLayout.Controls.Add(_txtChannelCode, 1, 0);
        mainLayout.Controls.Add(lblName, 0, 1);
        mainLayout.Controls.Add(_txtChannelName, 1, 1);
        mainLayout.SetColumnSpan(buttonPanel, 2);
        mainLayout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(mainLayout);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ChannelCode) || string.IsNullOrWhiteSpace(ChannelName))
        {
            MessageBox.Show("채널 코드와 이름을 모두 입력해야 합니다.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
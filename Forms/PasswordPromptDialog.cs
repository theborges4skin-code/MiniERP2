namespace MiniERP2.Forms;

/// <summary>
/// 암호로 보호된 엑셀 파일을 열기 전에 비밀번호를 입력받는 다이얼로그입니다.
/// </summary>
public class PasswordPromptDialog : Form
{
    private readonly TextBox _txtPassword = new();

    public string Password => _txtPassword.Text;

    public PasswordPromptDialog(string fileName)
    {
        InitializeComponent(fileName);
    }

    private void InitializeComponent(string fileName)
    {
        Text = "암호 입력";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(380, 170);

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var lblMessage = new Label
        {
            Text = $"'{fileName}' 파일이 암호로 보호되어 있습니다.\n비밀번호를 입력하세요.",
            Dock = DockStyle.Fill,
        };

        _txtPassword.Dock = DockStyle.Fill;
        _txtPassword.UseSystemPasswordChar = true;

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnOk = new Button { Text = "확인", Width = 80 };
        var btnCancel = new Button { Text = "취소", Width = 80 };
        btnOk.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        mainLayout.Controls.Add(lblMessage, 0, 0);
        mainLayout.Controls.Add(_txtPassword, 0, 1);
        mainLayout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(mainLayout);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}

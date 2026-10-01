namespace MiniERP2.Forms;

/// <summary>
/// 예/아니요 확인 창. 파일 대화상자(SaveFileDialog/OpenFileDialog)가 닫힌 직후 MessageBox.Show를
/// 띄우면 이 환경에서 창이 보이지 않게(또는 다른 창 뒤에) 생성되어, 사용자가 알아채기 전까지 앱이
/// 멈춘 것처럼 보이는 지연이 반복됐다(내보내기 덮어쓰기 확인). 일반 폼으로 만들어
/// FormManager.ShowDialogSafe로 띄우고 맨 앞에 고정한다.
/// </summary>
public class SafeConfirmDialog : Form
{
    public SafeConfirmDialog(string title, string message)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label
        {
            Text = message,
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Padding = new Padding(0, 4, 0, 12),
        }, 0, 0);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var btnNo = new Button { Text = "아니요", DialogResult = DialogResult.No, Size = new Size(80, 28) };
        var btnYes = new Button { Text = "예", DialogResult = DialogResult.Yes, Size = new Size(80, 28) };
        buttons.Controls.Add(btnNo);
        buttons.Controls.Add(btnYes);
        layout.Controls.Add(buttons, 0, 1);

        Controls.Add(layout);
        AcceptButton = btnYes;
        CancelButton = btnNo;
    }
}

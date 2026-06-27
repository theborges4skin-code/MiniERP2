namespace MiniERP2.Forms;

/// <summary>광고매핑 규칙(임시/조건부)을 만들 때 대상 상품그룹을 입력받는 작은 다이얼로그입니다.</summary>
public class AdTargetGroupPromptDialog : Form
{
    private readonly TextBox _textBox = new();
    public string TargetGroup => _textBox.Text.Trim();

    public AdTargetGroupPromptDialog()
    {
        Text = "대상 상품그룹 입력";
        Size = new Size(360, 160);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "이 항목을 매핑할 상품그룹을 입력하세요:", AutoSize = true }, 0, 0);
        _textBox.Dock = DockStyle.Top;
        layout.Controls.Add(_textBox, 0, 1);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft };
        var btnCancel = new Button { Text = "취소", Width = 80 };
        var btnOk = new Button { Text = "확인", Width = 80 };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnOk.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);
        layout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(layout);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        _textBox.Focus();
    }
}

namespace MiniERP2.Forms;

/// <summary>한 줄 텍스트를 입력받는 범용 작은 다이얼로그입니다(임시 매핑 SKU 입력 등에 사용).</summary>
public class TextPromptDialog : Form
{
    private readonly TextBox _textBox = new();
    public string Value => _textBox.Text.Trim();

    public TextPromptDialog(string title, string label, string initialValue = "")
    {
        Text = title;
        Size = new Size(380, 160);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = label, AutoSize = true }, 0, 0);
        _textBox.Dock = DockStyle.Top;
        _textBox.Text = initialValue;
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

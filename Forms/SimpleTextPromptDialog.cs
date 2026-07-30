using System.Text.RegularExpressions;

namespace MiniERP2.Forms;

/// <summary>
/// 한 줄 텍스트 하나만 입력받는 범용 다이얼로그. <paramref name="validator"/>를 주면 확인 시
/// 검증하고 실패 메시지를 보여준다(예: 거래처 마감보드 §4 귀속월 YYYY-MM 형식 검증).
/// </summary>
public class SimpleTextPromptDialog : Form
{
    private readonly TextBox _inputBox = new();
    private readonly Func<string, string?>? _validator;

    public string Value => _inputBox.Text.Trim();

    public SimpleTextPromptDialog(string title, string label, string initialValue = "", Func<string, string?>? validator = null)
    {
        _validator = validator;
        InitializeComponent(title, label, initialValue);
    }

    private void InitializeComponent(string title, string label, string initialValue)
    {
        Text = title;
        Size = new Size(340, 160);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        _inputBox.Text = initialValue;
        _inputBox.Width = 280;
        layout.Controls.Add(_inputBox);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40 };
        var btnOk = new Button { Text = "확인", Size = new Size(80, 30) };
        var btnCancel = new Button { Text = "취소", Size = new Size(80, 30) };
        btnOk.Click += OnOkClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var error = _validator?.Invoke(Value);
        if (error != null)
        {
            MessageBox.Show(error, "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    public static Func<string, string?> PeriodValidator => value =>
        Regex.IsMatch(value, @"^\d{4}-\d{2}$") ? null : "귀속월은 YYYY-MM 형식으로 입력하세요 (예: 2026-07).";
}

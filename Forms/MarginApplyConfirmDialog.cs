namespace MiniERP2.Forms;

/// <summary>
/// 간이 마진 계산기의 DB 적용 전 "대상 목록과 변경 전/후 값" 확인 다이얼로그(§6 공통 규칙).
/// 제조원가 적용(§6.1)·권장소비자가 적용(§6.3)에서 재사용한다.
/// </summary>
public class MarginApplyConfirmDialog : Form
{
    private readonly DataGridView _grid = new();

    private MarginApplyConfirmDialog(string title, string message, IReadOnlyList<(string Label, string Before, string After)> rows)
    {
        InitializeComponent(title, message, rows);
    }

    /// <summary>대상 목록을 보여주고 사용자가 확인을 눌렀는지 여부를 반환한다.</summary>
    public static bool Confirm(IWin32Window? owner, string title, string message, IReadOnlyList<(string Label, string Before, string After)> rows)
    {
        using var dialog = new MarginApplyConfirmDialog(title, message, rows);
        return MiniERP2.UI.FormManager.ShowDialogSafe(dialog, owner) == DialogResult.OK;
    }

    private void InitializeComponent(string title, string message, IReadOnlyList<(string Label, string Before, string After)> rows)
    {
        Text = title;
        Size = new Size(560, 480);
        MinimumSize = new Size(420, 300);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var messageLabel = new Label { Text = message, Dock = DockStyle.Fill, AutoSize = false, Height = 50, Padding = new Padding(8) };
        layout.Controls.Add(messageLabel, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add("Label", "대상");
        _grid.Columns.Add("Before", "변경 전");
        _grid.Columns.Add("After", "변경 후");
        foreach (var r in rows) _grid.Rows.Add(r.Label, r.Before, r.After);
        layout.Controls.Add(_grid, 0, 1);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var btnOk = new Button { Text = "적용", Width = 90 };
        var btnCancel = new Button { Text = "취소", Width = 90 };
        btnOk.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);
        layout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(layout);
        CancelButton = btnCancel;
    }
}

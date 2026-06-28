namespace MiniERP2.Forms;

/// <summary>
/// 채널을 다른 그룹으로 옮기거나(콤보박스에서 기존 그룹 선택) 새 그룹으로 옮기는(직접 입력) 다이얼로그.
/// 이 앱에서 "그룹"은 채널의 GroupName으로만 존재하므로(채널이 0개면 그룹 노드도 사라짐), 여기서
/// 기존에 없던 이름을 입력하면 그게 곧 "새 그룹 추가"가 된다. 빈칸으로 두면 '미분류'로 이동.
/// </summary>
public class MoveChannelToGroupDialog : Form
{
    private readonly ComboBox _comboBox = new();

    public string? GroupName => string.IsNullOrWhiteSpace(_comboBox.Text) ? null : _comboBox.Text.Trim();

    public MoveChannelToGroupDialog(IEnumerable<string> existingGroups, string currentGroup, string channelName)
    {
        Text = $"'{channelName}' 그룹 이동";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(380, 170);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "옮길 그룹을 선택하거나 새 그룹명을 입력하세요(빈칸=미분류):", AutoSize = true }, 0, 0);

        _comboBox.Dock = DockStyle.Top;
        _comboBox.DropDownStyle = ComboBoxStyle.DropDown;
        _comboBox.Items.AddRange(existingGroups.ToArray());
        _comboBox.Text = currentGroup;
        layout.Controls.Add(_comboBox, 0, 1);

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
        _comboBox.Focus();
    }
}

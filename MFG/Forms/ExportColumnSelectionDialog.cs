namespace MiniERP2.MFG.Forms;

/// <summary>엑셀로 내보낼 열 선택 + 한 열=값 필터(MiniERP2 ExportColumnSelectionDialog와 같은 기능).</summary>
public class ExportColumnSelectionDialog : Form
{
    private readonly CheckedListBox _columns = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly ComboBox _filterColumn = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly TextBox _filterValue = new() { Width = 130 };

    public List<string> SelectedColumns { get; private set; } = [];
    public string? FilterColumn { get; private set; }
    public string? FilterValue { get; private set; }

    public ExportColumnSelectionDialog(IReadOnlyList<string> allColumns)
    {
        Text = "엑셀로 내보낼 항목 선택";
        Size = new Size(400, 500);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        foreach (var c in allColumns) _columns.Items.Add(c, true);
        _filterColumn.Items.Add("(필터 없음)");
        _filterColumn.Items.AddRange(allColumns.Cast<object>().ToArray());
        _filterColumn.SelectedIndex = 0;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(new Label { Text = "내보낼 항목(헤더)을 선택하세요. 다시 불러올 때 행을 맞추려면 Id를 꼭 포함하세요.", AutoSize = false, Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(_columns, 0, 1);
        var filter = new FlowLayoutPanel { Dock = DockStyle.Fill };
        filter.Controls.AddRange([new Label { Text = "필터(선택):", AutoSize = true, Margin = new Padding(0, 6, 4, 0) }, _filterColumn,
            new Label { Text = "=", AutoSize = true, Margin = new Padding(4, 6, 4, 0) }, _filterValue]);
        layout.Controls.Add(filter, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "취소", Width = 80, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "내보내기", Width = 90 };
        ok.Click += (_, _) =>
        {
            SelectedColumns = _columns.CheckedItems.Cast<string>().ToList();
            if (SelectedColumns.Count == 0) { Ui.Info(this, "최소 한 개 이상의 항목을 선택하세요."); return; }
            FilterColumn = _filterColumn.SelectedIndex > 0 ? _filterColumn.SelectedItem as string : null;
            FilterValue = _filterValue.Text;
            DialogResult = DialogResult.OK;
        };
        buttons.Controls.AddRange([cancel, ok]);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

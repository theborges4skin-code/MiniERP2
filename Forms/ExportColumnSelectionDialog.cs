namespace MiniERP2.Forms;

/// <summary>
/// 데이터 관리창에서 엑셀로 내보낼 때 어떤 열(데이터 항목)을 포함할지 고르고, 선택적으로 한
/// 열=값 필터를 지정할 수 있게 한다(예: "A채널의 csku항목과 납품가 항목만 출력").
/// </summary>
public class ExportColumnSelectionDialog : Form
{
    private readonly CheckedListBox _columnList = new();
    private readonly ComboBox _filterColumnCombo = new();
    private readonly TextBox _filterValueTextBox = new();

    public List<string> SelectedColumns { get; private set; } = [];
    public string? FilterColumn { get; private set; }
    public string? FilterValue { get; private set; }

    public ExportColumnSelectionDialog(IEnumerable<string> allColumns)
    {
        InitializeComponent(allColumns.ToList());
    }

    private void InitializeComponent(List<string> allColumns)
    {
        Text = "엑셀로 내보낼 항목 선택";
        Size = new Size(380, 460);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(10) };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        mainLayout.Controls.Add(new Label { Text = "내보낼 데이터 항목(헤더)을 선택하세요:", AutoSize = true }, 0, 0);

        _columnList.Dock = DockStyle.Fill;
        _columnList.CheckOnClick = true;
        foreach (var column in allColumns)
        {
            _columnList.Items.Add(column, true);
        }
        mainLayout.Controls.Add(_columnList, 0, 1);

        var filterPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        filterPanel.Controls.Add(new Label { Text = "필터(선택, 비워두면 전체 출력):", AutoSize = true }, 0, 0);
        var filterRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        _filterColumnCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _filterColumnCombo.Width = 140;
        _filterColumnCombo.Items.Add("(필터 없음)");
        _filterColumnCombo.Items.AddRange(allColumns.Cast<object>().ToArray());
        _filterColumnCombo.SelectedIndex = 0;
        filterRow.Controls.Add(_filterColumnCombo);
        filterRow.Controls.Add(new Label { Text = "=", AutoSize = true, Padding = new Padding(6, 5, 6, 0) });
        _filterValueTextBox.Width = 120;
        filterRow.Controls.Add(_filterValueTextBox);
        filterPanel.Controls.Add(filterRow, 0, 1);
        mainLayout.Controls.Add(filterPanel, 0, 2);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnCancel = new Button { Text = "취소", Width = 80 };
        var btnOk = new Button { Text = "내보내기", Width = 90 };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnOk.Click += OnOkClick;
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);
        mainLayout.Controls.Add(buttonPanel, 0, 3);

        Controls.Add(mainLayout);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var selected = _columnList.CheckedItems.Cast<string>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("최소 한 개 이상의 항목을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectedColumns = selected;
        FilterColumn = _filterColumnCombo.SelectedIndex > 0 ? _filterColumnCombo.SelectedItem as string : null;
        FilterValue = _filterValueTextBox.Text;
        DialogResult = DialogResult.OK;
        Close();
    }
}

using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 배송지 주소록의 "복사등록" 다이얼로그. AddChannelDialog(채널 설정의 새 채널 추가 시 기존 채널
/// 설정을 복사하는 다이얼로그)와 같은 형식 — 새 라벨을 입력받고, 복사할 원본 주소를 콤보박스로
/// 고른다. 실제 복제(필드 복사 + Upsert)는 AddressBookForm에서 처리한다.
/// </summary>
public class AddressBookCopyDialog : Form
{
    private readonly TextBox _txtLabel = new();
    private readonly ComboBox _cmbCopyFrom = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public string NewLabel => _txtLabel.Text;

    public AddressBookEntry? SourceEntry => _cmbCopyFrom.SelectedItem as AddressBookEntry;

    public AddressBookCopyDialog(IEnumerable<AddressBookEntry> existingEntries, AddressBookEntry? defaultSource)
    {
        InitializeComponent(existingEntries, defaultSource);
    }

    private void InitializeComponent(IEnumerable<AddressBookEntry> existingEntries, AddressBookEntry? defaultSource)
    {
        Text = "배송지 복사등록";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(380, 190);

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), RowCount = 3, ColumnCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var lblCopyFrom = new Label { Text = "복사할 배송지:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _cmbCopyFrom.Dock = DockStyle.Fill;
        var orderedEntries = existingEntries.OrderBy(e => e.Label).ToList();
        foreach (var entry in orderedEntries) _cmbCopyFrom.Items.Add(entry);
        _cmbCopyFrom.DisplayMember = "Label";
        var defaultIndex = defaultSource != null ? orderedEntries.FindIndex(e => e.AddressId == defaultSource.AddressId) : -1;
        _cmbCopyFrom.SelectedIndex = defaultIndex >= 0 ? defaultIndex : (orderedEntries.Count > 0 ? 0 : -1);

        var lblLabel = new Label { Text = "새 라벨(표시명):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _txtLabel.Dock = DockStyle.Fill;
        if (defaultSource != null) _txtLabel.Text = defaultSource.Label + "_사본";

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnOk = new Button { Text = "확인", Width = 80 };
        var btnCancel = new Button { Text = "취소", Width = 80 };

        btnOk.Click += OnOkClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        mainLayout.Controls.Add(lblCopyFrom, 0, 0);
        mainLayout.Controls.Add(_cmbCopyFrom, 1, 0);
        mainLayout.Controls.Add(lblLabel, 0, 1);
        mainLayout.Controls.Add(_txtLabel, 1, 1);
        mainLayout.SetColumnSpan(buttonPanel, 2);
        mainLayout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(mainLayout);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (SourceEntry == null)
        {
            MessageBox.Show("복사할 배송지를 선택해야 합니다.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(NewLabel))
        {
            MessageBox.Show("새 라벨을 입력해야 합니다.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

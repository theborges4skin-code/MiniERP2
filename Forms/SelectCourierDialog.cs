using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 엑셀 내보내기 시 사용할 택배사를 선택하는 다이얼로그입니다.
/// </summary>
public class SelectCourierDialog : Form
{
    private readonly ComboBox _courierComboBox = new();
    public CourierMaster? SelectedCourier { get; private set; }

    public SelectCourierDialog()
    {
        InitializeComponent();
        LoadCouriers();
    }

    private void InitializeComponent()
    {
        Text = "택배사 선택";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(350, 150);

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _courierComboBox.Dock = DockStyle.Fill;
        _courierComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _courierComboBox.DisplayMember = "CourierName";

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnOk = new Button { Text = "확인", Width = 80 };
        var btnCancel = new Button { Text = "취소", Width = 80 };

        btnOk.Click += OnOkClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        mainLayout.Controls.Add(_courierComboBox, 0, 0);
        mainLayout.Controls.Add(buttonPanel, 0, 1);
        Controls.Add(mainLayout);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void LoadCouriers()
    {
        var repository = new CourierRepository();
        _courierComboBox.DataSource = repository.GetAll();
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        SelectedCourier = _courierComboBox.SelectedItem as CourierMaster;
        DialogResult = DialogResult.OK;
        Close();
    }
}
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// FbaOrderForm에서 박스규격 콤보의 "(직접입력)"을 골랐을 때 뜨는 W/D/H 입력창. "규격마스터에
/// 저장" 버튼으로 다음 발주부터는 마스터에서 바로 고를 수 있게 등록할 수 있다(기획서 §4). 저장하지
/// 않고 "확인"만 누르면 이번 박스에만 한 번 쓰는 치수로 처리되고 BoxSpecName은 "(직접입력)"이 된다.
/// </summary>
public class FbaBoxSpecInputDialog : Form
{
    public double WidthMm { get; private set; }
    public double DepthMm { get; private set; }
    public double HeightMm { get; private set; }
    public string BoxSpecName { get; private set; } = "(직접입력)";

    private readonly FbaBoxSpecRepository _repository = new();
    private readonly NumericUpDown _widthInput = new();
    private readonly NumericUpDown _depthInput = new();
    private readonly NumericUpDown _heightInput = new();
    private readonly TextBox _nameInput = new();

    public FbaBoxSpecInputDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "박스규격 직접입력";
        Size = new Size(360, 240);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _widthInput.Minimum = 0;
        _widthInput.Maximum = 9999;
        _widthInput.Width = 100;
        _depthInput.Minimum = 0;
        _depthInput.Maximum = 9999;
        _depthInput.Width = 100;
        _heightInput.Minimum = 0;
        _heightInput.Maximum = 9999;
        _heightInput.Width = 100;
        _nameInput.Width = 150;
        _nameInput.PlaceholderText = "규격마스터에 저장할 이름";

        layout.Controls.Add(new Label { Text = "가로(mm):", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 0);
        layout.Controls.Add(_widthInput, 1, 0);
        layout.Controls.Add(new Label { Text = "세로(mm):", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 1);
        layout.Controls.Add(_depthInput, 1, 1);
        layout.Controls.Add(new Label { Text = "높이(mm):", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 2);
        layout.Controls.Add(_heightInput, 1, 2);
        layout.Controls.Add(new Label { Text = "규격명(저장시):", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 3);
        layout.Controls.Add(_nameInput, 1, 3);

        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnOk = new Button { Text = "확인 (이번만 사용)", AutoSize = true, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "취소", AutoSize = true, DialogResult = DialogResult.Cancel };
        var btnSaveToMaster = new Button { Text = "규격마스터에 저장", AutoSize = true };
        btnOk.Click += (s, e) => ApplyValues("(직접입력)");
        btnSaveToMaster.Click += OnSaveToMasterClick;
        btnPanel.Controls.Add(btnOk);
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnSaveToMaster);

        layout.Controls.Add(btnPanel, 0, 4);
        layout.SetColumnSpan(btnPanel, 2);

        Controls.Add(layout);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void ApplyValues(string boxSpecName)
    {
        WidthMm = (double)_widthInput.Value;
        DepthMm = (double)_depthInput.Value;
        HeightMm = (double)_heightInput.Value;
        BoxSpecName = boxSpecName;
    }

    private void OnSaveToMasterClick(object? sender, EventArgs e)
    {
        var name = _nameInput.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("저장할 규격명을 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _repository.Upsert(new FbaBoxSpec
        {
            BoxName = name,
            WidthMm = (double)_widthInput.Value,
            DepthMm = (double)_depthInput.Value,
            HeightMm = (double)_heightInput.Value,
            SortOrder = 999,
        });
        ApplyValues(name);
        DialogResult = DialogResult.OK;
        Close();
    }
}

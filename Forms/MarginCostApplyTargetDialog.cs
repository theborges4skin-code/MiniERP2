namespace MiniERP2.Forms;

/// <summary>제조원가 적용 대상 선택(§6.1) — 기본값 없이 반드시 하나를 고르게 한다.</summary>
public class MarginCostApplyTargetDialog : Form
{
    private readonly RadioButton _masterRadio = new();
    private readonly RadioButton _cskuRadio = new();

    public enum Target { Master, Csku }
    public Target? SelectedTarget { get; private set; }

    public MarginCostApplyTargetDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "제조원가 적용 대상 선택";
        Size = new Size(360, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), RowCount = 4 };

        layout.Controls.Add(new Label
        {
            Text = "적용 대상을 선택하세요.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        });

        _masterRadio.Text = "마스터 대표원가 (ItemTable.CostPrice) — 이 SKU를 쓰는 전 채널에 영향";
        _masterRadio.AutoSize = true;
        _masterRadio.MaximumSize = new Size(320, 0);
        layout.Controls.Add(_masterRadio);

        _cskuRadio.Text = "CSKU 개별원가 (ChannelSkuTable.CostPriceOverride) — 선택한 행의 CSKU만";
        _cskuRadio.AutoSize = true;
        _cskuRadio.MaximumSize = new Size(320, 0);
        layout.Controls.Add(_cskuRadio);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40 };
        var btnOk = new Button { Text = "다음", Size = new Size(80, 28) };
        var btnCancel = new Button { Text = "취소", Size = new Size(80, 28) };
        btnOk.Click += OnOkClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        CancelButton = btnCancel;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (!_masterRadio.Checked && !_cskuRadio.Checked)
        {
            MessageBox.Show("적용 대상을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectedTarget = _masterRadio.Checked ? Target.Master : Target.Csku;
        DialogResult = DialogResult.OK;
        Close();
    }
}

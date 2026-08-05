using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 이력채널 이관 다이얼로그(샘플발송이력관리_개발기획서.md §5.2~5.3). 발주/출고 이력 관리창에서
/// 비매출 라인만 선택했을 때 우클릭으로 연다.
/// </summary>
public class ChannelTransferDialog : Form
{
    private readonly SalesChannelRepository _channelRepo = new();
    private readonly int _selectedCount;

    private ComboBox _targetChannelCombo = new();
    private RadioButton _priceKeep = new();
    private RadioButton _priceFromTarget = new();
    private RadioButton _priceManual = new();
    private TextBox _priceManualBox = new();
    private RadioButton _lineKindKeep = new();
    private RadioButton _lineKindConvert = new();
    private RadioButton _periodKeep = new();
    private RadioButton _periodManual = new();
    private TextBox _periodManualBox = new();

    public string? TargetChannelCode { get; private set; }
    public string? TargetChannelName { get; private set; }
    public bool UpdateSupplyPriceFromTarget { get; private set; }
    public decimal? ManualSupplyPrice { get; private set; }
    public bool ConvertToSaleTransaction { get; private set; }
    public string? ForcedClosingPeriod { get; private set; }

    public ChannelTransferDialog(int selectedCount)
    {
        _selectedCount = selectedCount;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "선택 이력 채널 이관";
        Size = new Size(420, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1 };

        layout.Controls.Add(new Label
        {
            Text = $"선택한 {_selectedCount}건을 이관합니다.",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8),
        });

        layout.Controls.Add(new Label { Text = "대상 채널", AutoSize = true, Margin = new Padding(0, 4, 0, 2) });
        var channels = _channelRepo.GetAll();
        _targetChannelCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _targetChannelCombo.DataSource = channels;
        _targetChannelCombo.DisplayMember = nameof(SalesChannel.ChannelName);
        _targetChannelCombo.ValueMember = nameof(SalesChannel.ChannelCode);
        layout.Controls.Add(_targetChannelCombo);

        layout.Controls.Add(BuildGroup("납품가", out _priceKeep, "유지(기본)", out _priceFromTarget, "대상 채널 CSKU 납품가로 갱신",
            manualBox: out _priceManualBox, manualLabel: "직접 입력:", manualRadio: out _priceManual));

        layout.Controls.Add(BuildSimpleGroup("구분값 처리", out _lineKindKeep, "유지(기본)", out _lineKindConvert, "정상거래로 편입(LineKind 해제)"));

        layout.Controls.Add(BuildGroup("귀속월", out _periodKeep, "유지(기본)", out _, "",
            manualBox: out _periodManualBox, manualLabel: "직접 지정(YYYY-MM):", manualRadio: out _periodManual, includeMiddleOption: false));

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 42 };
        var btnCancel = new Button { Text = "취소", Width = 80 };
        var btnOk = new Button { Text = "이관 실행", Width = 90, Font = new Font(Font, FontStyle.Bold) };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnOk.Click += OnOkClick;
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (_targetChannelCombo.SelectedValue is not string targetCode || string.IsNullOrEmpty(targetCode))
        {
            MessageBox.Show("대상 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        decimal? manualPrice = null;
        if (_priceManual.Checked)
        {
            if (!decimal.TryParse(_priceManualBox.Text, out var parsed) || parsed < 0)
            {
                MessageBox.Show("납품가를 올바른 숫자로 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            manualPrice = parsed;
        }

        string? forcedPeriod = null;
        if (_periodManual.Checked)
        {
            var error = SimpleTextPromptDialog.PeriodValidator(_periodManualBox.Text.Trim());
            if (error != null)
            {
                MessageBox.Show(error, "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            forcedPeriod = _periodManualBox.Text.Trim();
        }

        TargetChannelCode = targetCode;
        TargetChannelName = (_targetChannelCombo.SelectedItem as SalesChannel)?.ChannelName ?? targetCode;
        UpdateSupplyPriceFromTarget = _priceFromTarget.Checked;
        ManualSupplyPrice = manualPrice;
        ConvertToSaleTransaction = _lineKindConvert.Checked;
        ForcedClosingPeriod = forcedPeriod;

        DialogResult = DialogResult.OK;
        Close();
    }

    private Control BuildSimpleGroup(string title, out RadioButton keep, string keepLabel, out RadioButton alt, string altLabel)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Top, Height = 70, Margin = new Padding(0, 6, 0, 0) };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8, 4, 0, 0) };
        keep = new RadioButton { Text = keepLabel, AutoSize = true, Checked = true };
        alt = new RadioButton { Text = altLabel, AutoSize = true };
        panel.Controls.Add(keep);
        panel.Controls.Add(alt);
        box.Controls.Add(panel);
        return box;
    }

    private Control BuildGroup(string title, out RadioButton keep, string keepLabel, out RadioButton alt, string altLabel,
        out TextBox manualBox, string manualLabel, out RadioButton manualRadio, bool includeMiddleOption = true)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Top, Height = includeMiddleOption ? 110 : 80, Margin = new Padding(0, 6, 0, 0) };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8, 4, 0, 0) };
        keep = new RadioButton { Text = keepLabel, AutoSize = true, Checked = true };
        panel.Controls.Add(keep);

        alt = new RadioButton { Text = altLabel, AutoSize = true, Visible = includeMiddleOption };
        if (includeMiddleOption) panel.Controls.Add(alt);

        var manualPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var localManualRadio = new RadioButton { Text = manualLabel, AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
        var localManualBox = new TextBox { Width = 120, Margin = new Padding(3, 3, 3, 3), Enabled = false };
        localManualRadio.CheckedChanged += (s, e) => localManualBox.Enabled = localManualRadio.Checked;
        manualPanel.Controls.Add(localManualRadio);
        manualPanel.Controls.Add(localManualBox);
        panel.Controls.Add(manualPanel);
        manualRadio = localManualRadio;
        manualBox = localManualBox;

        box.Controls.Add(panel);
        return box;
    }
}

using MiniERP2.Database;

namespace MiniERP2.Forms;

/// <summary>광고비 보고서 저장 시 기간(YYYY-MM) 입력용 다이얼로그.</summary>
public class AdFactPeriodInputDialog : Form
{
    private readonly string _channelCode;
    private readonly string _channelName;
    private readonly ProfitFactRepository _repo;
    private TextBox _periodBox = new();
    private Label _overwriteWarningLabel = new();

    public string SelectedPeriod => _periodBox.Text.Trim();

    public AdFactPeriodInputDialog(string channelCode, string channelName, ProfitFactRepository repo)
    {
        _channelCode = channelCode;
        _channelName = channelName;
        _repo = repo;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "광고비 보고서 저장 — 기간 입력";
        Size = new Size(360, 200);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var channelLabel = new Label { Text = $"채널: {_channelName}", AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        var periodLabel = new Label { Text = "기간 (YYYY-MM):", AutoSize = true };
        _periodBox = new TextBox { Text = DateTime.Today.ToString("yyyy-MM"), Width = 120, Margin = new Padding(0, 2, 0, 6) };
        _periodBox.TextChanged += (s, e) => CheckExisting();

        _overwriteWarningLabel = new Label { Text = string.Empty, ForeColor = Color.DarkOrange, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };

        var buttonPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 40 };
        var btnCancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Size = new Size(72, 30) };
        var btnOk = new Button { Text = "저장", DialogResult = DialogResult.OK, Size = new Size(72, 30) };
        btnOk.Click += OnOkClick;
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        layout.Controls.Add(channelLabel);
        layout.Controls.Add(periodLabel);
        layout.Controls.Add(_periodBox);
        layout.Controls.Add(_overwriteWarningLabel);

        Controls.Add(layout);
        Controls.Add(buttonPanel);

        CheckExisting();
    }

    private void CheckExisting()
    {
        var p = _periodBox.Text.Trim();
        _overwriteWarningLabel.Text = (p.Length == 7 && _repo.HasAdData(p, _channelCode))
            ? $"⚠ {p}의 기존 광고비 데이터를 덮어씁니다."
            : string.Empty;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var p = _periodBox.Text.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(p, @"^\d{4}-\d{2}$"))
        {
            MessageBox.Show("기간은 YYYY-MM 형식으로 입력하세요 (예: 2026-06).", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        }
    }
}

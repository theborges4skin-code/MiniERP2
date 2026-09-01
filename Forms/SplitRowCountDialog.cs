namespace MiniERP2.Forms;

/// <summary>
/// 분리배송 처리 시 몇 개의 송장(행)으로 나눌지 입력받는 소형 다이얼로그.
/// 입력한 행 수만큼 수량을 균등 분배하며(나머지는 앞쪽 행부터 1개씩 더 배분), 정확한 배분이
/// 필요 없는 주문도 많으므로 이 값은 시작점일 뿐 그리드에서 각 행 수량을 바로 고치면 된다.
/// </summary>
public class SplitRowCountDialog : Form
{
    private readonly NumericUpDown _countSpinner = new();
    private readonly Label _previewLabel = new();
    private readonly int? _previewQuantity;

    public int SplitCount => (int)_countSpinner.Value;

    public SplitRowCountDialog(int maxSplitCount, int? previewQuantity = null)
    {
        _previewQuantity = previewQuantity;
        InitializeComponent(maxSplitCount);
    }

    private void InitializeComponent(int maxSplitCount)
    {
        Text = "분리배송 처리";
        Size = new Size(340, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var lblInfo = new Label
        {
            Text = "몇 개의 송장(행)으로 나눌까요?",
            Dock = DockStyle.Top,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 6, 0, 0)
        };

        var countRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(12, 0, 0, 0),
            FlowDirection = FlowDirection.LeftToRight
        };
        var lblCount = new Label { Text = "행 수:", AutoSize = true, Margin = new Padding(0, 3, 6, 0) };
        _countSpinner.Minimum = 2;
        _countSpinner.Maximum = Math.Max(2, maxSplitCount);
        _countSpinner.Value = Math.Min(2, _countSpinner.Maximum);
        _countSpinner.Width = 80;
        _countSpinner.ValueChanged += (s, e) => UpdatePreview();
        _countSpinner.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
        };
        countRow.Controls.AddRange([lblCount, _countSpinner]);

        _previewLabel.Dock = DockStyle.Top;
        _previewLabel.Height = 28;
        _previewLabel.Padding = new Padding(12, 0, 0, 0);
        _previewLabel.ForeColor = SystemColors.GrayText;

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8)
        };
        var btnOk = new Button { Text = "분리", Width = 80 };
        var btnCancel = new Button { Text = "취소", Width = 80 };
        btnOk.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnPanel.Controls.AddRange([btnCancel, btnOk]);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.Add(_previewLabel);
        Controls.Add(countRow);
        Controls.Add(lblInfo);
        Controls.Add(btnPanel);

        ActiveControl = _countSpinner;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_previewQuantity is not int qty || qty <= 0)
        {
            _previewLabel.Text = string.Empty;
            return;
        }

        var n = (int)_countSpinner.Value;
        var baseQty = qty / n;
        var remainder = qty % n;
        _previewLabel.Text = remainder == 0
            ? $"예: {qty}개 → {baseQty}개씩 {n}행"
            : $"예: {qty}개 → {baseQty + 1}개 {remainder}행 + {baseQty}개 {n - remainder}행";
    }
}

namespace MiniERP2.Forms;

/// <summary>
/// 수동 주문 추가 시 수량·추가할 주문 수를 입력받는 소형 확인 다이얼로그.
/// CSKU 코드·상품명을 표시하고 수량을 NumericUpDown으로 입력받습니다. "추가할 주문 수"는 엑셀에서
/// 수취인 정보 여러 줄을 붙여넣을 줄을 한 번에 만들기 위한 값입니다.
/// </summary>
public class ManualOrderQuantityDialog : Form
{
    private readonly NumericUpDown _qtySpinner = new();
    private readonly NumericUpDown _countSpinner = new();
    public int Quantity => (int)_qtySpinner.Value;
    public int OrderCount => (int)_countSpinner.Value;

    /// <summary>CSKU 추가용 — 수량과 추가할 주문 수를 묻는다.</summary>
    public ManualOrderQuantityDialog(string cskuCode, string productName)
    {
        InitializeComponent($"{cskuCode}  —  {productName}", askQuantity: true);
    }

    private void InitializeComponent(string info, bool askQuantity)
    {
        Text = "수량 입력";
        Size = new Size(340, askQuantity ? 205 : 165);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var lblInfo = new Label
        {
            Text = info,
            Dock = DockStyle.Top,
            Height = 44,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Regular)
        };

        _qtySpinner.Minimum = 1;
        _qtySpinner.Maximum = 9999;
        _qtySpinner.Value = 1;
        _countSpinner.Minimum = 1;
        _countSpinner.Maximum = 500;
        _countSpinner.Value = 1;
        var qtyRow = MakeRow("수량:", _qtySpinner, "개");
        var countRow = MakeRow("추가할 주문 수:", _countSpinner, "건");

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8)
        };
        var btnOk = new Button { Text = "추가", Width = 80 };
        var btnCancel = new Button { Text = "취소", Width = 80 };
        btnOk.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnPanel.Controls.AddRange([btnCancel, btnOk]);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        // DockStyle.Top은 나중에 추가한 것이 위로 쌓인다.
        Controls.Add(countRow);
        if (askQuantity) Controls.Add(qtyRow);
        Controls.Add(lblInfo);
        Controls.Add(btnPanel);

        ActiveControl = askQuantity ? _qtySpinner : _countSpinner;
    }

    private FlowLayoutPanel MakeRow(string label, NumericUpDown spinner, string unit)
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(12, 4, 0, 0),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        spinner.Width = 80;
        spinner.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
        };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 4, 6, 0) });
        row.Controls.Add(spinner);
        row.Controls.Add(new Label { Text = unit, AutoSize = true, Margin = new Padding(4, 4, 0, 0) });
        return row;
    }
}

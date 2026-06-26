namespace MiniERP2.Forms;

/// <summary>
/// 마스터SKU 엑셀 가져오기 시, 실제 파일의 헤더를 보면서 어느 열이 SKU/상품명/원가인지
/// 직접 선택할 수 있게 하는 다이얼로그입니다.
/// </summary>
public class MasterSkuImportMappingDialog : Form
{
    private readonly ComboBox _skuCombo = new();
    private readonly ComboBox _itemNameCombo = new();
    private readonly ComboBox _costPriceCombo = new();

    public string SkuColumn => _skuCombo.SelectedItem as string ?? string.Empty;
    public string ItemNameColumn => _itemNameCombo.SelectedItem as string ?? string.Empty;
    public string CostPriceColumn => _costPriceCombo.SelectedItem as string ?? string.Empty;

    public MasterSkuImportMappingDialog(List<string> headers)
    {
        InitializeComponent(headers);
    }

    private void InitializeComponent(List<string> headers)
    {
        Text = "마스터SKU 가져오기 - 열 선택";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(380, 220);

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), RowCount = 4, ColumnCount = 2 };
        for (int i = 0; i < 3; i++) mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        SetUpCombo(_skuCombo, headers, ["sku", "품목코드", "코드"]);
        SetUpCombo(_itemNameCombo, headers, ["상품명", "품목명", "이름"]);
        SetUpCombo(_costPriceCombo, headers, ["원가", "단가", "가격", "cost"]);

        mainLayout.Controls.Add(new Label { Text = "SKU 열:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        mainLayout.Controls.Add(_skuCombo, 1, 0);
        mainLayout.Controls.Add(new Label { Text = "상품명 열:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        mainLayout.Controls.Add(_itemNameCombo, 1, 1);
        mainLayout.Controls.Add(new Label { Text = "원가 열:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        mainLayout.Controls.Add(_costPriceCombo, 1, 2);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnOk = new Button { Text = "확인", Width = 80 };
        var btnCancel = new Button { Text = "취소", Width = 80 };
        btnOk.Click += OnOkClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        mainLayout.SetColumnSpan(buttonPanel, 2);
        mainLayout.Controls.Add(buttonPanel, 0, 3);

        Controls.Add(mainLayout);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private static void SetUpCombo(ComboBox combo, List<string> headers, string[] guessKeywords)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Items.AddRange(headers.Cast<object>().ToArray());

        var guessedIndex = headers.FindIndex(h => guessKeywords.Any(k => h.Contains(k, StringComparison.OrdinalIgnoreCase)));
        combo.SelectedIndex = guessedIndex >= 0 ? guessedIndex : (headers.Count > 0 ? 0 : -1);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (_skuCombo.SelectedItem == null || _itemNameCombo.SelectedItem == null || _costPriceCombo.SelectedItem == null)
        {
            MessageBox.Show("SKU/상품명/원가 열을 모두 선택하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

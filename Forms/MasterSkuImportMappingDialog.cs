using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 마스터SKU 엑셀 가져오기 시, 실제 파일의 시트/시작행(헤더 행)과 헤더를 보면서
/// SKU/상품명/원가 및 예비필드 3개가 어느 열인지 직접 선택할 수 있게 하는 다이얼로그입니다.
/// </summary>
public class MasterSkuImportMappingDialog : Form
{
    private const string NotUsed = "(사용 안 함)";

    private readonly ExcelPackage _package;
    private readonly ComboBox _sheetCombo = new();
    private readonly NumericUpDown _headerRowInput = new();
    private readonly ComboBox _skuCombo = new();
    private readonly ComboBox _itemNameCombo = new();
    private readonly ComboBox _costPriceCombo = new();
    private readonly ComboBox _reserve1Combo = new();
    private readonly ComboBox _reserve2Combo = new();
    private readonly ComboBox _reserve3Combo = new();

    public string SheetName => _sheetCombo.SelectedItem as string ?? string.Empty;
    public int HeaderRow => (int)_headerRowInput.Value;
    public string SkuColumn => _skuCombo.SelectedItem as string ?? string.Empty;
    public string ItemNameColumn => _itemNameCombo.SelectedItem as string ?? string.Empty;
    public string CostPriceColumn => _costPriceCombo.SelectedItem as string ?? string.Empty;
    public string? Reserve1Column => AsOptionalColumn(_reserve1Combo);
    public string? Reserve2Column => AsOptionalColumn(_reserve2Combo);
    public string? Reserve3Column => AsOptionalColumn(_reserve3Combo);

    private static string? AsOptionalColumn(ComboBox combo) =>
        combo.SelectedItem is string s && s != NotUsed ? s : null;

    public MasterSkuImportMappingDialog(ExcelPackage package)
    {
        _package = package;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "마스터SKU 가져오기 - 시트/열 선택";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(420, 420);

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), RowCount = 9, ColumnCount = 2 };
        for (int i = 0; i < 8; i++) mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _sheetCombo.Dock = DockStyle.Fill;
        _sheetCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _sheetCombo.Items.AddRange(_package.Workbook.Worksheets.Select(w => w.Name).Cast<object>().ToArray());

        _headerRowInput.Dock = DockStyle.Fill;
        _headerRowInput.Minimum = 1;
        _headerRowInput.Maximum = 1000;
        _headerRowInput.Value = 1;

        foreach (var combo in new[] { _skuCombo, _itemNameCombo, _costPriceCombo, _reserve1Combo, _reserve2Combo, _reserve3Combo })
        {
            combo.Dock = DockStyle.Fill;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
        }

        int row = 0;
        mainLayout.Controls.Add(new Label { Text = "시트:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        mainLayout.Controls.Add(_sheetCombo, 1, row++);
        mainLayout.Controls.Add(new Label { Text = "헤더 행:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        mainLayout.Controls.Add(_headerRowInput, 1, row++);
        mainLayout.Controls.Add(new Label { Text = "SKU 열:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        mainLayout.Controls.Add(_skuCombo, 1, row++);
        mainLayout.Controls.Add(new Label { Text = "상품명 열:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        mainLayout.Controls.Add(_itemNameCombo, 1, row++);
        mainLayout.Controls.Add(new Label { Text = "원가 열:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        mainLayout.Controls.Add(_costPriceCombo, 1, row++);
        mainLayout.Controls.Add(new Label { Text = "예비1 열:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        mainLayout.Controls.Add(_reserve1Combo, 1, row++);
        mainLayout.Controls.Add(new Label { Text = "예비2 열:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        mainLayout.Controls.Add(_reserve2Combo, 1, row++);
        mainLayout.Controls.Add(new Label { Text = "예비3 열:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        mainLayout.Controls.Add(_reserve3Combo, 1, row++);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnOk = new Button { Text = "확인", Width = 80 };
        var btnCancel = new Button { Text = "취소", Width = 80 };
        btnOk.Click += OnOkClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);
        mainLayout.SetColumnSpan(buttonPanel, 2);
        mainLayout.Controls.Add(buttonPanel, 0, row);

        Controls.Add(mainLayout);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        _sheetCombo.SelectedIndexChanged += (s, e) => RefreshHeaders();
        _headerRowInput.ValueChanged += (s, e) => RefreshHeaders();

        if (_sheetCombo.Items.Count > 0) _sheetCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// 선택된 시트/헤더 행의 실제 헤더 텍스트를 읽어 모든 콤보박스의 후보를 새로 채웁니다.
    /// </summary>
    private void RefreshHeaders()
    {
        if (_sheetCombo.SelectedItem is not string sheetName) return;

        var sheet = _package.Workbook.Worksheets[sheetName];
        var headerRow = (int)_headerRowInput.Value;

        var headers = new List<string>();
        if (sheet?.Dimension != null && headerRow <= sheet.Dimension.End.Row)
        {
            for (int col = 1; col <= sheet.Dimension.End.Column; col++)
            {
                var header = sheet.Cells[headerRow, col].Value?.ToString();
                if (!string.IsNullOrWhiteSpace(header)) headers.Add(header);
            }
        }

        SetUpRequiredCombo(_skuCombo, headers, ["sku", "품목코드", "코드"]);
        SetUpRequiredCombo(_itemNameCombo, headers, ["상품명", "품목명", "이름"]);
        SetUpRequiredCombo(_costPriceCombo, headers, ["원가", "단가", "가격", "cost"]);
        SetUpOptionalCombo(_reserve1Combo, headers);
        SetUpOptionalCombo(_reserve2Combo, headers);
        SetUpOptionalCombo(_reserve3Combo, headers);
    }

    private static void SetUpRequiredCombo(ComboBox combo, List<string> headers, string[] guessKeywords)
    {
        combo.Items.Clear();
        combo.Items.AddRange(headers.Cast<object>().ToArray());

        var guessedIndex = headers.FindIndex(h => guessKeywords.Any(k => h.Contains(k, StringComparison.OrdinalIgnoreCase)));
        combo.SelectedIndex = guessedIndex >= 0 ? guessedIndex : (headers.Count > 0 ? 0 : -1);
    }

    private static void SetUpOptionalCombo(ComboBox combo, List<string> headers)
    {
        combo.Items.Clear();
        combo.Items.Add(NotUsed);
        combo.Items.AddRange(headers.Cast<object>().ToArray());
        combo.SelectedIndex = 0;
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

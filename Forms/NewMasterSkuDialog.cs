using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 마스터DB(ItemTable)에 원하는 SKU가 아직 없을 때, CSKU 등록 흐름 중간에 바로 새 마스터SKU를
/// 만들 수 있게 하는 작은 다이얼로그입니다(NewCskuRegistrationDialog에서 호출). SKU 코드는
/// TempSkuGenerator로 기본값을 제안하되(기존 "임시 SKU 등록" 관례와 동일), 직접 입력해 실제
/// 코드로 등록할 수도 있습니다.
/// </summary>
public class NewMasterSkuDialog : Form
{
    private readonly ItemRepository _itemRepository = new();

    private TextBox _skuText = new();
    private TextBox _itemNameText = new();
    private ComboBox _productGroupCombo = new();
    private TextBox _costPriceText = new();
    private TextBox _unitText = new();

    public string? ResultSku { get; private set; }
    public string? ResultItemName { get; private set; }
    public string? ResultUnit { get; private set; }
    public string? ResultProductGroup { get; private set; }

    /// <param name="suggestedItemName">CSKU 등록 화면에서 검색어로 입력해둔 품목명이 있으면
    /// 기본값으로 넘겨받아 품명 칸에 미리 채운다(매번 다시 타이핑하지 않도록).</param>
    /// <param name="suggestedCostPrice">간이 마진 계산기 등에서 이미 계산해둔 제조원가(적용)가
    /// 있으면 미리 채운다(간이마진계산기_개발기획서.md §6.4).</param>
    /// <param name="suggestedProductGroup">미리 채울 상품그룹(비워두면 마지막에 등록한 그룹 없이 빈 값).</param>
    public NewMasterSkuDialog(string? suggestedItemName = null, decimal? suggestedCostPrice = null, string? suggestedProductGroup = null)
    {
        InitializeComponent(suggestedItemName, suggestedCostPrice, suggestedProductGroup);
    }

    private void InitializeComponent(string? suggestedItemName, decimal? suggestedCostPrice = null, string? suggestedProductGroup = null)
    {
        Text = "새 마스터SKU 등록";
        Size = new Size(420, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var existingItems = _itemRepository.GetAll();
        _skuText = new TextBox { Dock = DockStyle.Fill, Text = TempSkuGenerator.GenerateNext(existingItems.Select(i => i.Sku)) };
        _itemNameText = new TextBox { Dock = DockStyle.Fill, Text = suggestedItemName ?? string.Empty };

        // 상품그룹은 자유 입력이지만(ItemTable.ProductGroup은 TEXT), 오타로 그룹이 갈라지면
        // 이익분석의 상품그룹별 요약이 쪼개지므로 이미 쓰이는 그룹을 목록으로 제시한다.
        _productGroupCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        _productGroupCombo.Items.AddRange(existingItems
            .Select(i => i.ProductGroup)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase)
            .Cast<object>()
            .ToArray());
        _productGroupCombo.Text = suggestedProductGroup ?? string.Empty;

        _costPriceText = new TextBox { Dock = DockStyle.Fill, Text = (suggestedCostPrice ?? 0m).ToString("0.####") };
        _unitText = new TextBox { Dock = DockStyle.Fill, Text = "kg" };

        AddRow(layout, 0, "SKU 코드", _skuText);
        AddRow(layout, 1, "품명", _itemNameText);
        AddRow(layout, 2, "상품그룹", _productGroupCombo);
        AddRow(layout, 3, "제조원가", _costPriceText);
        AddRow(layout, 4, "단위", _unitText);

        var hint = new Label
        {
            Text = "SKU 코드는 자동 제안값(TEMP...)을 그대로 쓰거나 직접 편집할 수 있습니다.\n등록 후 마스터SKU 관리창에서 정보를 보완해주세요.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
        };
        layout.Controls.Add(hint, 0, 5);
        layout.SetColumnSpan(hint, 2);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0), Height = 40 };
        var btnCancel = new Button { Text = "취소", Size = new Size(80, 28) };
        var btnOk = new Button { Text = "등록", Size = new Size(80, 28) };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnOk.Click += OnOkClick;
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        outer.Controls.Add(layout, 0, 0);
        outer.Controls.Add(buttonPanel, 0, 1);
        Controls.Add(outer);
        CancelButton = btnCancel;
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 4, 0) }, 0, row);
        control.Margin = new Padding(3, 3, 3, 3);
        layout.Controls.Add(control, 1, row);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var sku = _skuText.Text.Trim();
        if (string.IsNullOrEmpty(sku))
        {
            MessageBox.Show("SKU 코드를 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_itemRepository.GetBySku(sku) != null)
        {
            MessageBox.Show($"이미 존재하는 SKU '{sku}'입니다. 다른 코드를 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var itemName = _itemNameText.Text.Trim();
        if (string.IsNullOrEmpty(itemName))
        {
            MessageBox.Show("품명을 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        decimal.TryParse(_costPriceText.Text, out var costPrice);
        var unit = string.IsNullOrWhiteSpace(_unitText.Text) ? "kg" : _unitText.Text.Trim();
        var productGroup = _productGroupCombo.Text.Trim();

        _itemRepository.Upsert(new ItemModel
        {
            Sku = sku,
            ItemName = itemName,
            CostPrice = costPrice,
            Unit = unit,
            ProductGroup = string.IsNullOrEmpty(productGroup) ? null : productGroup,
        });

        ResultSku = sku;
        ResultItemName = itemName;
        ResultUnit = unit;
        ResultProductGroup = string.IsNullOrEmpty(productGroup) ? null : productGroup;
        DialogResult = DialogResult.OK;
        Close();
    }
}

using MiniERP2.Database;

namespace MiniERP2.Forms;

/// <summary>
/// CSKU별 통계 그리드 행 더블클릭 시 뜨는 상세조회 다이얼로그(CSKU별통계_개발기획서.md §6).
/// ChannelSkuTable/ItemTable을 조회만 할 뿐 그리드/엑셀 본문에는 반영하지 않는다(파일 정보로만
/// 가공한다는 원칙 유지). DB에 없으면 안내 문구만 표시하고 나머지 필드는 공란으로 둔다.
/// </summary>
public class CskuDetailDialog : Form
{
    private readonly ChannelSkuRepository _channelSkuRepo;
    private readonly ItemRepository _itemRepo;
    private readonly string _channelCode;
    private readonly string _cskuCode;

    private Label _statusLabel = new();
    private Label _mskuValue = new();
    private Label _supplyPriceValue = new();
    private Label _invoiceNameValue = new();
    private Label _costPriceOverrideValue = new();
    private Label _itemNameValue = new();
    private Label _costPriceValue = new();
    private Label _productGroupValue = new();

    public CskuDetailDialog(string channelCode, string cskuCode, ChannelSkuRepository? channelSkuRepo = null, ItemRepository? itemRepo = null)
    {
        _channelCode = channelCode;
        _cskuCode = cskuCode;
        _channelSkuRepo = channelSkuRepo ?? new ChannelSkuRepository();
        _itemRepo = itemRepo ?? new ItemRepository();
        InitializeComponent();
        LoadDetail();
    }

    private void InitializeComponent()
    {
        Text = $"CSKU 상세조회 — {_channelCode} / {_cskuCode}";
        Size = new Size(420, 340);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _statusLabel = new Label { AutoSize = true, ForeColor = Color.DarkOrange, Margin = new Padding(0, 0, 0, 8) };
        layout.SetColumnSpan(_statusLabel, 2);
        layout.Controls.Add(_statusLabel, 0, 0);

        AddRow(layout, 1, "채널 CSKU 코드", $"{_channelCode} / {_cskuCode}");
        (_mskuValue, _) = AddRow(layout, 2, "매핑 MSKU");
        (_supplyPriceValue, _) = AddRow(layout, 3, "공급가 (SupplyPrice)");
        (_invoiceNameValue, _) = AddRow(layout, 4, "송장 표기명");
        (_costPriceOverrideValue, _) = AddRow(layout, 5, "CSKU 개별 원가");
        (_itemNameValue, _) = AddRow(layout, 6, "상품명 (ItemTable)");
        (_costPriceValue, _) = AddRow(layout, 7, "마스터 원가 (CostPrice)");
        (_productGroupValue, _) = AddRow(layout, 8, "상품그룹");

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 40,
        };
        var btnClose = new Button { Text = "닫기", DialogResult = DialogResult.OK, Size = new Size(72, 30) };
        buttonPanel.Controls.Add(btnClose);
        AcceptButton = btnClose;
        CancelButton = btnClose;

        Controls.Add(layout);
        Controls.Add(buttonPanel);
    }

    private (Label ValueLabel, Label KeyLabel) AddRow(TableLayoutPanel layout, int row, string label, string? value = null)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var keyLabel = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 4, 8, 4) };
        var valueLabel = new Label { Text = value ?? string.Empty, AutoSize = true, Margin = new Padding(0, 4, 0, 4), Font = new Font(Font, FontStyle.Bold) };
        layout.Controls.Add(keyLabel, 0, row);
        layout.Controls.Add(valueLabel, 1, row);
        return (valueLabel, keyLabel);
    }

    private void LoadDetail()
    {
        var csku = _channelSkuRepo.GetByChannelAndCskuCode(_channelCode, _cskuCode);
        if (csku == null)
        {
            _statusLabel.Text = "등록되지 않은 CSKU입니다.";
            return;
        }

        _mskuValue.Text = csku.Msku;
        _supplyPriceValue.Text = csku.SupplyPrice.ToString("#,##0");
        _invoiceNameValue.Text = csku.InvoiceDisplayName ?? string.Empty;
        _costPriceOverrideValue.Text = csku.CostPriceOverride?.ToString("#,##0") ?? string.Empty;

        var item = _itemRepo.GetBySku(csku.Msku);
        if (item == null) return;

        _itemNameValue.Text = item.ItemName;
        _costPriceValue.Text = item.CostPrice.ToString("#,##0");
        _productGroupValue.Text = item.ProductGroup ?? string.Empty;
    }
}

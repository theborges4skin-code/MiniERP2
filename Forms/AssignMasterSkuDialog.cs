using MiniERP2.Database;

namespace MiniERP2.Forms;

/// <summary>
/// CSKU에 연결할 마스터SKU를 고르는 다이얼로그. TempSkuGenerator로 임시 등록된(예: "TEMP004")
/// 마스터SKU를 실제 카탈로그 SKU로 정식 교체할 때 쓴다(거래처별 CSKU 관리 §3). 기존 마스터SKU
/// 중에서 고르거나, 그 자리에서 바로 새로 만들 수 있다(NewMasterSkuDialog 재사용).
/// </summary>
public class AssignMasterSkuDialog : Form
{
    private readonly ItemRepository _itemRepository = new();
    private ComboBox _skuCombo = new();

    public string? SelectedSku { get; private set; }

    public AssignMasterSkuDialog(string? currentMsku, string? suggestedItemName)
    {
        InitializeComponent(currentMsku, suggestedItemName);
    }

    private void InitializeComponent(string? currentMsku, string? suggestedItemName)
    {
        Text = "마스터SKU 지정";
        Size = new Size(420, 220);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var items = _itemRepository.GetAll().OrderBy(i => i.Sku).ToList();
        _skuCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        _skuCombo.Items.AddRange(items.Select(i => $"{i.Sku} — {i.ItemName}").Cast<object>().ToArray());
        if (!string.IsNullOrWhiteSpace(currentMsku)) _skuCombo.Text = currentMsku;

        layout.Controls.Add(new Label { Text = "마스터SKU:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(_skuCombo, 1, 0);

        var btnNew = new Button { Text = "새 마스터SKU 만들기...", Dock = DockStyle.Fill, Height = 30 };
        btnNew.Click += (s, e) =>
        {
            using var dlg = new NewMasterSkuDialog(suggestedItemName);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ResultSku == null) return;
            var label = $"{dlg.ResultSku} — {dlg.ResultItemName}";
            _skuCombo.Items.Add(label);
            _skuCombo.Text = dlg.ResultSku;
        };
        layout.Controls.Add(btnNew, 1, 1);

        var hint = new Label
        {
            Text = "기존 마스터SKU를 검색해 고르거나, 없으면 바로 새로 만들어 이 CSKU에 연결합니다.",
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
        };
        layout.Controls.Add(hint, 0, 2);
        layout.SetColumnSpan(hint, 2);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40 };
        var btnOk = new Button { Text = "확인", Size = new Size(80, 30) };
        var btnCancel = new Button { Text = "취소", Size = new Size(80, 30) };
        btnOk.Click += OnOkClick;
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var text = _skuCombo.Text.Trim();
        var sku = text.Contains(" — ") ? text[..text.IndexOf(" — ", StringComparison.Ordinal)] : text;

        if (string.IsNullOrEmpty(sku) || _itemRepository.GetBySku(sku) == null)
        {
            MessageBox.Show("등록된 마스터SKU를 선택하거나 [새 마스터SKU 만들기]로 먼저 등록하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SelectedSku = sku;
        DialogResult = DialogResult.OK;
        Close();
    }
}

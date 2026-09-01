using MiniERP2.Database;
using MiniERP2.Utils;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// CSKU에 연결할 마스터SKU를 고르는 다이얼로그. TempSkuGenerator로 임시 등록된(예: "TEMP004")
/// 마스터SKU를 실제 카탈로그 SKU로 정식 교체할 때 쓴다(거래처별 CSKU 관리 §3). 기존 마스터SKU
/// 중에서 고르거나, 그 자리에서 바로 새로 만들 수 있다(NewMasterSkuDialog 재사용).
///
/// "임시 SKU 등록"으로 급하게 만든 CSKU는 코드도 보통 "채널_TEMP005"처럼 임시 형태를 그대로
/// 물려받는다. 마스터SKU를 정식으로 바꿀 때 CSKU 코드도 같이 "정식" 값으로 바꿀 수 있게, 코드
/// 입력칸도 함께 둔다(비워두거나 그대로 두면 코드는 안 바뀜 — 기존 동작과 호환).
/// </summary>
public class AssignMasterSkuDialog : Form
{
    private readonly ItemRepository _itemRepository = new();
    private readonly string _channelName;
    private ComboBox _skuCombo = new();
    private TextBox _cskuCodeBox = new();

    public string? SelectedSku { get; private set; }

    /// <summary>확인 시점의 CSKU 코드 입력값입니다. 호출 측에서 원래 코드와 비교해 바뀌었는지 판단하세요.</summary>
    public string SelectedCskuCode { get; private set; } = "";

    public AssignMasterSkuDialog(string? currentMsku, string? suggestedItemName, string currentCskuCode, string channelName)
    {
        _channelName = channelName;
        InitializeComponent(currentMsku, suggestedItemName, currentCskuCode);
    }

    private void InitializeComponent(string? currentMsku, string? suggestedItemName, string currentCskuCode)
    {
        Text = "마스터SKU 지정";
        Size = new Size(440, 270);
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var items = _itemRepository.GetAll().OrderBy(i => i.Sku).ToList();
        // AutoCompleteMode(Suggest 계열)는 한글 IME 조합 중 텍스트가 깨지고 스페이스가 씹히는
        // WinForms 고질 버그를 유발해서 뺐다 — 드롭다운(화살표/Alt+Down)으로 목록 선택은 그대로 된다.
        _skuCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        _skuCombo.Items.AddRange(items.Select(i => $"{i.Sku} — {i.ItemName}").Cast<object>().ToArray());
        if (!string.IsNullOrWhiteSpace(currentMsku)) _skuCombo.Text = currentMsku;

        layout.Controls.Add(new Label { Text = "마스터SKU:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.Controls.Add(_skuCombo, 1, 0);

        var btnNew = new Button { Text = "새 마스터SKU 만들기...", Dock = DockStyle.Fill, Height = 30 };
        btnNew.Click += (s, e) =>
        {
            using var dlg = new NewMasterSkuDialog(suggestedItemName);
            if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK || dlg.ResultSku == null) return;
            var label = $"{dlg.ResultSku} — {dlg.ResultItemName}";
            _skuCombo.Items.Add(label);
            _skuCombo.Text = dlg.ResultSku;
        };
        layout.Controls.Add(btnNew, 1, 1);

        var cskuCodePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _cskuCodeBox = new TextBox { Width = 220, Text = currentCskuCode };
        var btnSuggestCode = new Button { Text = "추천값 채우기", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        btnSuggestCode.Click += (s, e) =>
        {
            var sku = ParseSelectedSku();
            if (string.IsNullOrEmpty(sku)) return;
            _cskuCodeBox.Text = CskuCodeGenerator.BuildDefault(_channelName, sku);
        };
        cskuCodePanel.Controls.Add(_cskuCodeBox);
        cskuCodePanel.Controls.Add(btnSuggestCode);

        layout.Controls.Add(new Label { Text = "CSKU 코드:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        layout.Controls.Add(cskuCodePanel, 1, 2);

        var hint = new Label
        {
            Text = "기존 마스터SKU를 검색해 고르거나, 없으면 바로 새로 만들어 이 CSKU에 연결합니다.\n" +
                   "CSKU 코드는 그대로 둬도 되고, 임시로 등록된 코드를 정식 코드로 바꾸고 싶으면 [추천값 채우기]를 누르거나 직접 입력하세요.",
            Dock = DockStyle.Fill,
            ForeColor = Color.DimGray,
        };
        layout.Controls.Add(hint, 0, 3);
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

    private string? ParseSelectedSku()
    {
        var text = _skuCombo.Text.Trim();
        return text.Contains(" — ") ? text[..text.IndexOf(" — ", StringComparison.Ordinal)] : (string.IsNullOrEmpty(text) ? null : text);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var sku = ParseSelectedSku();
        if (string.IsNullOrEmpty(sku) || _itemRepository.GetBySku(sku) == null)
        {
            MessageBox.Show("등록된 마스터SKU를 선택하거나 [새 마스터SKU 만들기]로 먼저 등록하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_cskuCodeBox.Text))
        {
            MessageBox.Show("CSKU 코드를 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SelectedSku = sku;
        SelectedCskuCode = _cskuCodeBox.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}

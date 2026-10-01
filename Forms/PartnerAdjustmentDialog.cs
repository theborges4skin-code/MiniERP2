using System.Globalization;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감보드 — 수동 거래처에 할인·에누리·지원금처럼 금액만 있는 조정 라인을 넣는 입력창.
/// 금액은 양수로 받아 차감(음수 단가)으로 저장하며, 입력 기준(VAT포함/별도)을 골라 VAT포함으로 환산한다.
/// </summary>
public class PartnerAdjustmentDialog : Form
{
    private readonly ComboBox _nameCombo = new();
    private readonly TextBox _amountBox = new();
    private readonly RadioButton _vatIncludedRadio = new();
    private readonly RadioButton _vatExcludedRadio = new();

    public string ItemName => _nameCombo.Text.Trim();

    /// <summary>차감할 금액(VAT포함, 양수).</summary>
    public decimal AmountVatIncluded { get; private set; }

    public PartnerAdjustmentDialog(string partyName, bool vatExcludedDefault)
    {
        Text = $"할인/에누리 추가 — {partyName}";
        Size = new Size(420, 230);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _nameCombo.Items.AddRange(["할인", "에누리", "광고비 지원", "인건비 지원"]);
        _nameCombo.Text = "할인";
        _nameCombo.Dock = DockStyle.Fill;
        layout.Controls.Add(new Label { Text = "항목명:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 0);
        layout.Controls.Add(_nameCombo, 1, 0);

        _amountBox.Dock = DockStyle.Fill;
        _amountBox.PlaceholderText = "차감할 금액(양수로 입력)";
        layout.Controls.Add(new Label { Text = "차감 금액:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 1);
        layout.Controls.Add(_amountBox, 1, 1);

        var vatPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _vatIncludedRadio.Text = "VAT포함 금액";
        _vatIncludedRadio.AutoSize = true;
        _vatExcludedRadio.Text = "VAT별도(공급가) 금액";
        _vatExcludedRadio.AutoSize = true;
        (vatExcludedDefault ? _vatExcludedRadio : _vatIncludedRadio).Checked = true;
        vatPanel.Controls.Add(_vatIncludedRadio);
        vatPanel.Controls.Add(_vatExcludedRadio);
        layout.Controls.Add(new Label { Text = "입력 기준:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 2);
        layout.Controls.Add(vatPanel, 1, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "취소", Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "확인", Size = new Size(80, 28) };
        ok.Click += OnOkClick;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(layout);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (ItemName.Length == 0)
        {
            MessageBox.Show(this, "항목명을 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!decimal.TryParse(_amountBox.Text.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount == 0)
        {
            MessageBox.Show(this, "차감 금액을 숫자로 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        amount = Math.Abs(amount); // 실수로 -를 붙여도 차감으로 처리
        AmountVatIncluded = _vatExcludedRadio.Checked ? amount * VatCalculator.VatDivisor : amount;
        DialogResult = DialogResult.OK;
        Close();
    }
}

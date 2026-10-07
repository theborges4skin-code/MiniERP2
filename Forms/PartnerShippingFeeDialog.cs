using System.Globalization;

namespace MiniERP2.Forms;

/// <summary>거래처 마감보드 [배송비 추가] — 배송비 단가(VAT포함, 기본 3,000원)와 적용 수량을 입력받는다.</summary>
public class PartnerShippingFeeDialog : Form
{
    private readonly TextBox _priceBox = new() { Text = "3000", Width = 120 };
    private readonly TextBox _qtyBox = new() { Width = 120 };

    public decimal UnitPrice { get; private set; }
    public int Qty { get; private set; }

    /// <param name="suggestedQty">이번 달 송장(묶음) 수 — 수량 기본값.</param>
    public PartnerShippingFeeDialog(string partyName, string period, int suggestedQty)
    {
        Text = $"배송비 추가 — {partyName}";
        Size = new Size(400, 220);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        _qtyBox.Text = suggestedQty > 0 ? suggestedQty.ToString(CultureInfo.InvariantCulture) : "";

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Label L(string t) => new() { Text = t, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };

        layout.Controls.Add(L("단가(VAT포함):"), 0, 0);
        layout.Controls.Add(_priceBox, 1, 0);
        layout.Controls.Add(L("적용 수량(건):"), 0, 1);
        layout.Controls.Add(_qtyBox, 1, 1);
        layout.Controls.Add(new Label
        {
            AutoSize = true, ForeColor = Color.Gray, Padding = new Padding(0, 4, 0, 0),
            Text = $"{period} 말일자 라인으로 들어갑니다. 수량 기본값 = 이번 달 송장 {suggestedQty}건",
        }, 0, 2);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 2)!, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "취소", Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "확인", Size = new Size(80, 28) };
        ok.Click += OnOk;
        buttons.Controls.AddRange([cancel, ok]);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(layout);
    }

    private void OnOk(object? sender, EventArgs e)
    {
        if (!decimal.TryParse(_priceBox.Text.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price <= 0)
        {
            MessageBox.Show(this, "단가를 0보다 큰 숫자로 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!int.TryParse(_qtyBox.Text.Replace(",", "").Trim(), out var qty) || qty <= 0)
        {
            MessageBox.Show(this, "적용 수량을 1 이상 정수로 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        UnitPrice = price;
        Qty = qty;
        DialogResult = DialogResult.OK;
        Close();
    }
}

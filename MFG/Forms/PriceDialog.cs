using System.Globalization;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Forms;

/// <summary>
/// 단가 추가 대화상자(원료 원/kg, 부자재 원/개 공용). 입력 금액은 지금 화면의 VAT 보기 기준으로 받고,
/// 저장은 VAT포함으로 환산한다(docs/MFG_PLAN.md §3-1).
/// </summary>
public class PriceDialog : Form
{
    private readonly ComboBox _supplier = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox _price = Ui.Text(140);
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 140 };
    private readonly ComboBox _source = new() { Width = 200 };
    private readonly CheckBox _uncertain = new() { Text = "단가 확실치 않음(불확실 표시)", AutoSize = true };
    private readonly TextBox _note = Ui.Text(320, multiline: true);
    private readonly bool _showIncluded;
    private readonly bool _taxFree;

    public string? SupplierCode { get; private set; }
    public double PriceIncl { get; private set; }
    public string EffectiveDate { get; private set; } = string.Empty;
    public string Source => _source.Text.Trim();
    public bool IsUncertain => _uncertain.Checked;
    public string Note => _note.Text.Trim();

    public PriceDialog(string title, string unit, IReadOnlyList<Supplier> suppliers, string? defaultSupplier, bool showIncluded, bool taxFree, bool allowUncertain)
    {
        _showIncluded = showIncluded;
        _taxFree = taxFree;
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        Width = 520;
        Height = 380;

        _supplier.Items.Add("(매입처 없음)");
        foreach (var s in suppliers) _supplier.Items.Add(s);
        _supplier.SelectedIndex = 0;
        for (var i = 1; i < _supplier.Items.Count; i++)
        {
            if (((Supplier)_supplier.Items[i]!).ChannelCode == defaultSupplier) _supplier.SelectedIndex = i;
        }
        _source.Items.AddRange(["견적", "거래명세서", "세금계산서", "사용자확정", "기타"]);
        _source.Text = "견적";
        _uncertain.Visible = allowUncertain;

        var vatText = taxFree ? "면세 품목" : Vat.ModeLabel(showIncluded);
        var form = Ui.Form(
            ("매입처", _supplier),
            ($"단가({unit}, {vatText})", _price),
            ("적용일", _date),
            ("출처", _source),
            ("", _uncertain),
            ("메모", _note));
        var hint = Ui.Label(taxFree ? "면세 품목은 VAT 보기와 관계없이 입력값 그대로 저장됩니다."
                                    : $"지금 {Vat.ModeLabel(showIncluded)} 보기입니다. 저장은 VAT포함 금액으로 됩니다.");
        hint.ForeColor = Ui.MutedFore;

        var ok = new Button { Text = "추가", Width = 90 };
        ok.Click += (_, _) => OnOk();
        var cancel = new Button { Text = "취소", Width = 90, DialogResult = DialogResult.Cancel };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 42, Padding = new Padding(6) };
        bar.Controls.AddRange([cancel, ok]);

        Controls.Add(form);
        Controls.Add(bar);
        Controls.Add(hint);
        hint.Dock = DockStyle.Bottom;
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void OnOk()
    {
        if (!Fmt.TryParse(_price.Text, out var v) || v < 0)
        {
            Ui.Warn(this, "단가를 숫자로 입력하세요.");
            return;
        }
        PriceIncl = Vat.FromView(v, _showIncluded, _taxFree);
        SupplierCode = _supplier.SelectedItem is Supplier s ? s.ChannelCode : null;
        EffectiveDate = _date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        DialogResult = DialogResult.OK;
    }
}

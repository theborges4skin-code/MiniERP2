using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 선택한 발주 줄 여러 개에 같은 주문 정보(수취인/연락처/주소/수량 등)를 한 번에 입력한다. 체크한
/// 항목만 덮어쓰고 나머지는 그대로 둔다 — 값을 입력하면 자동으로 체크된다(빈 값으로 지우려면 직접
/// 체크). 줄마다 값이 다른 경우는 엑셀에서 복사해 그리드에 Ctrl+V로 붙여넣는 쪽이 편하다.
/// </summary>
public class BulkOrderFieldDialog : Form
{
    private sealed record Field(string Label, bool Multiline, Action<OfsOrderItem, string> Apply);

    private static readonly Field[] Fields =
    [
        new("주문번호", false, (o, v) => o.OrderNo = v),
        new("상품명", false, (o, v) => o.ProductName = v),
        new("옵션명", false, (o, v) => o.OptionName = v),
        new("수량", false, (o, v) => o.Quantity = int.Parse(v)),
        new("수취인", false, (o, v) => o.Recipient = v),
        new("연락처", false, (o, v) => o.Phone = v),
        new("주소", false, (o, v) => o.Address = v),
        new("배송메시지", false, (o, v) => o.DeliveryMessage = v),
        new("메모(내부용)", true, (o, v) => o.Remark = v),
    ];

    private readonly CheckBox[] _checks = new CheckBox[Fields.Length];
    private readonly TextBox[] _texts = new TextBox[Fields.Length];

    public BulkOrderFieldDialog(int targetCount)
    {
        InitializeComponent(targetCount);
    }

    /// <summary>체크한 항목만 item에 적용한다.</summary>
    public void ApplyTo(OfsOrderItem item)
    {
        for (int i = 0; i < Fields.Length; i++)
        {
            if (_checks[i].Checked) Fields[i].Apply(item, _texts[i].Text.Trim());
        }
    }

    private void InitializeComponent(int targetCount)
    {
        Text = "선택 행 일괄 입력";
        Size = new Size(520, 470);
        MinimumSize = new Size(420, 470);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var header = new Label
        {
            Text = $"선택한 {targetCount}건에 체크한 항목을 같은 값으로 입력합니다.",
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(10, 8, 10, 0),
        };

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8, 4, 8, 4) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (int i = 0; i < Fields.Length; i++)
        {
            var field = Fields[i];
            var check = new CheckBox { Text = field.Label, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            var text = new TextBox { Dock = DockStyle.Fill, Multiline = field.Multiline };
            if (field.Multiline) text.ScrollBars = ScrollBars.Vertical;
            text.TextChanged += (s, e) => { if (text.Text.Length > 0) check.Checked = true; };
            _checks[i] = check;
            _texts[i] = text;

            table.RowStyles.Add(new RowStyle(SizeType.Absolute, field.Multiline ? 60 : 30));
            table.Controls.Add(check, 0, i);
            table.Controls.Add(text, 1, i);
        }

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8)
        };
        var btnCancel = new Button { Text = "취소", Width = 80, DialogResult = DialogResult.Cancel };
        var btnOk = new Button { Text = "적용", Width = 80 };
        btnOk.Click += OnOkClick;
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnOk);
        CancelButton = btnCancel;

        Controls.Add(table);
        Controls.Add(header);
        Controls.Add(btnPanel);
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        if (!_checks.Any(c => c.Checked))
        {
            MessageBox.Show("입력할 항목을 하나 이상 체크하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var qtyIndex = Array.FindIndex(Fields, f => f.Label == "수량");
        if (_checks[qtyIndex].Checked && (!int.TryParse(_texts[qtyIndex].Text.Trim(), out var qty) || qty < 1))
        {
            MessageBox.Show("수량은 1 이상의 숫자로 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

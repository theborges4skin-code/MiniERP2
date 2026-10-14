using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 주문 1건에 함께 보낼 품목(CSKU)을 최대 <see cref="MultiProductAssigner.MaxProductsPerShipment"/>개까지
/// 고르고 품목별 수량을 정하는 창. 결과(<see cref="Slots"/>)는 선택한 주문 줄마다 똑같이 적용된다 —
/// 첫 품목은 그 줄에, 나머지는 같은 수취인 정보로 복사한 줄에 들어가 한 묶음(합포장)이 된다.
/// </summary>
public class MultiProductAssignDialog : Form
{
    private sealed record Choice(string CskuCode, string ProductName)
    {
        public override string ToString() => $"{CskuCode}   {ProductName}";
    }

    private static readonly object NoneChoice = "(선택 안 함)";

    private readonly ManualCskuResolver _resolver;
    private readonly ComboBox[] _combos = new ComboBox[MultiProductAssigner.MaxProductsPerShipment];
    private readonly NumericUpDown[] _qtys = new NumericUpDown[MultiProductAssigner.MaxProductsPerShipment];
    private readonly NumericUpDown _orderCountSpinner = new() { Minimum = 1, Maximum = 500, Value = 1, Width = 70 };

    /// <summary>새 주문으로 추가할 때 같은 품목 구성으로 만들 주문 수(askOrderCount가 false면 항상 1).</summary>
    public int OrderCount => (int)_orderCountSpinner.Value;

    public IReadOnlyList<MultiProductAssigner.ProductSlot> Slots { get; private set; } = [];

    /// <param name="frequent">채널의 자주 쓴 CSKU(출고 이력 기준) — 각 칸의 목록에 미리 넣어둔다.</param>
    /// <param name="targetDescription">적용 대상 설명(예: "선택한 주문 10건", "새 주문").</param>
    /// <param name="askOrderCount">새 주문 추가일 때 "추가할 주문 수" 칸을 보여준다.</param>
    public MultiProductAssignDialog(
        ManualCskuResolver resolver,
        IEnumerable<(string CskuCode, string ProductName)> frequent,
        string targetDescription,
        bool askOrderCount = false)
    {
        _resolver = resolver;
        InitializeComponent(frequent.Select(f => new Choice(f.CskuCode, f.ProductName)).ToList(), targetDescription, askOrderCount);
    }

    private void InitializeComponent(List<Choice> frequent, string targetDescription, bool askOrderCount)
    {
        Text = $"여러 품목 배정 (최대 {MultiProductAssigner.MaxProductsPerShipment}개)";
        Size = new Size(720, 330);
        MinimumSize = new Size(560, 330);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var header = new Label
        {
            Text = $"적용 대상: {_resolver.ChannelName} ({_resolver.ChannelCode}) — {targetDescription}\n" +
                   $"첫 품목은 그 줄에, 나머지는 같은 수취인으로 줄을 추가해 한 송장(합포장)으로 묶습니다.\n" +
                   $"송장 한 줄에 최대 {MultiProductAssigner.MaxProductsPerShipment}품목까지 실리므로, 그 이상은 주문을 나눠 직접 입력하세요.",
            Dock = DockStyle.Top,
            Height = 62,
            Padding = new Padding(10, 6, 10, 0),
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            Padding = new Padding(8, 4, 8, 4),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        for (int i = 0; i < _combos.Length; i++)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            combo.Items.Add(NoneChoice);
            foreach (var choice in frequent) combo.Items.Add(choice);
            combo.SelectedIndex = 0;
            _combos[i] = combo;

            var qty = new NumericUpDown { Minimum = 1, Maximum = 9999, Value = 1, Width = 70 };
            _qtys[i] = qty;

            var slotIndex = i;
            var btnSearch = new Button { Text = "검색", Dock = DockStyle.Fill };
            btnSearch.Click += (s, e) => SetChoice(slotIndex, _resolver.PickBySearch(this));
            var btnMaster = new Button { Text = "마스터SKU", Dock = DockStyle.Fill };
            btnMaster.Click += (s, e) => SetChoice(slotIndex, _resolver.PickByMasterSku(this));

            var qtyPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            qtyPanel.Controls.Add(new Label { Text = "수량", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            qtyPanel.Controls.Add(qty);

            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            table.Controls.Add(new Label { Text = $"{i + 1}.", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, i);
            table.Controls.Add(combo, 1, i);
            table.Controls.Add(btnSearch, 2, i);
            table.Controls.Add(btnMaster, 3, i);
            table.Controls.Add(qtyPanel, 4, i);
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
        if (askOrderCount)
        {
            // RightToLeft 흐름이라 단위 → 칸 → 라벨 순으로 넣어야 "추가할 주문 수: [ ] 건"으로 보인다.
            btnPanel.Controls.Add(new Label { Text = "건", AutoSize = true, Margin = new Padding(4, 6, 24, 0) });
            btnPanel.Controls.Add(_orderCountSpinner);
            btnPanel.Controls.Add(new Label { Text = "추가할 주문 수:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        }
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.Add(table);
        Controls.Add(header);
        Controls.Add(btnPanel);
    }

    private void SetChoice(int slotIndex, (string CskuCode, string ProductName)? picked)
    {
        if (picked is not { } p) return;
        var combo = _combos[slotIndex];
        var existing = combo.Items.OfType<Choice>()
            .FirstOrDefault(c => c.CskuCode.Equals(p.CskuCode, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new Choice(p.CskuCode, p.ProductName);
            combo.Items.Add(existing);
        }
        combo.SelectedItem = existing;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var slots = new List<MultiProductAssigner.ProductSlot>();
        for (int i = 0; i < _combos.Length; i++)
        {
            if (_combos[i].SelectedItem is Choice choice)
                slots.Add(_resolver.MakeSlot(choice.CskuCode, choice.ProductName, (int)_qtys[i].Value));
        }

        var error = MultiProductAssigner.Validate(slots);
        if (error != null)
        {
            MessageBox.Show(error, "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Slots = slots;
        DialogResult = DialogResult.OK;
        Close();
    }
}

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감보드에서 단가를 고칠 때 "이 건만"인지 "같은 CSKU 전체"인지 고르는 창.
/// MessageBox의 [예]/[아니오]로는 어느 쪽이 어느 범위인지 매번 헷갈린다는 지적이 있어
/// (사용자 신고, 2026-09-22) 버튼에 동작 이름을 그대로 적은 전용 창으로 바꿨다.
/// </summary>
public class UnitPriceScopeDialog : Form
{
    public enum Scope { AllCsku, ThisLineOnly }

    /// <summary>[취소]로 닫으면 null.</summary>
    public Scope? SelectedScope { get; private set; }

    private readonly string _priceLabel;
    private readonly string _allCskuDescription;
    private readonly string _thisLineDescription;

    public UnitPriceScopeDialog(string priceLabel, string allCskuDescription, string thisLineDescription)
    {
        _priceLabel = priceLabel;
        _allCskuDescription = allCskuDescription;
        _thisLineDescription = thisLineDescription;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "단가 변경 범위 선택";
        Size = new Size(560, 250);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 8), ColumnCount = 1 };

        layout.Controls.Add(new Label
        {
            Text = $"단가를 {_priceLabel}으로 변경합니다. 어느 범위에 적용할까요?",
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            Margin = new Padding(0, 0, 0, 12),
        });
        layout.Controls.Add(new Label
        {
            Text = "· 전체 CSKU 수정 — " + _allCskuDescription,
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            Margin = new Padding(0, 0, 0, 6),
        });
        layout.Controls.Add(new Label
        {
            Text = "· 해당건 CSKU 수정 — " + _thisLineDescription,
            AutoSize = true,
            MaximumSize = new Size(510, 0),
        });

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "취소", Size = new Size(90, 30) };
        var btnThisLine = new Button { Text = "해당건 CSKU 수정", Size = new Size(140, 30) };
        var btnAll = new Button { Text = "전체 CSKU 수정", Size = new Size(140, 30) };

        btnCancel.Click += (s, e) => { SelectedScope = null; DialogResult = DialogResult.Cancel; Close(); };
        btnThisLine.Click += (s, e) => { SelectedScope = Scope.ThisLineOnly; DialogResult = DialogResult.OK; Close(); };
        btnAll.Click += (s, e) => { SelectedScope = Scope.AllCsku; DialogResult = DialogResult.OK; Close(); };

        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnThisLine);
        buttonPanel.Controls.Add(btnAll);

        Controls.Add(layout);
        Controls.Add(buttonPanel);

        // 실수로 엔터를 쳐도 넓은 범위가 적용되지 않도록 기본 버튼은 "해당건"으로 둔다.
        AcceptButton = btnThisLine;
        CancelButton = btnCancel;
    }
}

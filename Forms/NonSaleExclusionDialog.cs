using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감보드 라인 상세에서 정상 매출 라인을 매출마감제외(비매출)로 재분류할 때 구분(샘플/CS/
/// 기타)과 사유(비고)를 입력받는다. 에누리/할인/무상발송 등은 전용 구분값을 새로 만들지 않고
/// "기타"로 묶은 뒤 사유 칸에 자유 입력해 구분한다(사용자 결정).
/// </summary>
public class NonSaleExclusionDialog : Form
{
    private readonly ComboBox _kindCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly TextBox _reasonBox = new() { Width = 280 };

    public string LineKind => _kindCombo.SelectedItem?.ToString() ?? LineKinds.Other;
    public string Reason => _reasonBox.Text.Trim();

    public NonSaleExclusionDialog(int lineCount)
    {
        InitializeComponent(lineCount);
    }

    private void InitializeComponent(int lineCount)
    {
        Text = "매출마감제외 처리";
        Size = new Size(360, 220);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, AutoSize = true };
        layout.Controls.Add(new Label
        {
            Text = $"선택한 {lineCount}건을 매출 집계에서 제외합니다.\n출고 이력(OutboundDetail)은 그대로 남고, '비매출 내역'에서 계속 추적됩니다.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        });
        layout.Controls.Add(new Label { Text = "구분:", AutoSize = true });
        _kindCombo.Items.AddRange(LineKinds.All);
        _kindCombo.SelectedIndex = Array.IndexOf(LineKinds.All, LineKinds.Other);
        layout.Controls.Add(_kindCombo);
        layout.Controls.Add(new Label { Text = "사유(비고, 예: 에누리/할인/무상발송 — 선택):", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
        layout.Controls.Add(_reasonBox);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40 };
        var btnOk = new Button { Text = "확인", Size = new Size(80, 30) };
        var btnCancel = new Button { Text = "취소", Size = new Size(80, 30) };
        btnOk.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}

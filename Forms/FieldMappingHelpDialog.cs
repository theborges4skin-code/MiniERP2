namespace MiniERP2.Forms;

/// <summary>
/// 채널설정 창의 "발주서 매핑"/"정산서 매핑" 탭에서 각 입력란이 무엇을 의미하는지
/// 구체적인 예시와 함께 설명하는 도움말 창입니다.
/// </summary>
public class FieldMappingHelpDialog : Form
{
    public FieldMappingHelpDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "필드 매핑 도움말";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(560, 480);
        MinimizeBox = false;
        MaximizeBox = false;

        var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            Font = new Font(Font.FontFamily, 10),
            Text = HelpText,
        };

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var btnClose = new Button { Text = "닫기", Width = 80 };
        btnClose.Click += (s, e) => Close();
        buttonPanel.Controls.Add(btnClose);

        var contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };
        contentPanel.Controls.Add(textBox);

        Controls.Add(contentPanel);
        Controls.Add(buttonPanel);

        AcceptButton = btnClose;
        CancelButton = btnClose;
    }

    private const string HelpText =
        """
        각 표준 필드(상품명, 옵션명, 수량 등)마다 엑셀 파일의 어떤 위치에서
        값을 읽어올지 아래 3가지 항목으로 지정합니다.

        - 시트 이름: 값이 들어있는 엑셀 시트의 이름입니다. 비워두면 첫 번째 시트를 사용합니다.
        - 헤더 행: 열 제목(헤더)이 적힌 행 번호입니다. (1부터 시작)
        - 열: 엑셀의 헤더 행에서 실제로 찾을 열 제목(헤더 텍스트)입니다.

        예시
        ----
        시트 이름 = "AA", 헤더 행 = 3, 열 = "BB" 로 설정하면:
        엑셀의 "AA"라는 이름을 가진 시트에서, 3번째 행을 헤더(제목) 행으로
        보고, 그 헤더들 중 "BB"라는 글자가 적힌 열을 찾아, 그 열의 4행부터
        끝까지의 값을 이 표준 필드의 데이터로 가져옵니다.

        한 채널 안에서도 표준 필드마다 시트/헤더 행을 다르게 지정할 수
        있습니다. 예를 들어 "상품명"은 "주문" 시트 2행 헤더에서, "배송비"는
        "비용" 시트 1행 헤더에서 각각 읽어오도록 설정할 수 있습니다.

        "열"을 빈칸으로 두면 해당 표준 필드는 이 채널에서 사용하지
        않는다는 뜻이며, 읽기 단계에서 무시됩니다.

        발주서 매핑 vs 정산서 매핑
        ----
        같은 채널이라도 발주서(주문) 파일과 정산서(매출/정산) 파일은
        보통 양식이 다릅니다. 이 둘은 서로 다른 탭에서 독립적으로
        설정하므로, 한쪽을 바꿔도 다른 쪽에는 영향이 없습니다.
        """;
}

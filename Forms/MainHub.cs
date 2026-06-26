using MiniERP2.UI;

namespace MiniERP2.Forms;

public class MainHub : Form
{
    public MainHub()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "MiniERP2 - Main Hub";
        Size = new Size(1024, 768);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            ColumnStyles = { new ColumnStyle(SizeType.Absolute, 200), new ColumnStyle(SizeType.Percent, 100) }
        };

        var sidebar = CreateSidebar();
        var contentPanel = CreateContentPanel();

        mainLayout.Controls.Add(sidebar, 0, 0);
        mainLayout.Controls.Add(contentPanel, 1, 0);

        Controls.Add(mainLayout);
    }

    private Control CreateSidebar()
    {
        var sidebarPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(10)
        };

        var titleLabel = new Label
        {
            Text = "MiniERP2",
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 20)
        };

        sidebarPanel.Controls.Add(titleLabel);

        // 기획서 0절의 화면 목록에 따라 메뉴 버튼 생성
        var ofsButton = CreateMenuButton("OFS (발주처리)", (s, e) => { FormManager.Show<OfsForm>(); });
        ofsButton.Enabled = true;
        sidebarPanel.Controls.Add(ofsButton);
        
        var masterSkuButton = CreateMenuButton("마스터SKU 관리", (s, e) => { FormManager.Show<MasterSkuForm>(); });
        masterSkuButton.Enabled = true; // 버튼 활성화
        sidebarPanel.Controls.Add(masterSkuButton);

        var mappingButton = CreateMenuButton("매핑 관리", (s, e) => { FormManager.Show<MappingForm>(); });
        mappingButton.Enabled = true;
        sidebarPanel.Controls.Add(mappingButton);

        var channelConfigButton = CreateMenuButton("채널 설정", (s, e) => { FormManager.Show<ChannelConfigForm>(); });
        channelConfigButton.Enabled = true;
        sidebarPanel.Controls.Add(channelConfigButton);

        sidebarPanel.Controls.Add(CreateMenuButton("마감/이익분석", (s, e) => { /* FormManager.Show<SettlementForm>(); */ }));
        sidebarPanel.Controls.Add(CreateMenuButton("기타/문서관리", (s, e) => { /* FormManager.Show<DocsForm>(); */ }));

        return sidebarPanel;
    }

    private Button CreateMenuButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(160, 40),
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(5)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += onClick;
        // TODO: 각 기능별 폼이 만들어지면 주석을 해제합니다.
        button.Enabled = false; 
        return button;
    }

    private Control CreateContentPanel()
    {
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20)
        };

        var welcomeLabel = new Label
        {
            Text = "메인 허브에 오신 것을 환영합니다.\n왼쪽 메뉴에서 작업을 선택하세요.",
            Font = new Font(Font.FontFamily, 12),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        // TODO: 기획서 5.1절 '마스터 데이터 동기화 상태 요약 표시' 영역 구현

        contentPanel.Controls.Add(welcomeLabel);
        return contentPanel;
    }
}
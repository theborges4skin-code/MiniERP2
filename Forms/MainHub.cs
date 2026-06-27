using MiniERP2.Database;
using MiniERP2.UI;

namespace MiniERP2.Forms;

public class MainHub : Form
{
    private readonly ItemRepository _itemRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly SettlementRepository _settlementRepository = new();

    private Label _summaryLabel = new();

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

        Activated += (s, e) => RefreshSummary();
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

        var settlementButton = CreateMenuButton("마감/이익분석", (s, e) => { FormManager.Show<SettlementForm>(); });
        settlementButton.Enabled = true;
        sidebarPanel.Controls.Add(settlementButton);

        sidebarPanel.Controls.Add(CreateMenuButton("기타/문서관리", (s, e) => { /* FormManager.Show<DocsForm>(); */ }));

        var legacyImportButton = CreateMenuButton("레거시 데이터 가져오기", OnLegacyImportClick);
        legacyImportButton.Enabled = true;
        sidebarPanel.Controls.Add(legacyImportButton);

        var dataManagementButton = CreateMenuButton("데이터 관리", (s, e) => { FormManager.Show<DataManagementForm>(); });
        dataManagementButton.Enabled = true;
        sidebarPanel.Controls.Add(dataManagementButton);

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

        _summaryLabel = new Label
        {
            Font = new Font(Font.FontFamily, 12),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
        };

        contentPanel.Controls.Add(_summaryLabel);
        RefreshSummary();
        return contentPanel;
    }

    /// <summary>
    /// 기획서 5.1절 '마스터 데이터 동기화 상태 요약 표시'. 창이 활성화될 때마다 다시 집계한다.
    /// </summary>
    private void RefreshSummary()
    {
        if (_summaryLabel.IsDisposed) return;

        var channelCount = _salesChannelRepository.GetAll().Count;
        var itemCount = _itemRepository.GetAll().Count;
        var settlementChannelsWithData = _salesChannelRepository.GetAll()
            .Count(c => _settlementRepository.GetByChannel(c.ChannelCode).Count > 0);

        _summaryLabel.Text =
            "메인 허브에 오신 것을 환영합니다.\n왼쪽 메뉴에서 작업을 선택하세요.\n\n" +
            "── 마스터 데이터 현황 ──\n" +
            $"등록된 채널: {channelCount}개\n" +
            $"등록된 마스터SKU: {itemCount}개\n" +
            $"이익분석 결과가 저장된 채널: {settlementChannelsWithData}개";
    }

    private void OnLegacyImportClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "SQLite DB (*.sqlite)|*.sqlite|All files (*.*)|*.*",
            Title = "구버전 MiniERP(V3) ERP_Database.sqlite 파일을 선택하세요",
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        var confirm = MessageBox.Show(
            "선택한 구버전 DB의 채널 설정/마스터SKU/매핑 규칙(1:1/예외/조건부)/채널별 납품가를 가져옵니다.\n" +
            "기존에 같은 채널코드/SKU가 있으면 갱신되고, 1:1/예외 매핑 규칙은 기존 규칙과 병합됩니다.\n" +
            "조건부 매핑 규칙은 재실행 시 중복으로 추가될 수 있으니 같은 DB로 두 번 가져오지 마세요.\n\n계속하시겠습니까?",
            "레거시 데이터 가져오기", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        Cursor = Cursors.WaitCursor;
        try
        {
            var service = new LegacyMigrationService();
            var result = service.Migrate(ofd.FileName);

            MessageBox.Show(
                $"가져오기 완료.\n\n채널: {result.ChannelsImported}개\n마스터SKU: {result.ItemsImported}개\n" +
                $"임시SKU(레거시): {result.TempSkusImported}개\n채널별 납품가: {result.ChannelSkusImported}건\n" +
                $"매핑 규칙(1:1+예외): {result.RulesImported}건\n조건부 매핑 규칙(다중조건): {result.ConditionRulesImported}건\n\n" +
                "※ 택배사 양식은 이번 가져오기에 포함되지 않았습니다. 필요하면 택배사양식관리창에서 직접 입력해주세요.\n" +
                "※ 조건부 매핑 규칙은 매핑관리창에 전용 편집 UI가 아직 없습니다 — 그 채널의 '조건부 매핑' 탭에서 저장을 누르면 가져온 내용이 사라지니 주의하세요.",
                "가져오기 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);

            RefreshSummary();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"가져오기 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }
}

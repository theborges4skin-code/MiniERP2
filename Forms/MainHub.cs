using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

public class MainHub : Form
{
    private readonly ItemRepository _itemRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly FbaBoxSpecRepository _fbaBoxSpecRepository = new();
    private readonly SettlementRepository _settlementRepository = new();
    private readonly AutoOrderInboxRepository _autoOrderInboxRepository = new();
    private readonly AutoOrderSettingsService _autoOrderSettingsService = new();

    private Label _summaryLabel = new();
    private System.Windows.Forms.Timer _autoOrderPollTimer = new();

    private TextBox _searchBox = new();
    private ListBox _searchResultsBox = new();
    private List<FeatureIndexEntry> _searchMatches = new();

    public MainHub()
    {
        InitializeComponent();
        _salesChannelRepository.EnsureSampleChannel();
        _fbaBoxSpecRepository.EnsureDefaultBoxSpecs();
        Load += OnMainHubLoad;
    }

    private void InitializeComponent()
    {
        Text = "MiniERP2 - Main Hub";
        Size = new Size(1200, 800);
        StartPosition = FormStartPosition.CenterScreen;

        var groups = BuildMenuGroups();
        var contentPanel = CreateContentPanel(groups);
        var menuStrip = BuildMenuStrip(groups);

        // Dock 처리 순서상 Fill 패널을 먼저 추가하고 Top 메뉴스트립을 나중에 추가해야
        // 메뉴스트립이 상단을 온전히 차지하고 나머지 공간을 Fill 패널이 채운다.
        Controls.Add(contentPanel);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;

        // 검색 결과 목록은 레이아웃 공간을 고정으로 차지하지 않도록 Form에 직접 얹은 뒤(절대좌표),
        // 입력이 있을 때만 검색창 바로 아래에 위치를 계산해 띄운다(자동완성 드롭다운 흉내).
        Controls.Add(_searchResultsBox);

        KeyPreview = true;
        KeyDown += OnMainHubKeyDown;

        Activated += (s, e) => RefreshSummary();
        FormClosing += (s, e) => _autoOrderPollTimer.Stop();
    }

    /// <summary>
    /// 화면 목록을 업무 흐름 기준 6개 그룹으로 묶는다. 상단 메뉴(그룹명 → 드롭다운)와 중앙부
    /// 버튼 배치(그룹명 → 그룹박스)가 이 목록 하나를 공유해서 만들어지므로, 화면이 추가/이동되면
    /// 여기 한 곳만 고치면 된다. 단축키(Shortcut)는 Ctrl+1~8 / Ctrl+F1~F8 범위 안에서 배정하며,
    /// ToolStripMenuItem.ShortcutKeys에 등록하는 것만으로 전역 단축키로 동작한다(별도 키 후킹 불필요).
    /// "레거시 데이터 가져오기"는 사용 빈도가 낮아 단축키를 배정하지 않았다(null).
    /// </summary>
    private List<(string GroupTitle, List<(string Text, EventHandler Handler, Keys? Shortcut)> Actions)> BuildMenuGroups() => new()
    {
        ("발주/배송", new()
        {
            ("OFS (발주처리)", (s, e) => FormManager.Show<OfsForm>(), Keys.Control | Keys.D3),
            ("발주/출고 이력", (s, e) => FormManager.Show<OutboundHistoryForm>(), Keys.Control | Keys.D4),
            ("운송장 파일 누락건 점검", (s, e) => TrackingBackfillCheckFlow.Run(this), null),
            ("풀필먼트 발주 처리", (s, e) => FormManager.Show<FboOrderForm>(), Keys.Control | Keys.D6),
            ("풀필먼트 발주 이력", (s, e) => FormManager.Show<FboHistoryForm>(), Keys.Control | Keys.D7),
            ("자동발주처리", (s, e) => FormManager.Show<AutoOrderInboxForm>(), Keys.Control | Keys.D9),
            ("FBA 발주 처리", (s, e) => FormManager.Show<FbaOrderForm>(), null),
            ("FBA 발주 이력", (s, e) => FormManager.Show<FbaHistoryForm>(), null),
        }),
        ("기준정보", new()
        {
            ("마스터SKU 관리", (s, e) => FormManager.Show<MasterSkuForm>(), Keys.Control | Keys.D1),
            ("거래처별 CSKU 관리", (s, e) => FormManager.Show<ChannelCskuForm>(), null),
            ("매핑 관리", (s, e) => FormManager.Show<MappingForm>(), Keys.Control | Keys.D5),
            ("채널 설정", (s, e) => FormManager.Show<ChannelConfigForm>(), Keys.Control | Keys.D2),
            ("견적·단가 관리", (s, e) => FormManager.Show<PriceQuoteForm>(), Keys.Control | Keys.F7),
            ("배송지 주소록 관리", (s, e) => FormManager.Show<AddressBookForm>(), null),
            ("간이 마진 계산기", (s, e) => FormManager.Show<MarginCalculatorForm>(), null),
            ("정산 마진 계산기", (s, e) => FormManager.Show<SimpleMarginCalculatorForm>(), null),
        }),
        ("정산", new()
        {
            ("마감/이익분석", (s, e) => FormManager.Show<SettlementForm>(), Keys.Control | Keys.F1),
            ("광고비 분석", (s, e) => FormManager.Show<AdMappingForm>(), Keys.Control | Keys.F2),
            ("월별 마감 자동화", (s, e) => FormManager.Show<MonthlyClosingForm>(), Keys.Control | Keys.F3),
            ("거래처 마감보드", (s, e) => FormManager.Show<PartnerClosingForm>(), null),
            ("온라인 거래처 취합", (s, e) => FormManager.Show<PartnerConsolidationForm>(), null),
        }),
        ("보고서", new()
        {
            ("종합보고서", (s, e) => FormManager.Show<ReportForm>(), Keys.Control | Keys.F4),
            ("수출요약보고서", (s, e) => FormManager.Show<ExportSummaryForm>(), Keys.Control | Keys.F8),
            ("CSKU별 통계", (s, e) => FormManager.Show<CskuStatForm>(), null),
        }),
        ("문서관리", new()
        {
            ("문서관리", (s, e) => FormManager.Show<DocLineHistoryForm>(), Keys.Control | Keys.F5),
            ("전체 문서 작성(고급)", (s, e) => FormManager.Show<DocsForm>(), Keys.Control | Keys.F9),
            ("거래명세표 조회/내보내기", (s, e) => FormManager.Show<DocStatementBrowserForm>(), Keys.Control | Keys.F6),
        }),
        ("데이터관리", new()
        {
            ("데이터 관리", (s, e) => FormManager.Show<DataManagementForm>(), Keys.Control | Keys.D8),
            ("레거시 데이터 가져오기", OnLegacyImportClick, null),
        }),
    };

    private MenuStrip BuildMenuStrip(List<(string GroupTitle, List<(string Text, EventHandler Handler, Keys? Shortcut)> Actions)> groups)
    {
        var menuStrip = new MenuStrip { Dock = DockStyle.Top };
        foreach (var (groupTitle, actions) in groups)
        {
            var topItem = new ToolStripMenuItem(groupTitle);
            foreach (var (text, handler, shortcut) in actions)
            {
                var item = new ToolStripMenuItem(text);
                item.Click += handler;
                if (shortcut.HasValue) item.ShortcutKeys = shortcut.Value;
                topItem.DropDownItems.Add(item);
            }
            menuStrip.Items.Add(topItem);
        }
        return menuStrip;
    }

    private Control CreateContentPanel(List<(string GroupTitle, List<(string Text, EventHandler Handler, Keys? Shortcut)> Actions)> groups)
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(20),
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var searchPanel = BuildSearchPanel();

        _summaryLabel = new Label
        {
            Font = new Font(Font.FontFamily, 11),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
        };

        var groupsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
        };
        foreach (var (groupTitle, actions) in groups)
            groupsFlow.Controls.Add(BuildGroupBox(groupTitle, actions));

        outer.Controls.Add(searchPanel, 0, 0);
        outer.Controls.Add(_summaryLabel, 0, 1);
        outer.Controls.Add(groupsFlow, 0, 2);

        RefreshSummary();
        return outer;
    }

    /// <summary>
    /// 기능 검색창. 메인 화면 버튼뿐 아니라 하위 창(예: 발주/출고 이력 안의 "운송장 파일 누락건
    /// 점검 > 택배운임 통계")까지 <see cref="FeatureIndex"/>에 등록된 항목을 실시간으로 찾아준다.
    /// </summary>
    private Control BuildSearchPanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };

        panel.Controls.Add(new Label { Text = "기능 검색(Ctrl+K):", AutoSize = true, Padding = new Padding(0, 9, 4, 0) });

        _searchBox = new TextBox { Width = 420, Margin = new Padding(0, 5, 0, 0) };
        _searchBox.TextChanged += OnSearchTextChanged;
        _searchBox.KeyDown += OnSearchBoxKeyDown;
        panel.Controls.Add(_searchBox);

        panel.Controls.Add(new Label
        {
            Text = "메인 화면 버튼뿐 아니라 하위 창의 세부 기능까지 검색합니다. ↓/Enter로 선택, Esc로 닫기.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(10, 11, 0, 0),
        });

        _searchResultsBox = new ListBox { Width = 460, Height = 240, Visible = false, IntegralHeight = false };
        _searchResultsBox.Click += (_, _) => ActivateSelectedSearchResult();
        _searchResultsBox.KeyDown += OnSearchResultsKeyDown;

        return panel;
    }

    private void OnSearchTextChanged(object? sender, EventArgs e)
    {
        var query = _searchBox.Text.Trim();
        if (query.Length == 0)
        {
            HideSearchResults();
            return;
        }

        _searchMatches = FeatureIndex.All
            .Where(f => f.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .ToList();

        _searchResultsBox.Items.Clear();
        if (_searchMatches.Count == 0)
        {
            _searchResultsBox.Items.Add("검색 결과가 없습니다.");
            _searchResultsBox.Enabled = false;
        }
        else
        {
            _searchResultsBox.Enabled = true;
            foreach (var match in _searchMatches) _searchResultsBox.Items.Add(match.DisplayText);
            _searchResultsBox.SelectedIndex = 0;
        }

        _searchResultsBox.Location = PointToClient(_searchBox.PointToScreen(new Point(0, _searchBox.Height + 2)));
        _searchResultsBox.Visible = true;
        _searchResultsBox.BringToFront();
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && _searchResultsBox.Visible && _searchResultsBox.Items.Count > 0)
        {
            _searchResultsBox.Focus();
            _searchResultsBox.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            ActivateSelectedSearchResult();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            HideSearchResults();
        }
    }

    private void OnSearchResultsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ActivateSelectedSearchResult();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            HideSearchResults();
            _searchBox.Focus();
        }
    }

    private void ActivateSelectedSearchResult()
    {
        var index = _searchResultsBox.SelectedIndex;
        if (index < 0 || index >= _searchMatches.Count) return;

        var entry = _searchMatches[index];
        HideSearchResults();
        _searchBox.Clear();
        entry.Open();
    }

    private void HideSearchResults() => _searchResultsBox.Visible = false;

    private void OnMainHubKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.K)
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
            e.Handled = true;
        }
    }

    private Control BuildGroupBox(string title, List<(string Text, EventHandler Handler, Keys? Shortcut)> actions)
    {
        var box = new GroupBox
        {
            Text = title,
            Size = new Size(240, 46 + actions.Count * 50),
            Margin = new Padding(10),
            Padding = new Padding(8, 4, 8, 8),
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        foreach (var (text, handler, shortcut) in actions)
            flow.Controls.Add(BuildButtonRow(text, handler, shortcut));

        box.Controls.Add(flow);
        return box;
    }

    /// <summary>버튼 줄 바깥 왼쪽에 단축키 표시 라벨을 붙인 한 줄([단축키][버튼])을 만든다.</summary>
    private Control BuildButtonRow(string text, EventHandler onClick, Keys? shortcut)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0),
        };

        var shortcutLabel = new Label
        {
            Text = shortcut.HasValue ? FormatShortcut(shortcut.Value) : "",
            Size = new Size(58, 40),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = SystemColors.GrayText,
            Font = new Font(Font.FontFamily, 8),
            Margin = new Padding(0, 5, 2, 5),
        };

        row.Controls.Add(shortcutLabel);
        row.Controls.Add(CreateMenuButton(text, onClick));
        return row;
    }

    private static string FormatShortcut(Keys shortcut)
    {
        var key = shortcut & Keys.KeyCode;
        var keyLabel = key is >= Keys.D0 and <= Keys.D9 ? ((int)(key - Keys.D0)).ToString() : key.ToString();
        return $"Ctrl+{keyLabel}";
    }

    private Button CreateMenuButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(160, 40),
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(5),
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += onClick;
        return button;
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
        var autoOrderNewCount = _autoOrderInboxRepository.CountNew();

        _summaryLabel.Text =
            "메인 허브에 오신 것을 환영합니다.\n상단 메뉴 또는 아래 버튼에서 작업을 선택하세요.\n\n" +
            "── 마스터 데이터 현황 ──\n" +
            $"등록된 채널: {channelCount}개\n" +
            $"등록된 마스터SKU: {itemCount}개\n" +
            $"이익분석 결과가 저장된 채널: {settlementChannelsWithData}개\n" +
            (autoOrderNewCount > 0 ? $"▶ 자동발주처리 확인 필요: {autoOrderNewCount}건" : "자동발주처리: 신규 없음");
    }

    /// <summary>
    /// 자동발주처리 폴링 3경로 중 "시작 시 1회"+"30분 타이머"(02_자동발주처리_MiniERP2연동_설계.md §4).
    /// 아직 인증 전이면(캐시된 로그인 없음) 백그라운드에서 브라우저를 불쑥 띄우지 않도록 조용히
    /// 건너뛴다 — 사용자가 자동발주처리 창의 [지금 확인]/[연동 설정]에서 명시적으로 인증해야 한다.
    /// </summary>
    private void OnMainHubLoad(object? sender, EventArgs e)
    {
        var settings = _autoOrderSettingsService.Load();

        _autoOrderPollTimer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(5, settings.PollingIntervalMinutes) * 60 * 1000,
        };
        _autoOrderPollTimer.Tick += async (_, _) => await PollAutoOrdersQuietlyAsync();
        _autoOrderPollTimer.Start();

        if (settings.PollOnStartup)
        {
            _ = PollAutoOrdersQuietlyAsync();
        }
    }

    private async Task PollAutoOrdersQuietlyAsync()
    {
        try
        {
            var settings = _autoOrderSettingsService.Load();
            if (!settings.IsConfigured) return;

            var client = new GoogleDriveAutoOrderClient(_autoOrderSettingsService);
            var pollingService = new AutoOrderPollingService(client, _autoOrderInboxRepository);
            await pollingService.PollAsync(allowInteractiveAuth: false);
        }
        catch
        {
            // 백그라운드 자동 폴링 실패는 조용히 넘어간다 — 사용자는 자동발주처리 창의
            // [지금 확인]으로 수동 확인 시 구체적인 오류를 볼 수 있다.
        }

        if (!IsDisposed) BeginInvoke(RefreshSummary);
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
                "※ 택배사 양식은 이번 가져오기에 포함되지 않았습니다. 필요하면 택배사양식관리창에서 직접 입력해주세요.",
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

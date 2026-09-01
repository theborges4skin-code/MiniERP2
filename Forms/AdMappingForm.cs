using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.DataLoaders;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 광고비 파일을 채널별로 불러와 상품그룹 단위로 매핑하는 창입니다(SalesManagerV2의 광고매핑
/// 인터페이스를 본떴습니다 — 마스터SKU/CSKU 매핑과 달리 1:1 매핑 단계 없이 임시/조건부 매핑만
/// 사용하고, 대상은 상품그룹입니다). 채널마다 광고 리포트 헤더 구성이 다양하므로 "필드 매핑"
/// 탭에서 시트/헤더행/열을 직접 설정해 실제 파일로 테스트하며 맞춰가는 것을 전제로 합니다.
/// </summary>
public class AdMappingForm : Form
{
    private readonly AdMappingRepository _adMappingRepository = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();
    private readonly DocPartyRepository _docPartyRepository = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly SettingsService _settingsService = new();
    private readonly AdSpendLoader _adSpendLoader = new();
    private readonly AdLegacyMigrationService _legacyMigrationService = new();
    private readonly ProfitFactRepository _profitFactRepository = new();
    private readonly AdChannelSplitRepository _channelSplitRepository = new();

    /// <summary>null이면 채널 분리 기능이 꺼져있는 채널(대부분의 채널). 켜져 있으면 파일을 불러올
    /// 때마다 이 리졸버로 RESOLVED_CHANNEL/CHANNEL_MATCH_TYPE을 채운다.</summary>
    private AdChannelSplitResolver? _channelSplitResolver;

    // 채널 분리 사용 여부/캠페인 소스 헤더는 DB(AdChannelSplitSettings)에 저장하고 여기 캐시해둔다.
    private bool _channelSplitEnabled;
    private List<string> _channelSplitCampaignSourceHeaders = [];

    private Label _channelDisplayLabel = new();
    private TabControl _tabControl = new();
    private ChannelConfig? _currentChannelConfig;
    private SalesChannel? _selectedChannel;

    /// <summary>채널 선택 팝업에서 항상 맨 위에 고정·펼침 상태로 보여줄 그룹명.
    /// 광고 매핑은 온라인 채널에서만 쓰이므로 "온라인" 폴더를 고정한다.</summary>
    private const string OnlineGroupName = "온라인";

    // "광고비 데이터" 탭
    private ExcelLikeDataGridView _adDataGrid = new();
    private DataGridView _adGroupGrid = new();
    private Label _adSummaryLabel = new();
    private List<AdSpendItem> _loadedAdItems = [];

    // "임시 매핑" 탭
    private DataGridView _tempRuleGrid = new();

    // "조건부 매핑(상세)" 탭
    private TabPage _conditionDetailTabPage = new();
    private DataGridView _conditionRuleGrid = new();
    private DataGridView _conditionDetailGrid = new();
    private TextBox _conditionKeyTextBox = new();
    private TextBox _conditionTargetGroupTextBox = new();
    private Label _conditionPreviewLabel = new();
    private Label _conditionSaveFeedbackLabel = new();
    private long _selectedConditionRuleId = -1;

    // "예외 처리" 탭(행 필터)
    private DataGridView _exceptionGrid = new();

    private CheckBox _unmappedOnlyCheckBox = new();
    private CheckBox _unclassifiedOnlyCheckBox = new();

    // "채널 분리 규칙" 탭
    private TabPage _channelSplitTabPage = new();
    private CheckBox _channelSplitEnabledCheckBox = new();
    private TextBox _campaignSourceHeadersTextBox = new();
    private Label _channelSplitSettingsFeedbackLabel = new();
    private DataGridView _channelSplitInventoryGrid = new();
    private Label _channelSplitInventoryFeedbackLabel = new();
    private DataGridView _channelSplitPreruleGrid = new();
    private DataGridView _channelSplitPreruleDetailGrid = new();
    private NumericUpDown _channelSplitPriorityInput = new();
    private ComboBox _channelSplitPreruleTargetChannelCombo = new();
    private TextBox _channelSplitPreruleNoteTextBox = new();
    private CheckBox _channelSplitPreruleEnabledCheckBox = new();
    private Label _channelSplitPrerulePreviewLabel = new();
    private Label _channelSplitPreruleSaveFeedbackLabel = new();
    private long _selectedChannelSplitPreruleId = -1;

    private static readonly (AdStdField Field, string Label)[] AdFields =
    [
        (AdStdField.ProductName, "상품명"),
        (AdStdField.ProductId, "상품번호/캠페인"),
        (AdStdField.OptionName, "옵션명"),
        (AdStdField.Cost, "광고비"),
        (AdStdField.Extra1, "추가항목1"),
        (AdStdField.Extra2, "추가항목2"),
        (AdStdField.Note1, "비고1"),
        (AdStdField.Note2, "비고2"),
        (AdStdField.Note3, "비고3"),
    ];

    public AdMappingForm()
    {
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
        LoadChannels();
    }

    private void InitializeComponent()
    {
        Text = "광고 매핑";
        Size = new Size(1100, 750);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        topPanel.Controls.Add(new Label { Text = "채널:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        _channelDisplayLabel = new Label
        {
            Text = "(선택 안 됨)",
            AutoSize = false,
            Size = new Size(180, 25),
            BorderStyle = BorderStyle.Fixed3D,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
        };
        topPanel.Controls.Add(_channelDisplayLabel);
        var btnSelectChannel = new Button { Text = "채널 선택...", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnSelectChannel.Click += OnSelectChannelClick;
        topPanel.Controls.Add(btnSelectChannel);

        var btnLegacyImport = new Button { Text = "SalesManagerV2 데이터 가져오기", AutoSize = true, Padding = new Padding(8, 5, 8, 5) };
        btnLegacyImport.Click += OnLegacyImportClick;
        topPanel.Controls.Add(btnLegacyImport);

        _tabControl = new TabControl { Dock = DockStyle.Fill };
        _tabControl.TabPages.Add(CreateAdDataTabPage());
        _tabControl.TabPages.Add(CreateTempRuleTabPage());
        _conditionDetailTabPage = CreateConditionDetailTabPage();
        _tabControl.TabPages.Add(_conditionDetailTabPage);
        _tabControl.TabPages.Add(CreateExceptionTabPage());
        _channelSplitTabPage = CreateChannelSplitTabPage();
        _tabControl.TabPages.Add(_channelSplitTabPage);
        mainLayout.Controls.Add(topPanel, 0, 0);
        mainLayout.Controls.Add(_tabControl, 0, 1);
        Controls.Add(mainLayout);
    }

    private void LoadChannels()
    {
        var channels = _salesChannelRepository.GetAll();
        _selectedChannel = channels.FirstOrDefault(c => string.Equals(c.GroupName, OnlineGroupName, StringComparison.Ordinal))
            ?? channels.FirstOrDefault();
        UpdateChannelDisplay();
        if (_selectedChannel != null) OnChannelChanged();
    }

    private void UpdateChannelDisplay()
    {
        _channelDisplayLabel.Text = _selectedChannel?.ChannelName ?? "(선택 안 됨)";
    }

    private void OnSelectChannelClick(object? sender, EventArgs e)
    {
        using var dialog = new SelectChannelDialog(pinnedGroupName: OnlineGroupName);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK || dialog.SelectedChannel == null) return;

        _selectedChannel = dialog.SelectedChannel;
        UpdateChannelDisplay();
        OnChannelChanged();
    }

    private void OnChannelChanged()
    {
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode)) return;

        _currentChannelConfig = _channelConfigService.Load().FirstOrDefault(c => c.ChannelCode == channelCode)
            ?? new ChannelConfig { ChannelCode = channelCode, ChannelName = _selectedChannel?.ChannelName ?? channelCode };

        _loadedAdItems = [];
        _adDataGrid.DataSource = null;
        UpdateAdSummary();

        LoadTempRules(channelCode);
        LoadConditionRules(channelCode);
        LoadExceptionRules(channelCode);

        LoadChannelSplitSettings();
        LoadChannelSplitPrerules(channelCode);
        RebuildChannelSplitResolver();
        RefreshChannelSplitInventoryGrid();
    }

    /// <summary>
    /// SalesManagerV2(레거시 Python 광고매핑 도구)의 config 폴더(ad_condition_rules.json 등이
    /// 있는 곳)를 선택해 현재 선택된 채널로 조건부/예외 규칙을 이관하고, ad_channels_config.json의
    /// 채널별 헤더 매핑은 이름이 일치하는 채널에 한해 "필드 매핑" 탭 설정으로 채워준다. 레거시
    /// 조건 헤더는 원본 텍스트라 표준 필드로 완벽히 번역되지 않을 수 있어, 번역 못 한 항목과
    /// 매칭 안 된 채널명은 결과 안내에 모아 보여준다(조건부 매핑(상세) 탭에서 직접 바로잡으면 됨).
    /// </summary>
    private void OnLegacyImportClick(object? sender, EventArgs e)
    {
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode))
        {
            MessageBox.Show("먼저 규칙을 이관할 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var folderDialog = new FolderBrowserDialog { Description = "SalesManagerV2의 config 폴더(ad_condition_rules.json 등이 있는 폴더)를 선택하세요" };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

        var channelName = _selectedChannel?.ChannelName ?? channelCode;
        if (MessageBox.Show(
                $"'{channelName}' 채널로 조건부/예외 규칙을 이관합니다(레거시 파일엔 채널코드가 없어 전체 규칙을 이 채널로 가져옵니다). 계속하시겠습니까?",
                "이관 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var result = _legacyMigrationService.Migrate(folderDialog.SelectedPath, channelCode);

            LoadConditionRules(channelCode);
            LoadExceptionRules(channelCode);
            var message = $"조건부 매핑 {result.ConditionRulesImported}건, 예외처리 {result.ExceptionRulesImported}건, " +
                           $"채널 필드 매핑 {result.ChannelFieldMappingsImported}건을 가져왔습니다.";
            if (result.UnmatchedChannelNames.Count > 0)
            {
                message += $"\n\n다음 레거시 채널명은 현재 채널 목록과 일치하지 않아 필드 매핑을 건너뛰었습니다:\n{string.Join(", ", result.UnmatchedChannelNames.Distinct())}";
            }
            if (result.UntranslatedHeaders.Count > 0)
            {
                message += $"\n\n다음 조건 헤더는 표준 필드로 자동 번역하지 못해 '상품명'으로 임시 지정했습니다(조건부 매핑(상세) 탭에서 확인 필요):\n{string.Join(", ", result.UntranslatedHeaders.Distinct())}";
            }

            MessageBox.Show(message, "이관 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"이관 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ===================== 광고비 데이터 탭 =====================

    private TabPage CreateAdDataTabPage()
    {
        var tabPage = new TabPage("광고비 데이터");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnLoad = new Button
        {
            Text = "광고비 파일 불러오기",
            Size = new Size(140, 30),
        };
        var loadTooltip = new ToolTip();
        loadTooltip.SetToolTip(btnLoad, "파일을 여러 번 불러오면 이전에 불러온 항목에 누적됩니다(예: 쇼핑광고/성과형처럼 유형이 다른 파일을 각각 불러와 합산). 새로 시작하려면 \"불러온 항목 초기화\"를 누르세요.");
        btnLoad.Click += OnLoadAdFileClick;
        toolStrip.Controls.Add(btnLoad);

        var btnResetLoaded = new Button { Text = "불러온 항목 초기화", Size = new Size(120, 30) };
        btnResetLoaded.Click += OnResetLoadedAdItemsClick;
        toolStrip.Controls.Add(btnResetLoaded);

        var btnExport = new Button { Text = "분석결과 내보내기", Size = new Size(120, 30) };
        btnExport.Click += OnExportAdResultClick;
        toolStrip.Controls.Add(btnExport);

        var btnSaveReport = new Button { Text = "보고서에 저장", Size = new Size(100, 30) };
        btnSaveReport.Click += OnSaveAdFactClick;
        toolStrip.Controls.Add(btnSaveReport);

        _unmappedOnlyCheckBox = new CheckBox
        {
            Text = "미매핑만 보기",
            AutoSize = true,
            Padding = new Padding(8, 6, 0, 0),
        };
        _unmappedOnlyCheckBox.CheckedChanged += (s, e) => ApplyUnmappedFilter();
        toolStrip.Controls.Add(_unmappedOnlyCheckBox);

        _unclassifiedOnlyCheckBox = new CheckBox
        {
            Text = "미분류 채널만 보기",
            AutoSize = true,
            Padding = new Padding(8, 6, 0, 0),
        };
        _unclassifiedOnlyCheckBox.CheckedChanged += (s, e) => ApplyUnmappedFilter();
        toolStrip.Controls.Add(_unclassifiedOnlyCheckBox);

        _adDataGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "AdMappingForm.AdDataGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _adDataGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "ProductName", HeaderText = "상품명", DataPropertyName = "ProductName", Width = 220 },
            new DataGridViewTextBoxColumn { Name = "ProductId", HeaderText = "상품번호/캠페인", DataPropertyName = "ProductId", Width = 160 },
            new DataGridViewTextBoxColumn { Name = "OptionName", HeaderText = "옵션명", DataPropertyName = "OptionName", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "Cost", HeaderText = "광고비", DataPropertyName = "Cost", Width = 100 },
            new DataGridViewTextBoxColumn { Name = "MappedGroup", HeaderText = "매핑된 상품그룹", DataPropertyName = "MappedGroup", Width = 140 },
            new DataGridViewTextBoxColumn { Name = "MatchType", HeaderText = "매핑타입", DataPropertyName = "MatchType", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "상태", DataPropertyName = "Status", Width = 100 },
            new DataGridViewTextBoxColumn { Name = "ResolvedChannel", HeaderText = "분리채널", DataPropertyName = "ResolvedChannel", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "ChannelMatchType", HeaderText = "채널매칭", DataPropertyName = "ChannelMatchType", Width = 80 },
            new DataGridViewTextBoxColumn { Name = "CampaignKey", HeaderText = "캠페인", DataPropertyName = "CampaignKey", Width = 140 },
            new DataGridViewTextBoxColumn { Name = "Extra1", HeaderText = "추가항목1", DataPropertyName = "Extra1", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "Extra2", HeaderText = "추가항목2", DataPropertyName = "Extra2", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "Note1", HeaderText = "비고1", DataPropertyName = "Note1", Width = 100 },
            new DataGridViewTextBoxColumn { Name = "Note2", HeaderText = "비고2", DataPropertyName = "Note2", Width = 100 },
            new DataGridViewTextBoxColumn { Name = "Note3", HeaderText = "비고3", DataPropertyName = "Note3", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );

        var menu = new ContextMenuStrip();
        menu.Items.Add("임시 매핑으로 등록", null, (s, e) => OnAddTempRuleFromSelectedAdItem());
        menu.Items.Add("조건부 매핑 규칙 추가", null, (s, e) => OnAddConditionRuleFromSelectedAdItem());
        menu.Items.Add("이 행 예외처리(계산 제외)", null, (s, e) => OnAddExceptionFromSelectedAdItem());
        _adDataGrid.ContextMenuStrip = menu;
        _adDataGrid.RowPrePaint += OnAdGridRowPrePaint;

        _adSummaryLabel = new Label { Dock = DockStyle.Fill, Text = "광고비 파일을 불러오세요.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        layout.Controls.Add(toolStrip, 0, 0);
        // 우측: 상품그룹별 광고비 집계 패널
        _adGroupGrid = new CellCopyDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 24,
            RowHeadersVisible = false,
        };
        _adGroupGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "상품그룹", Width = 120, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { HeaderText = "광고비", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );

        var groupLabel = new Label
        {
            Text = "상품그룹별 광고비",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
        };
        var groupPanel = new Panel { Dock = DockStyle.Fill };
        groupPanel.Controls.Add(_adGroupGrid);
        groupPanel.Controls.Add(groupLabel);

        var splitLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        splitLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        splitLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        splitLayout.Controls.Add(_adDataGrid, 0, 0);
        splitLayout.Controls.Add(groupPanel, 1, 0);

        layout.Controls.Add(splitLayout, 0, 1);
        layout.Controls.Add(_adSummaryLabel, 0, 2);
        tabPage.Controls.Add(layout);
        return tabPage;
    }

    private async void OnLoadAdFileClick(object? sender, EventArgs e)
    {
        if (_currentChannelConfig == null || string.IsNullOrEmpty(_selectedChannel?.ChannelCode))
        {
            MessageBox.Show("먼저 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var layouts = _currentChannelConfig.AdFileLayouts;
        if (layouts.Count == 0)
        {
            MessageBox.Show("채널설정 → 광고비 헤더 설정 탭에 레이아웃을 먼저 등록해주세요.",
                "레이아웃 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "광고비 파일을 선택하세요 (여러 파일 선택 가능)",
            Multiselect = true,
            InitialDirectory = _settingsService.GetLastFolder("AdMappingLoad") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        var channelCode = _selectedChannel!.ChannelCode;
        var engine = new AdMappingEngine(_adMappingRepository, channelCode);

        _settingsService.SetLastFolder("AdMappingLoad", Path.GetDirectoryName(ofd.FileNames[0])!);

        var allItems = new List<AdSpendItem>();
        foreach (var fileName in ofd.FileNames)
        {
            try
            {
                // 레이아웃 자동탐지
                var detected = _adSpendLoader.DetectLayout(fileName, layouts);
                Models.AdFileLayout? selectedLayout = detected.Count switch
                {
                    1 => detected[0],
                    > 1 => PickLayout(layouts, $"{Path.GetFileName(fileName)}: 여러 레이아웃이 매칭됩니다."),
                    _ => layouts.Count == 1
                            ? layouts[0]
                            : PickLayout(layouts, $"{Path.GetFileName(fileName)}: 자동탐지에 실패했습니다. 레이아웃을 선택해주세요."),
                };
                if (selectedLayout == null) continue;

                List<AdSpendItem> fileItems;
                try
                {
                    fileItems = await _adSpendLoader.LoadFromFileAsync(engine, channelCode, selectedLayout, fileName);
                }
                catch (EncryptedExcelFileException)
                {
                    using var dialog = new PasswordPromptDialog(Path.GetFileName(fileName));
                    if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) continue;
                    fileItems = await _adSpendLoader.LoadFromFileAsync(engine, channelCode, selectedLayout, fileName, dialog.Password);
                }
                allItems.AddRange(fileItems);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다 ({Path.GetFileName(fileName)}).\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        if (_adSpendLoader.LastLoadHeaderRowLooksEmpty)
        {
            MessageBox.Show(
                "레이아웃의 헤더 행에서 헤더를 하나도 찾지 못했습니다.\n채널설정 → 광고비 헤더 설정 탭에서 시트 이름/헤더 행/열 이름을 확인해주세요.",
                "헤더 행 확인 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // 쇼핑광고/성과형처럼 유형이 다른 파일을 각각 따로 불러와도(레이아웃이 여러 개 등록된 채널)
        // 이전에 불러온 항목이 사라지지 않도록 교체 대신 누적한다 — "보고서에 저장"은 이 누적된
        // 전체 항목을 한 번에 그룹핑해 저장하므로, 저장 시점엔 두 유형이 자연히 합산된다.
        _loadedAdItems.AddRange(allItems);
        ApplyChannelSplitToLoadedItems();
        ApplyUnmappedFilter();
        UpdateAdSummary();
        UpdateConditionPreview();
    }

    /// <summary>
    /// 채널 분리(캠페인 → 하위채널)가 켜진 채널이면 현재 규칙으로 전체 항목을 다시 판정하고,
    /// 꺼져있으면 이전에 남아있을 수 있는 판정 결과를 지운다(설정을 끈 직후 등).
    /// </summary>
    private void ApplyChannelSplitToLoadedItems()
    {
        foreach (var item in _loadedAdItems)
        {
            if (_channelSplitResolver != null)
            {
                _channelSplitResolver.Resolve(item);
            }
            else
            {
                item.CampaignSrc = null;
                item.CampaignKey = null;
                item.ResolvedChannel = null;
                item.ChannelMatchType = null;
            }
        }
    }

    private void RebuildChannelSplitResolver()
    {
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode) || !_channelSplitEnabled)
        {
            _channelSplitResolver = null;
            return;
        }
        _channelSplitResolver = new AdChannelSplitResolver(_channelSplitRepository, channelCode, _channelSplitCampaignSourceHeaders);
    }

    /// <summary>같은 채널 안에서 잘못 불러온 항목을 지우고 새로 시작할 수 있는 명시적 초기화.</summary>
    private void OnResetLoadedAdItemsClick(object? sender, EventArgs e)
    {
        if (_loadedAdItems.Count == 0) return;
        if (MessageBox.Show($"불러온 항목 {_loadedAdItems.Count}건을 모두 지우고 초기화하시겠습니까?", "초기화 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _loadedAdItems = [];
        ApplyUnmappedFilter();
        UpdateAdSummary();
        UpdateConditionPreview();
    }

    private Models.AdFileLayout? PickLayout(IReadOnlyList<Models.AdFileLayout> layouts, string prompt)
    {
        using var form = new Form
        {
            Text = "레이아웃 선택", Size = new Size(360, 180),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false
        };
        var lbl = new Label { Text = prompt, Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 0), TextAlign = ContentAlignment.MiddleLeft };
        var combo = new ComboBox
        {
            Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(8), DataSource = layouts.ToList(), DisplayMember = "LayoutName"
        };
        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnOk = new Button { Text = "선택", DialogResult = DialogResult.OK, Width = 70 };
        var btnCancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Width = 70 };
        btnPanel.Controls.AddRange([btnCancel, btnOk]);
        form.Controls.AddRange([lbl, combo, btnPanel]);
        form.AcceptButton = btnOk; form.CancelButton = btnCancel;
        return FormManager.ShowDialogSafe(form, this) == DialogResult.OK ? combo.SelectedItem as Models.AdFileLayout : null;
    }

    private void ApplyUnmappedFilter()
    {
        IEnumerable<AdSpendItem> source = _loadedAdItems;
        if (_unmappedOnlyCheckBox.Checked)
            source = source.Where(i => string.IsNullOrEmpty(i.MappedGroup) && i.MatchType != "예외처리");
        if (_unclassifiedOnlyCheckBox.Checked)
            source = source.Where(i => i.ResolvedChannel == AdChannelSplitResolver.DefaultChannel);
        _adDataGrid.DataSource = new BindingList<AdSpendItem>(source.ToList());
    }

    /// <summary>마감/이익분석 화면(SettlementForm)과 동일한 방식으로 미매핑 행을 배경색으로 구분한다.</summary>
    private void OnAdGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _adDataGrid.Rows.Count) return;

        var row = _adDataGrid.Rows[e.RowIndex];
        if (row.DataBoundItem is not AdSpendItem item) return;

        bool unmapped = string.IsNullOrEmpty(item.MappedGroup) && item.MatchType != "예외처리";
        bool unclassifiedChannel = item.ResolvedChannel == AdChannelSplitResolver.DefaultChannel;
        if (unmapped || unclassifiedChannel)
        {
            // 다크모드에서 기본 글자색이 흰색으로 바뀌어도 강조 배경에서 글자가 보이도록 검은색으로 고정한다.
            row.DefaultCellStyle.BackColor = Color.MistyRose;
            row.DefaultCellStyle.ForeColor = Color.Black;
        }
        else
        {
            row.DefaultCellStyle.BackColor = _adDataGrid.DefaultCellStyle.BackColor;
            row.DefaultCellStyle.ForeColor = _adDataGrid.DefaultCellStyle.ForeColor;
        }
    }
    private void UpdateAdSummary()
    {
        if (_loadedAdItems.Count == 0)
        {
            _adSummaryLabel.Text = "광고비 파일을 불러오세요.";
            return;
        }

        var mapped = _loadedAdItems.Count(i => i.Status?.StartsWith("매핑") == true);
        var excluded = _loadedAdItems.Count(i => i.MatchType == "예외처리");
        var totalCost = _loadedAdItems.Where(i => i.MatchType != "예외처리").Sum(i => i.Cost);
        var summary = $"총 {_loadedAdItems.Count}건 | 매핑 {mapped}건 | 예외 {excluded}건 | 합계 광고비 {totalCost:N0}원";

        if (_channelSplitResolver != null)
        {
            var byChannel = _loadedAdItems
                .Where(i => i.MatchType != "예외처리")
                .GroupBy(i => i.ResolvedChannel ?? AdChannelSplitResolver.DefaultChannel)
                .Select(g => (Channel: g.Key, Cost: g.Sum(i => i.Cost)))
                .OrderBy(g => g.Channel == AdChannelSplitResolver.DefaultChannel ? 1 : 0)
                .ThenBy(g => g.Channel)
                .ToList();
            summary += " | 채널: " + string.Join(" / ", byChannel.Select(g => $"{g.Channel} {g.Cost:N0}"));
        }

        _adSummaryLabel.Text = summary;
        UpdateAdGroupGrid();
    }

    private void UpdateAdGroupGrid()
    {
        _adGroupGrid.Rows.Clear();
        if (_loadedAdItems.Count == 0) return;

        var groups = _loadedAdItems
            .Where(i => !string.IsNullOrEmpty(i.MappedGroup) && i.MatchType != "예외처리")
            .GroupBy(i => i.MappedGroup!)
            .Select(g => (Group: g.Key, Cost: g.Sum(i => i.Cost)))
            .OrderByDescending(g => g.Cost)
            .ToList();

        foreach (var (group, cost) in groups)
            _adGroupGrid.Rows.Add(group, cost);

        if (groups.Count > 0)
        {
            int totalIdx = _adGroupGrid.Rows.Add("합계", groups.Sum(g => g.Cost));
            var boldStyle = new DataGridViewCellStyle
            {
                Font = new Font(_adGroupGrid.Font, FontStyle.Bold),
                BackColor = SystemColors.ControlLight,
                Format = "N0",
                Alignment = DataGridViewContentAlignment.MiddleRight,
            };
            _adGroupGrid.Rows[totalIdx].DefaultCellStyle = boldStyle;
            _adGroupGrid.Rows[totalIdx].Cells[0].Style = new DataGridViewCellStyle(boldStyle)
            {
                Alignment = DataGridViewContentAlignment.MiddleLeft,
            };
        }
    }


    /// <summary>
    /// SalesManagerV2(ad_engine.py의 save_results)가 만들던 광고분석 결과 엑셀을 그대로 이식한다.
    /// 시트 구성/열 이름/순서를 동일하게 맞춤: "광고매핑상세"(원본 열 전체 + 판매채널 + 표준화된
    /// AD_* 열 + 매핑결과) / "그룹별_광고비"(판매채널/MAPPED_GROUP/AD_COST, 매핑된 행만 합산).
    /// </summary>
    private async void OnSaveAdFactClick(object? sender, EventArgs e)
    {
        if (_loadedAdItems.Count == 0)
        {
            MessageBox.Show("저장할 광고비 분석 결과가 없습니다. 먼저 광고비 파일을 불러오세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var channelCode = _selectedChannel?.ChannelCode ?? string.Empty;
        var channelName = _selectedChannel?.ChannelName ?? channelCode;
        using var periodDialog = new AdFactPeriodInputDialog(channelCode, channelName, _profitFactRepository);
        if (FormManager.ShowDialogSafe(periodDialog, this) != DialogResult.OK) return;
        var period = periodDialog.SelectedPeriod;

        Cursor = Cursors.WaitCursor;
        try
        {
            var items = _loadedAdItems.ToList();

            if (_channelSplitResolver == null)
            {
                var facts = await Task.Run(() => BuildAdFacts(items));
                await Task.Run(() => _profitFactRepository.SaveAdFacts(period, channelCode, channelName, facts));
                MessageBox.Show($"보고서 저장 완료 — {period} / {channelName} / {facts.Count}개 그룹", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 채널 분리가 켜진 경우: RESOLVED_CHANNEL별로 나눠 그 채널의 실제 SalesChannel로 저장한다.
            // "미분류"는 실채널이 아니므로 저장하지 않고 안내만 한다 — 실채널로 잘못 합산되는 것을
            // 막기 위함(§11 미결 사항: 취입 경로가 정해질 때까지는 안전하게 보류).
            var configs = _channelConfigService.Load();
            var savedSummaries = new List<string>();
            var skippedUnclassifiedCount = 0;
            var skippedUnclassifiedCost = 0m;
            var skippedUnknownChannels = new List<string>();

            var byResolvedChannel = items
                .Where(i => i.MatchType != "예외처리")
                .GroupBy(i => i.ResolvedChannel ?? AdChannelSplitResolver.DefaultChannel)
                .ToList();

            foreach (var group in byResolvedChannel)
            {
                if (group.Key == AdChannelSplitResolver.DefaultChannel)
                {
                    skippedUnclassifiedCount = group.Count();
                    skippedUnclassifiedCost = group.Sum(i => i.Cost);
                    continue;
                }

                var targetConfig = configs.FirstOrDefault(c => c.ChannelName == group.Key);
                if (targetConfig == null)
                {
                    skippedUnknownChannels.Add(group.Key);
                    continue;
                }

                var groupItems = group.ToList();
                var facts = await Task.Run(() => BuildAdFacts(groupItems));
                await Task.Run(() => _profitFactRepository.SaveAdFacts(period, targetConfig.ChannelCode, targetConfig.ChannelName, facts));
                savedSummaries.Add($"{targetConfig.ChannelName} {facts.Count}개 그룹");
            }

            var message = $"보고서 저장 완료 — {period}\n{string.Join("\n", savedSummaries)}";
            if (skippedUnclassifiedCount > 0)
                message += $"\n\n⚠ 미분류 {skippedUnclassifiedCount}건({skippedUnclassifiedCost:N0}원)은 저장하지 않았습니다. \"채널 분리 규칙\" 탭에서 먼저 분류해주세요.";
            if (skippedUnknownChannels.Count > 0)
                message += $"\n\n⚠ 다음 분리채널은 등록된 채널설정을 찾지 못해 저장을 건너뛰었습니다: {string.Join(", ", skippedUnknownChannels.Distinct())}";

            MessageBox.Show(message, "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"보고서 저장 오류: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static List<AdFactRow> BuildAdFacts(List<AdSpendItem> items) => items
        .Where(i => !string.IsNullOrEmpty(i.MappedGroup) && i.MatchType != "예외처리")
        .GroupBy(i => i.MappedGroup!)
        .Select(g => new AdFactRow { ProductGroup = g.Key, AdCost = g.Sum(i => i.Cost) })
        .ToList();

    private async void OnExportAdResultClick(object? sender, EventArgs e)
    {
        if (_loadedAdItems.Count == 0)
        {
            MessageBox.Show("내보낼 광고비 분석 결과가 없습니다. 먼저 광고비 파일을 불러오세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var channelName = _selectedChannel?.ChannelName ?? "채널";
        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"{channelName}_광고분석_{DateTime.Now:yyMM}.xlsx",
            _settingsService.GetLastFolder("AdMappingExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;

        _settingsService.SetLastFolder("AdMappingExport", Path.GetDirectoryName(filePath)!);

        var itemsSnapshot = _loadedAdItems.ToList();
        var splitEnabled = _channelSplitResolver != null;
        var exportChannelCode = _selectedChannel?.ChannelCode ?? "";
        var exportCompanyName = string.IsNullOrEmpty(exportChannelCode) ? "" :
            (_docPartyRepository.GetByChannelCode(exportChannelCode)?.CompanyName ?? "");

        Cursor = Cursors.WaitCursor;
        try
        {
            await Task.Run(() =>
            {
                ExcelLicense.Ensure();
                using var package = new ExcelPackage();
                WriteAdDetailSheetStatic(package.Workbook.Worksheets.Add("광고매핑상세"), channelName, itemsSnapshot);
                WriteAdGroupSummarySheetStatic(package.Workbook.Worksheets.Add("그룹별_광고비"), channelName, itemsSnapshot, splitEnabled);
                if (splitEnabled)
                    WriteChannelVerificationSheetStatic(package.Workbook.Worksheets.Add("채널검증"), itemsSnapshot);
                MetaSheetHelper.WriteToPackage(package, new FileMeta
                {
                    SourceType = "ad",
                    ChannelCode = exportChannelCode,
                    ChannelName = channelName,
                    CompanyName = exportCompanyName,
                    Period = DateTime.Now.ToString("yyyyMM"),
                });
                ExportHelper.SaveExcel(package, filePath);
            });
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 내보내는 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }
    private static void WriteAdDetailSheetStatic(ExcelWorksheet sheet, string channelName, List<AdSpendItem> items)
    {
        var rawHeaders = items.Where(i => i.RawValues is { Count: > 0 }).SelectMany(i => i.RawValues!.Keys).Distinct().ToList();
        string[] stdHeaders = ["AD_PRODUCT_NAME", "AD_PRODUCT_ID", "AD_OPTION", "AD_COST", "AD_EXTRA1", "AD_EXTRA2", "MAPPED_GROUP", "MATCH_TYPE", "MAPPING_STATUS", "CAMPAIGN_SRC", "CAMPAIGN_KEY", "RESOLVED_CHANNEL", "CHANNEL_MATCH_TYPE"];
        var headers = new List<string> { "판매채널" };
        headers.AddRange(rawHeaders);
        headers.AddRange(stdHeaders);

        for (int i = 0; i < headers.Count; i++) sheet.Cells[1, i + 1].Value = headers[i];

        int row = 2;
        foreach (var item in items)
        {
            int col = 1;
            sheet.Cells[row, col++].Value = channelName;
            foreach (var header in rawHeaders) sheet.Cells[row, col++].Value = item.RawValues?.GetValueOrDefault(header, string.Empty);
            sheet.Cells[row, col++].Value = item.ProductName;
            sheet.Cells[row, col++].Value = item.ProductId;
            sheet.Cells[row, col++].Value = item.OptionName;
            sheet.Cells[row, col++].Value = item.Cost;
            sheet.Cells[row, col++].Value = item.Extra1;
            sheet.Cells[row, col++].Value = item.Extra2;
            sheet.Cells[row, col++].Value = item.MappedGroup;
            sheet.Cells[row, col++].Value = item.MatchType;
            sheet.Cells[row, col++].Value = string.IsNullOrEmpty(item.MappedGroup) ? "X" : "O";
            sheet.Cells[row, col++].Value = item.CampaignSrc;
            sheet.Cells[row, col++].Value = item.CampaignKey;
            sheet.Cells[row, col++].Value = item.ResolvedChannel;
            sheet.Cells[row, col++].Value = item.ChannelMatchType;
            row++;
        }
        sheet.Cells.AutoFitColumns();
    }

    private static void WriteAdGroupSummarySheetStatic(ExcelWorksheet sheet, string channelName, List<AdSpendItem> items, bool splitEnabled)
    {
        if (!splitEnabled)
        {
            string[] headers = ["판매채널", "MAPPED_GROUP", "AD_COST"];
            for (int i = 0; i < headers.Length; i++) sheet.Cells[1, i + 1].Value = headers[i];

            // 레거시와 동일하게, 매핑된(MAPPING_STATUS == 'O') 행만 그룹별로 합산한다.
            var groups = items
                .Where(i => !string.IsNullOrEmpty(i.MappedGroup))
                .GroupBy(i => i.MappedGroup!)
                .Select(g => new { Group = g.Key, Cost = g.Sum(i => i.Cost) })
                .ToList();

            int row = 2;
            foreach (var g in groups)
            {
                sheet.Cells[row, 1].Value = channelName;
                sheet.Cells[row, 2].Value = g.Group;
                sheet.Cells[row, 3].Value = g.Cost;
                row++;
            }
            sheet.Cells.AutoFitColumns();
            return;
        }

        // 채널 분리가 켜진 경우: (RESOLVED_CHANNEL, MAPPED_GROUP) 2단 집계로 바꾼다(§6.2).
        // "미분류" 행도 누락을 인지할 수 있도록 집계에 그대로 포함한다.
        string[] splitHeaders = ["RESOLVED_CHANNEL", "MAPPED_GROUP", "AD_COST"];
        for (int i = 0; i < splitHeaders.Length; i++) sheet.Cells[1, i + 1].Value = splitHeaders[i];

        var splitGroups = items
            .Where(i => !string.IsNullOrEmpty(i.MappedGroup))
            .GroupBy(i => (Channel: i.ResolvedChannel ?? AdChannelSplitResolver.DefaultChannel, Group: i.MappedGroup!))
            .Select(g => new { g.Key.Channel, g.Key.Group, Cost = g.Sum(i => i.Cost) })
            .OrderBy(g => g.Channel == AdChannelSplitResolver.DefaultChannel ? 1 : 0)
            .ThenBy(g => g.Channel)
            .ThenBy(g => g.Group)
            .ToList();

        int splitRow = 2;
        foreach (var g in splitGroups)
        {
            sheet.Cells[splitRow, 1].Value = g.Channel;
            sheet.Cells[splitRow, 2].Value = g.Group;
            sheet.Cells[splitRow, 3].Value = g.Cost;
            splitRow++;
        }
        sheet.Cells.AutoFitColumns();
    }

    /// <summary>채널 분리 검증 시트(§6.3) — 원본 총 광고비와 채널별 합계가 일치하는지 한눈에 보여준다.
    /// 규칙 중복/누락으로 인한 오류는 조용히 발생하면 발견이 불가능하므로, 총합 일치 검사를 파일에
    /// 남겨 저장 시점마다 대조할 수 있게 한다.</summary>
    private static void WriteChannelVerificationSheetStatic(ExcelWorksheet sheet, List<AdSpendItem> items)
    {
        var billable = items.Where(i => i.MatchType != "예외처리").ToList();
        var originalTotal = billable.Sum(i => i.Cost);

        var byChannel = billable
            .GroupBy(i => i.ResolvedChannel ?? AdChannelSplitResolver.DefaultChannel)
            .Select(g => (Channel: g.Key, Cost: g.Sum(i => i.Cost)))
            .OrderBy(g => g.Channel == AdChannelSplitResolver.DefaultChannel ? 1 : 0)
            .ThenBy(g => g.Channel)
            .ToList();
        var splitTotal = byChannel.Sum(g => g.Cost);
        var unclassifiedCount = billable.Count(i => (i.ResolvedChannel ?? AdChannelSplitResolver.DefaultChannel) == AdChannelSplitResolver.DefaultChannel);

        int row = 1;
        void WriteRow(string label, object? value)
        {
            sheet.Cells[row, 1].Value = label;
            sheet.Cells[row, 2].Value = value;
            row++;
        }

        WriteRow("원본 총 광고비", originalTotal);
        WriteRow("채널 분리 합계", splitTotal);
        WriteRow("차액", originalTotal - splitTotal);
        foreach (var (channel, cost) in byChannel) WriteRow(channel, cost);
        WriteRow("미분류 캠페인 수", unclassifiedCount);

        sheet.Cells.AutoFitColumns();
    }

    private void OnAddTempRuleFromSelectedAdItem()
    {
        if (_adDataGrid.CurrentRow?.DataBoundItem is not AdSpendItem item) return;
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode)) return;

        using var dialog = new AdTargetGroupPromptDialog();
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.TargetGroup)) return;

        _adMappingRepository.UpsertTempRule(channelCode, AdMappingEngine.BuildKey(item), dialog.TargetGroup);
        LoadTempRules(channelCode);
        ReapplyMapping(channelCode); // UpdateAdSummary()를 호출해 _adSummaryLabel을 새 요약으로 갱신한다.
        // 노션 5.1 후속 점검: 그리드 재구성 직후 모달을 띄우면 같은 위험군이라 비모달 라벨로 대체.
        _adSummaryLabel.Text = $"[임시 매핑으로 등록했습니다 — {DateTime.Now:HH:mm:ss}] {_adSummaryLabel.Text}";
    }

    private void OnAddConditionRuleFromSelectedAdItem()
    {
        if (_adDataGrid.CurrentRow?.DataBoundItem is not AdSpendItem item) return;
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode) || string.IsNullOrWhiteSpace(item.ProductName)) return;

        using var dialog = new AdTargetGroupPromptDialog();
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.TargetGroup)) return;

        var details = new List<AdConditionDetail>
        {
            new() { HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = item.ProductName!, Logic = ConditionLogic.And },
        };
        var newRuleId = _adMappingRepository.AddConditionRuleWithDetails(channelCode, item.ProductName!, dialog.TargetGroup, details);

        LoadConditionRules(channelCode);
        SelectConditionRuleById(newRuleId);
        _tabControl.SelectedTab = _conditionDetailTabPage;
        ReapplyMapping(channelCode);
        // 노션 5.1 후속 점검: 탭 전환+그리드 재구성 직후 모달을 띄우면 같은 위험군이라 비모달
        // 라벨로 대체(어차피 조건부 매핑(상세) 탭으로 전환되며 새 규칙이 바로 선택된 채로 보임).
        _conditionSaveFeedbackLabel.Text = $"조건부 매핑 규칙을 추가했습니다 ({DateTime.Now:HH:mm:ss}) — 조건을 다듬은 뒤 저장하세요.";
    }

    private void OnAddExceptionFromSelectedAdItem()
    {
        if (_adDataGrid.CurrentRow?.DataBoundItem is not AdSpendItem item) return;
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode) || string.IsNullOrWhiteSpace(item.ProductId)) return;

        if (MessageBox.Show($"상품번호/캠페인 '{item.ProductId}'를 포함하는 행을 앞으로 계산에서 제외하시겠습니까?", "예외처리 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        _adMappingRepository.AddExceptionRule(new AdExceptionRule { ChannelCode = channelCode, HeaderField = AdStdField.ProductId, Operator = AdConditionOperator.Contains, TargetValue = item.ProductId! });
        LoadExceptionRules(channelCode);
        ReapplyMapping(channelCode);
    }

    private void ReapplyMapping(string channelCode)
    {
        if (_loadedAdItems.Count == 0) return;

        var engine = new AdMappingEngine(_adMappingRepository, channelCode);
        foreach (var item in _loadedAdItems) engine.ApplyMapping(item);
        ApplyUnmappedFilter();
        UpdateAdSummary();
        UpdateConditionPreview();
    }

    // ===================== 임시 매핑 탭 =====================

    private TabPage CreateTempRuleTabPage()
    {
        var tabPage = new TabPage("임시 매핑");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnSave = new Button { Text = "저장", Size = new Size(90, 28) };
        // 노션 5.1 후속 점검: SaveTempRules가 ReapplyMapping(그리드 재계산)을 하는데 그 직후
        // 모달을 띄우면 다른 화면들에서 반복 재현됐던 "모달이 안 보이게 생성되는" 경쟁 상태와
        // 같은 위험군이라 비모달 라벨로 대체했다.
        var tempRuleFeedbackLabel = new Label { AutoSize = true, Padding = new Padding(10, 7, 0, 0), ForeColor = Color.DarkGreen };
        btnSave.Click += (s, e) =>
        {
            SaveTempRules();
            tempRuleFeedbackLabel.Text = $"저장되었습니다. ({DateTime.Now:HH:mm:ss})";
        };
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(tempRuleFeedbackLabel);

        _tempRuleGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "AdMappingForm.TempRuleGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
        };
        _tempRuleGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "매칭 키(상품명_옵션_상품번호)", DataPropertyName = "Key", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "TargetGroup", HeaderText = "대상 상품그룹", DataPropertyName = "TargetGroup", Width = 200 }
        );

        layout.Controls.Add(toolStrip, 0, 0);
        layout.Controls.Add(_tempRuleGrid, 0, 1);
        tabPage.Controls.Add(layout);
        return tabPage;
    }

    private void LoadTempRules(string channelCode)
    {
        _tempRuleGrid.DataSource = new BindingList<AdMappingRule>(_adMappingRepository.GetTempRules(channelCode));
    }

    private void SaveTempRules()
    {
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode)) return;
        if (_tempRuleGrid.DataSource is not BindingList<AdMappingRule> rules) return;

        foreach (var rule in rules.Where(r => !string.IsNullOrWhiteSpace(r.Key)))
        {
            _adMappingRepository.UpsertTempRule(channelCode, rule.Key, rule.TargetGroup);
        }
        ReapplyMapping(channelCode);
    }

    // ===================== 조건부 매핑(상세) 탭 =====================

    private TabPage CreateConditionDetailTabPage()
    {
        var tabPage = new TabPage("조건부 매핑(상세)");

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var leftPanel = new Panel { Dock = DockStyle.Fill };
        _conditionRuleGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _conditionRuleGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "키(요약)", DataPropertyName = "Key", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "TargetGroup", HeaderText = "대상 그룹", DataPropertyName = "TargetGroup", Width = 100 }
        );
        _conditionRuleGrid.SelectionChanged += OnConditionRuleSelectionChanged;

        var leftButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36 };
        var btnAddRule = new Button { Text = "규칙 추가", Size = new Size(90, 28) };
        btnAddRule.Click += OnAddConditionRuleClick;
        var btnDeleteRule = new Button { Text = "규칙 삭제", Size = new Size(90, 28) };
        btnDeleteRule.Click += OnDeleteConditionRuleClick;
        leftButtonPanel.Controls.Add(btnAddRule);
        leftButtonPanel.Controls.Add(btnDeleteRule);

        leftPanel.Controls.Add(_conditionRuleGrid);
        leftPanel.Controls.Add(leftButtonPanel);

        var rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var summaryPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        _conditionKeyTextBox = new TextBox { Width = 220 };
        _conditionTargetGroupTextBox = new TextBox { Width = 120 };
        var btnSaveSummary = new Button { Text = "규칙 정보 저장", Size = new Size(110, 28) };
        btnSaveSummary.Click += OnSaveConditionSummaryClick;
        summaryPanel.Controls.Add(new Label { Text = "키(요약):", AutoSize = true, Padding = new Padding(0, 7, 3, 0) });
        summaryPanel.Controls.Add(_conditionKeyTextBox);
        summaryPanel.Controls.Add(new Label { Text = "대상 그룹:", AutoSize = true, Padding = new Padding(10, 7, 3, 0) });
        summaryPanel.Controls.Add(_conditionTargetGroupTextBox);
        summaryPanel.Controls.Add(btnSaveSummary);
        _conditionPreviewLabel = new Label { Text = "예상 매칭 건수: -", AutoSize = true, Padding = new Padding(15, 7, 0, 0), ForeColor = Color.Blue, Font = new Font(Font, FontStyle.Bold) };
        summaryPanel.Controls.Add(_conditionPreviewLabel);

        _conditionDetailGrid = new ExcelLikeDataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false };
        var headerFieldColumn = new DataGridViewComboBoxColumn { Name = "HeaderField", HeaderText = "비교할 항목", DataPropertyName = "HeaderField", DataSource = Enum.GetValues(typeof(AdStdField)), Width = 130 };
        var operatorColumn = new DataGridViewComboBoxColumn { Name = "Operator", HeaderText = "조건", DataPropertyName = "Operator", DataSource = Enum.GetValues(typeof(AdConditionOperator)), Width = 130 };
        var targetValueColumn = new DataGridViewTextBoxColumn { Name = "TargetValue", HeaderText = "비교할 값", DataPropertyName = "TargetValue", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };
        var logicColumn = new DataGridViewComboBoxColumn { Name = "Logic", HeaderText = "다음 조건과 결합", DataPropertyName = "Logic", DataSource = Enum.GetValues(typeof(ConditionLogic)), Width = 110 };
        _conditionDetailGrid.Columns.AddRange(headerFieldColumn, operatorColumn, targetValueColumn, logicColumn);
        _conditionDetailGrid.CurrentCellDirtyStateChanged += (s, e) => { if (_conditionDetailGrid.IsCurrentCellDirty) _conditionDetailGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _conditionDetailGrid.CellValueChanged += (s, e) => UpdateConditionPreview();

        var detailButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var btnAddDetail = new Button { Text = "조건 추가", Size = new Size(90, 28) };
        btnAddDetail.Click += OnAddConditionDetailClick;
        var btnDeleteDetail = new Button { Text = "조건 삭제", Size = new Size(90, 28) };
        btnDeleteDetail.Click += OnDeleteConditionDetailClick;
        var btnSaveDetails = new Button { Text = "상세조건 저장", Size = new Size(110, 28) };
        btnSaveDetails.Click += OnSaveConditionDetailsClick;
        detailButtonPanel.Controls.Add(btnAddDetail);
        detailButtonPanel.Controls.Add(btnDeleteDetail);
        detailButtonPanel.Controls.Add(btnSaveDetails);
        _conditionSaveFeedbackLabel = new Label { AutoSize = true, Padding = new Padding(15, 7, 0, 0), ForeColor = Color.DarkGreen };
        detailButtonPanel.Controls.Add(_conditionSaveFeedbackLabel);

        rightPanel.Controls.Add(summaryPanel, 0, 0);
        rightPanel.Controls.Add(_conditionDetailGrid, 0, 1);
        rightPanel.Controls.Add(detailButtonPanel, 0, 2);

        mainLayout.Controls.Add(leftPanel, 0, 0);
        mainLayout.Controls.Add(rightPanel, 1, 0);
        tabPage.Controls.Add(mainLayout);

        SetConditionDetailEditorEnabled(false);
        return tabPage;
    }

    private void SetConditionDetailEditorEnabled(bool enabled)
    {
        _conditionKeyTextBox.Enabled = enabled;
        _conditionTargetGroupTextBox.Enabled = enabled;
        _conditionDetailGrid.Enabled = enabled;
        if (!enabled)
        {
            _conditionKeyTextBox.Text = string.Empty;
            _conditionTargetGroupTextBox.Text = string.Empty;
            _conditionDetailGrid.DataSource = null;
        }
        UpdateConditionPreview();
    }

    private void LoadConditionRules(string channelCode)
    {
        _conditionRuleGrid.DataSource = new BindingList<AdMappingRule>(_adMappingRepository.GetConditionRules(channelCode));
        _selectedConditionRuleId = -1;
        SetConditionDetailEditorEnabled(false);
    }

    private void OnConditionRuleSelectionChanged(object? sender, EventArgs e)
    {
        if (_conditionRuleGrid.CurrentRow?.DataBoundItem is not AdMappingRule rule)
        {
            _selectedConditionRuleId = -1;
            SetConditionDetailEditorEnabled(false);
            return;
        }

        _selectedConditionRuleId = rule.Id;
        _conditionKeyTextBox.Text = rule.Key;
        _conditionTargetGroupTextBox.Text = rule.TargetGroup;
        _conditionDetailGrid.DataSource = new BindingList<AdConditionDetail>(_adMappingRepository.GetConditionDetails(rule.Id));
        SetConditionDetailEditorEnabled(true);
    }

    private void OnAddConditionRuleClick(object? sender, EventArgs e)
    {
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode))
        {
            MessageBox.Show("먼저 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var newRuleId = _adMappingRepository.AddConditionRuleWithDetails(channelCode, "새 조건부 규칙", string.Empty, []);
        LoadConditionRules(channelCode);
        SelectConditionRuleById(newRuleId);
    }

    private void SelectConditionRuleById(long ruleId)
    {
        foreach (DataGridViewRow row in _conditionRuleGrid.Rows)
        {
            if (row.DataBoundItem is AdMappingRule rule && rule.Id == ruleId)
            {
                _conditionRuleGrid.CurrentCell = row.Cells[0];
                break;
            }
        }
    }

    private void OnDeleteConditionRuleClick(object? sender, EventArgs e)
    {
        if (_selectedConditionRuleId < 0) return;
        if (MessageBox.Show("선택한 조건부 매핑 규칙과 그 상세조건을 모두 삭제합니다. 계속하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _adMappingRepository.DeleteConditionRule(_selectedConditionRuleId);
        var channelCode = _selectedChannel?.ChannelCode;
        if (!string.IsNullOrEmpty(channelCode)) { LoadConditionRules(channelCode); ReapplyMapping(channelCode); }
    }

    private void OnSaveConditionSummaryClick(object? sender, EventArgs e)
    {
        if (_selectedConditionRuleId < 0) return;
        var ruleId = _selectedConditionRuleId;
        _adMappingRepository.UpdateConditionRuleSummary(ruleId, _conditionKeyTextBox.Text, _conditionTargetGroupTextBox.Text);

        var channelCode = _selectedChannel?.ChannelCode;
        if (!string.IsNullOrEmpty(channelCode))
        {
            // LoadConditionRules가 목록을 다시 불러오며 선택을 초기화하므로, 같은 규칙을 다시
            // 선택해 편집을 이어갈 수 있게 한다.
            LoadConditionRules(channelCode);
            SelectConditionRuleById(ruleId);
            ReapplyMapping(channelCode);
        }
        // 노션 5.1 후속 점검: 그리드 재구성 직후 모달을 띄우면 다른 화면들에서 반복 재현됐던
        // 경쟁 상태와 같은 위험군이라 비모달 라벨로 대체했다.
        _conditionSaveFeedbackLabel.Text = $"규칙 정보 저장됨 ({DateTime.Now:HH:mm:ss})";
    }

    private void OnAddConditionDetailClick(object? sender, EventArgs e)
    {
        if (_conditionDetailGrid.DataSource is not BindingList<AdConditionDetail> details) return;
        details.Add(new AdConditionDetail { RuleId = _selectedConditionRuleId, HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = string.Empty, Logic = ConditionLogic.And });
        UpdateConditionPreview();
    }

    private void OnDeleteConditionDetailClick(object? sender, EventArgs e)
    {
        if (_conditionDetailGrid.DataSource is not BindingList<AdConditionDetail> details) return;
        if (_conditionDetailGrid.CurrentRow?.DataBoundItem is not AdConditionDetail detail) return;
        details.Remove(detail);
        UpdateConditionPreview();
    }

    private void OnSaveConditionDetailsClick(object? sender, EventArgs e)
    {
        if (_selectedConditionRuleId < 0) return;
        if (_conditionDetailGrid.DataSource is not BindingList<AdConditionDetail> details) return;

        _adMappingRepository.ReplaceConditionDetails(_selectedConditionRuleId, details.ToList());
        var channelCode = _selectedChannel?.ChannelCode;
        if (!string.IsNullOrEmpty(channelCode)) ReapplyMapping(channelCode);
        _conditionSaveFeedbackLabel.Text = $"상세조건 저장됨 ({DateTime.Now:HH:mm:ss})";
    }

    /// <summary>현재 불러온 광고비 데이터(_loadedAdItems)에 조건을 즉시 적용해 예상 매칭 건수를 보여준다.</summary>
    private void UpdateConditionPreview()
    {
        if (_conditionDetailGrid.DataSource is not BindingList<AdConditionDetail> details || details.Count == 0)
        {
            _conditionPreviewLabel.Text = "예상 매칭 건수: -";
            return;
        }

        if (_loadedAdItems.Count == 0)
        {
            _conditionPreviewLabel.Text = "예상 매칭 건수: (광고비 파일을 불러와야 미리볼 수 있습니다)";
            return;
        }

        var validDetails = details.Where(d => !string.IsNullOrWhiteSpace(d.TargetValue) || d.Operator == AdConditionOperator.IsZero).ToList();
        if (validDetails.Count == 0)
        {
            _conditionPreviewLabel.Text = $"예상 매칭 건수: 전체 {_loadedAdItems.Count}건(조건 없음)";
            return;
        }

        var matchCount = _loadedAdItems.Count(i => AdConditionEvaluator.Matches(validDetails, i));
        _conditionPreviewLabel.Text = $"예상 매칭 건수: {matchCount}건 / 전체 {_loadedAdItems.Count}건";
    }

    // ===================== 예외 처리 탭 =====================

    private TabPage CreateExceptionTabPage()
    {
        var tabPage = new TabPage("예외 처리");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        toolStrip.Controls.Add(new Label
        {
            Text = "광고비 파일에서 계산 대상이 아닌 행(합계/소계 등)을 걸러내는 규칙입니다 — 특정 헤더가 어떤 값을 가지면 그 행을 제외합니다.",
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0),
        });

        _exceptionGrid = new ExcelLikeDataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = true };
        var headerFieldColumn = new DataGridViewComboBoxColumn { Name = "HeaderField", HeaderText = "헤더 항목", DataPropertyName = "HeaderField", DataSource = Enum.GetValues(typeof(AdStdField)), Width = 130 };
        var operatorColumn = new DataGridViewComboBoxColumn { Name = "Operator", HeaderText = "조건", DataPropertyName = "Operator", DataSource = Enum.GetValues(typeof(AdConditionOperator)), Width = 130 };
        var targetValueColumn = new DataGridViewTextBoxColumn { Name = "TargetValue", HeaderText = "비교할 값", DataPropertyName = "TargetValue", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };
        _exceptionGrid.Columns.AddRange(headerFieldColumn, operatorColumn, targetValueColumn);
        _exceptionGrid.UserDeletingRow += OnExceptionRowDeleting;
        _exceptionGrid.CellEndEdit += OnExceptionCellEndEdit;

        layout.Controls.Add(toolStrip, 0, 0);
        layout.Controls.Add(_exceptionGrid, 0, 1);
        tabPage.Controls.Add(layout);
        return tabPage;
    }

    private void LoadExceptionRules(string channelCode)
    {
        _exceptionGrid.DataSource = new BindingList<AdExceptionRule>(_adMappingRepository.GetExceptionRules(channelCode));
    }

    private void OnExceptionCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode)) return;
        if (_exceptionGrid.Rows[e.RowIndex].DataBoundItem is not AdExceptionRule rule || rule.Id != 0) return;
        if (string.IsNullOrWhiteSpace(rule.TargetValue)) return;

        rule.ChannelCode = channelCode;
        _adMappingRepository.AddExceptionRule(rule);
        LoadExceptionRules(channelCode);
        ReapplyMapping(channelCode);
    }

    private void OnExceptionRowDeleting(object? sender, DataGridViewRowCancelEventArgs e)
    {
        if (e.Row.DataBoundItem is not AdExceptionRule rule || rule.Id == 0) return;
        _adMappingRepository.DeleteExceptionRule(rule.Id);
        var channelCode = _selectedChannel?.ChannelCode;
        if (!string.IsNullOrEmpty(channelCode)) ReapplyMapping(channelCode);
    }

    // ===================== 채널 분리 규칙 탭 =====================
    // 캠페인 → 하위채널 자동 분리(AdChannelSplit_Spec.md). "상품+옵션 → 품목그룹" 매핑과는
    // 완전히 독립된 축이라 위 임시/조건부/예외 규칙과는 별도 저장소(_channelSplitRepository)를 쓴다.

    private TabPage CreateChannelSplitTabPage()
    {
        var tabPage = new TabPage("채널 분리 규칙");
        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // settingsPanel의 두 줄(체크박스 / 헤더입력+저장버튼)에 명시적으로 RowStyle을 지정하지 않으면
        // TableLayoutPanel이 두 줄을 균등하지 않게 나눠 아래 줄(캠페인 소스 헤더 입력+설정 저장
        // 버튼)이 화면에서 잘려 보이지 않는 문제가 있었다 — 반드시 두 줄 모두 고정 높이로 지정한다.
        var settingsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(5) };
        settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        settingsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        var settingsRow1 = new FlowLayoutPanel { Dock = DockStyle.Fill };
        _channelSplitEnabledCheckBox = new CheckBox
        {
            Text = "이 채널은 캠페인 기준으로 하위채널을 자동 분리합니다(예: 쿠팡 광고비를 쿠팡일반/로켓/그로스로 분리).",
            AutoSize = true,
            Padding = new Padding(0, 4, 0, 0),
        };
        settingsRow1.Controls.Add(_channelSplitEnabledCheckBox);

        var settingsRow2 = new FlowLayoutPanel { Dock = DockStyle.Fill };
        settingsRow2.Controls.Add(new Label { Text = "캠페인 소스 헤더(우선순위, 쉼표구분):", AutoSize = true, Padding = new Padding(0, 7, 3, 0) });
        _campaignSourceHeadersTextBox = new TextBox { Width = 260 };
        settingsRow2.Controls.Add(_campaignSourceHeadersTextBox);
        var btnSaveSettings = new Button { Text = "설정 저장", Size = new Size(90, 28) };
        btnSaveSettings.Click += OnSaveChannelSplitSettingsClick;
        settingsRow2.Controls.Add(btnSaveSettings);
        _channelSplitSettingsFeedbackLabel = new Label { AutoSize = true, Padding = new Padding(10, 7, 0, 0), ForeColor = Color.DarkGreen };
        settingsRow2.Controls.Add(_channelSplitSettingsFeedbackLabel);

        settingsPanel.Controls.Add(settingsRow1, 0, 0);
        settingsPanel.Controls.Add(settingsRow2, 0, 1);

        var subTabControl = new TabControl { Dock = DockStyle.Fill };
        subTabControl.TabPages.Add(CreateChannelSplitInventoryTabPage());
        subTabControl.TabPages.Add(CreateChannelSplitPreruleTabPage());

        mainLayout.Controls.Add(settingsPanel, 0, 0);
        mainLayout.Controls.Add(subTabControl, 0, 1);
        tabPage.Controls.Add(mainLayout);
        return tabPage;
    }

    private void LoadChannelSplitSettings()
    {
        var channelCode = _selectedChannel?.ChannelCode;
        (_channelSplitEnabled, _channelSplitCampaignSourceHeaders) = string.IsNullOrEmpty(channelCode)
            ? (false, [])
            : _channelSplitRepository.GetSettings(channelCode);

        _channelSplitEnabledCheckBox.Checked = _channelSplitEnabled;
        _campaignSourceHeadersTextBox.Text = string.Join(", ", _channelSplitCampaignSourceHeaders);
    }

    private void OnSaveChannelSplitSettingsClick(object? sender, EventArgs e)
    {
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode)) return;

        _channelSplitEnabled = _channelSplitEnabledCheckBox.Checked;
        _channelSplitCampaignSourceHeaders = _campaignSourceHeadersTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _channelSplitRepository.SaveSettings(channelCode, _channelSplitEnabled, _channelSplitCampaignSourceHeaders);

        RebuildChannelSplitResolver();
        ApplyChannelSplitToLoadedItems();
        ApplyUnmappedFilter();
        UpdateAdSummary();
        RefreshChannelSplitInventoryGrid();
        _channelSplitSettingsFeedbackLabel.Text = $"설정 저장됨 ({DateTime.Now:HH:mm:ss})";
    }

    /// <summary>채널 분리 대상 하위채널 드롭다운 목록. 실제 등록된 채널설정(쿠팡 계열 ChannelType)의
    /// 채널명을 그대로 쓴다 — 저장 시 SalesChannel/ChannelConfig와 이름이 어긋나지 않게 하기 위함.</summary>
    private List<string> GetChannelSplitTargetOptions()
    {
        var options = _channelConfigService.Load()
            .Where(c => c.ChannelType is ChannelType.CoupangGeneral or ChannelType.CoupangRocket or ChannelType.CoupangGrowth)
            .OrderBy(c => c.ChannelType)
            .Select(c => c.ChannelName)
            .Distinct()
            .ToList();
        options.Add(AdChannelSplitResolver.DefaultChannel);
        return options;
    }

    // ── 캠페인 인벤토리 서브탭 ──────────────────────────────────

    private TabPage CreateChannelSplitInventoryTabPage()
    {
        var tabPage = new TabPage("캠페인 인벤토리");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var infoLabel = new Label
        {
            Text = "현재 불러온 광고비 데이터의 (헤더, 캠페인값) 고유 조합입니다. 노란 배경은 아직 채널을 확정하지 않은 신규 캠페인, 회색 배경은 이번 파일에 나타나지 않은 캠페인입니다.",
            Dock = DockStyle.Fill,
            Padding = new Padding(5, 5, 5, 0),
        };

        _channelSplitInventoryGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
        };
        var headerCol = new DataGridViewTextBoxColumn { Name = "HeaderName", HeaderText = "헤더", DataPropertyName = "HeaderName", Width = 110, ReadOnly = true };
        var valueCol = new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "캠페인 값", DataPropertyName = "Value", Width = 230, ReadOnly = true };
        var costCol = new DataGridViewTextBoxColumn
        {
            Name = "LastCost", HeaderText = "이번달 광고비", DataPropertyName = "LastCost", Width = 110, ReadOnly = true,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight },
        };
        var rowCountCol = new DataGridViewTextBoxColumn
        {
            Name = "RowCount", HeaderText = "행수", DataPropertyName = "RowCount", Width = 60, ReadOnly = true,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight },
        };
        var channelCol = new DataGridViewComboBoxColumn { Name = "TargetChannel", HeaderText = "채널", DataPropertyName = "TargetChannel", Width = 110, DataSource = GetChannelSplitTargetOptions() };
        _channelSplitInventoryGrid.Columns.AddRange(headerCol, valueCol, costCol, rowCountCol, channelCol);
        _channelSplitInventoryGrid.RowPrePaint += OnChannelSplitInventoryRowPrePaint;
        _channelSplitInventoryGrid.CurrentCellDirtyStateChanged += (s, e) => { if (_channelSplitInventoryGrid.IsCurrentCellDirty) _channelSplitInventoryGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var btnRefresh = new Button { Text = "새로고침", Size = new Size(90, 28) };
        btnRefresh.Click += (s, e) => RefreshChannelSplitInventoryGrid();
        toolStrip.Controls.Add(btnRefresh);
        var btnSave = new Button { Text = "저장", Size = new Size(90, 28) };
        btnSave.Click += OnSaveChannelSplitInventoryClick;
        toolStrip.Controls.Add(btnSave);
        var btnDeleteMissing = new Button { Text = "미출현 항목 삭제", Size = new Size(110, 28) };
        btnDeleteMissing.Click += OnDeleteMissingChannelSplitInventoryClick;
        toolStrip.Controls.Add(btnDeleteMissing);
        _channelSplitInventoryFeedbackLabel = new Label { AutoSize = true, Padding = new Padding(10, 7, 0, 0), ForeColor = Color.DarkGreen };
        toolStrip.Controls.Add(_channelSplitInventoryFeedbackLabel);

        layout.Controls.Add(infoLabel, 0, 0);
        layout.Controls.Add(_channelSplitInventoryGrid, 0, 1);
        layout.Controls.Add(toolStrip, 0, 2);
        tabPage.Controls.Add(layout);
        return tabPage;
    }

    private void OnChannelSplitInventoryRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _channelSplitInventoryGrid.Rows.Count) return;
        var row = _channelSplitInventoryGrid.Rows[e.RowIndex];
        if (row.DataBoundItem is not AdChannelSplitInventoryEntry entry) return;

        if (entry.IsNew)
        {
            row.DefaultCellStyle.BackColor = Color.LightYellow;
            row.DefaultCellStyle.ForeColor = Color.Black;
        }
        else if (entry.IsMissingThisMonth)
        {
            row.DefaultCellStyle.BackColor = Color.WhiteSmoke;
            row.DefaultCellStyle.ForeColor = Color.Gray;
        }
        else
        {
            row.DefaultCellStyle.BackColor = _channelSplitInventoryGrid.DefaultCellStyle.BackColor;
            row.DefaultCellStyle.ForeColor = _channelSplitInventoryGrid.DefaultCellStyle.ForeColor;
        }
    }

    /// <summary>DB에 저장된 인벤토리와 현재 불러온 데이터의 실시간 (헤더,값) 집계를 합쳐 보여준다.
    /// 이번 파일에만 있으면 신규(IsNew), DB에만 있으면 미출현(IsMissingThisMonth)으로 표시한다.</summary>
    private void RefreshChannelSplitInventoryGrid()
    {
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode))
        {
            _channelSplitInventoryGrid.DataSource = null;
            return;
        }

        var saved = _channelSplitRepository.GetInventory(channelCode)
            .ToDictionary(e => (e.HeaderName, e.Value));

        var liveGroups = _loadedAdItems
            .Where(i => !string.IsNullOrEmpty(i.CampaignSrc) && !string.IsNullOrEmpty(i.CampaignKey))
            .GroupBy(i => (Header: i.CampaignSrc!, Value: i.CampaignKey!))
            .Select(g => new { g.Key.Header, g.Key.Value, Cost = g.Sum(i => i.Cost), Count = g.Count() })
            .ToList();

        var thisMonth = DateTime.Now.ToString("yyMM");
        var rows = new List<AdChannelSplitInventoryEntry>();

        foreach (var live in liveGroups)
        {
            saved.TryGetValue((live.Header, live.Value), out var existing);
            rows.Add(new AdChannelSplitInventoryEntry
            {
                Id = existing?.Id ?? 0,
                ChannelCode = channelCode,
                HeaderName = live.Header,
                Value = live.Value,
                TargetChannel = existing?.TargetChannel ?? string.Empty,
                ConfirmedAt = existing?.ConfirmedAt,
                LastSeenYymm = thisMonth,
                LastCost = live.Cost,
                RowCount = live.Count,
                IsNew = existing == null,
                IsMissingThisMonth = false,
            });
        }

        var liveKeys = liveGroups.Select(g => (g.Header, g.Value)).ToHashSet();
        foreach (var (key, entry) in saved)
        {
            if (liveKeys.Contains(key)) continue;
            rows.Add(new AdChannelSplitInventoryEntry
            {
                Id = entry.Id,
                ChannelCode = channelCode,
                HeaderName = entry.HeaderName,
                Value = entry.Value,
                TargetChannel = entry.TargetChannel,
                ConfirmedAt = entry.ConfirmedAt,
                LastSeenYymm = entry.LastSeenYymm,
                LastCost = entry.LastCost,
                RowCount = 0,
                IsNew = false,
                IsMissingThisMonth = true,
            });
        }

        var ordered = rows.OrderByDescending(r => r.IsNew).ThenByDescending(r => r.LastCost).ToList();
        _channelSplitInventoryGrid.DataSource = new BindingList<AdChannelSplitInventoryEntry>(ordered);
    }

    private void OnSaveChannelSplitInventoryClick(object? sender, EventArgs e)
    {
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode)) return;
        if (_channelSplitInventoryGrid.DataSource is not BindingList<AdChannelSplitInventoryEntry> rows) return;

        foreach (var row in rows.Where(r => !r.IsMissingThisMonth && !string.IsNullOrWhiteSpace(r.TargetChannel)))
        {
            _channelSplitRepository.UpsertInventoryEntry(channelCode, row.HeaderName, row.Value, row.TargetChannel, row.LastSeenYymm ?? DateTime.Now.ToString("yyMM"), row.LastCost);
        }

        RebuildChannelSplitResolver();
        ApplyChannelSplitToLoadedItems();
        ApplyUnmappedFilter();
        UpdateAdSummary();
        RefreshChannelSplitInventoryGrid();
        _channelSplitInventoryFeedbackLabel.Text = $"저장되었습니다 ({DateTime.Now:HH:mm:ss})";
    }

    private void OnDeleteMissingChannelSplitInventoryClick(object? sender, EventArgs e)
    {
        if (_channelSplitInventoryGrid.DataSource is not BindingList<AdChannelSplitInventoryEntry> rows) return;
        var missing = rows.Where(r => r.IsMissingThisMonth && r.Id != 0).ToList();
        if (missing.Count == 0) return;
        if (MessageBox.Show($"이번 파일에 나타나지 않은 캠페인 {missing.Count}건을 인벤토리에서 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        foreach (var m in missing) _channelSplitRepository.DeleteInventoryEntry(m.Id);
        RefreshChannelSplitInventoryGrid();
    }

    // ── 선판정 규칙(prerules) 서브탭 ──────────────────────────────

    private TabPage CreateChannelSplitPreruleTabPage()
    {
        var tabPage = new TabPage("선판정 규칙(prerules)");

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var leftPanel = new Panel { Dock = DockStyle.Fill };
        _channelSplitPreruleGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _channelSplitPreruleGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Priority", HeaderText = "우선순위", DataPropertyName = "Priority", Width = 60 },
            new DataGridViewTextBoxColumn { Name = "TargetChannel", HeaderText = "채널", DataPropertyName = "TargetChannel", Width = 90 },
            new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "메모", DataPropertyName = "Note", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _channelSplitPreruleGrid.SelectionChanged += OnChannelSplitPreruleSelectionChanged;

        var leftButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36 };
        var btnAddRule = new Button { Text = "규칙 추가", Size = new Size(90, 28) };
        btnAddRule.Click += OnAddChannelSplitPreruleClick;
        var btnDeleteRule = new Button { Text = "규칙 삭제", Size = new Size(90, 28) };
        btnDeleteRule.Click += OnDeleteChannelSplitPreruleClick;
        leftButtonPanel.Controls.Add(btnAddRule);
        leftButtonPanel.Controls.Add(btnDeleteRule);

        leftPanel.Controls.Add(_channelSplitPreruleGrid);
        leftPanel.Controls.Add(leftButtonPanel);

        var rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var summaryPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        _channelSplitPriorityInput = new NumericUpDown { Width = 60, Minimum = 0, Maximum = 9999 };
        _channelSplitPreruleTargetChannelCombo = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList, DataSource = GetChannelSplitTargetOptions() };
        _channelSplitPreruleNoteTextBox = new TextBox { Width = 160 };
        _channelSplitPreruleEnabledCheckBox = new CheckBox { Text = "사용", AutoSize = true, Checked = true, Padding = new Padding(6, 4, 0, 0) };
        var btnSaveSummary = new Button { Text = "규칙 정보 저장", Size = new Size(110, 28) };
        btnSaveSummary.Click += OnSaveChannelSplitPreruleSummaryClick;

        summaryPanel.Controls.Add(new Label { Text = "우선순위:", AutoSize = true, Padding = new Padding(0, 7, 3, 0) });
        summaryPanel.Controls.Add(_channelSplitPriorityInput);
        summaryPanel.Controls.Add(new Label { Text = "채널:", AutoSize = true, Padding = new Padding(10, 7, 3, 0) });
        summaryPanel.Controls.Add(_channelSplitPreruleTargetChannelCombo);
        summaryPanel.Controls.Add(new Label { Text = "메모:", AutoSize = true, Padding = new Padding(10, 7, 3, 0) });
        summaryPanel.Controls.Add(_channelSplitPreruleNoteTextBox);
        summaryPanel.Controls.Add(_channelSplitPreruleEnabledCheckBox);
        summaryPanel.Controls.Add(btnSaveSummary);
        _channelSplitPrerulePreviewLabel = new Label { Text = "예상 매칭 건수: -", AutoSize = true, Padding = new Padding(15, 7, 0, 0), ForeColor = Color.Blue, Font = new Font(Font, FontStyle.Bold) };
        summaryPanel.Controls.Add(_channelSplitPrerulePreviewLabel);

        _channelSplitPreruleDetailGrid = new ExcelLikeDataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false };
        var headerNameColumn = new DataGridViewTextBoxColumn { Name = "HeaderName", HeaderText = "비교할 원본 헤더", DataPropertyName = "HeaderName", Width = 160 };
        var operatorColumn = new DataGridViewComboBoxColumn { Name = "Operator", HeaderText = "조건", DataPropertyName = "Operator", DataSource = Enum.GetValues(typeof(AdConditionOperator)), Width = 130 };
        var targetValueColumn = new DataGridViewTextBoxColumn { Name = "TargetValue", HeaderText = "비교할 값", DataPropertyName = "TargetValue", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };
        var logicColumn = new DataGridViewComboBoxColumn { Name = "Logic", HeaderText = "다음 조건과 결합", DataPropertyName = "Logic", DataSource = Enum.GetValues(typeof(ConditionLogic)), Width = 110 };
        _channelSplitPreruleDetailGrid.Columns.AddRange(headerNameColumn, operatorColumn, targetValueColumn, logicColumn);
        _channelSplitPreruleDetailGrid.CurrentCellDirtyStateChanged += (s, e) => { if (_channelSplitPreruleDetailGrid.IsCurrentCellDirty) _channelSplitPreruleDetailGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _channelSplitPreruleDetailGrid.CellValueChanged += (s, e) => UpdateChannelSplitPrerulePreview();

        var detailButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var btnAddDetail = new Button { Text = "조건 추가", Size = new Size(90, 28) };
        btnAddDetail.Click += OnAddChannelSplitPreruleDetailClick;
        var btnDeleteDetail = new Button { Text = "조건 삭제", Size = new Size(90, 28) };
        btnDeleteDetail.Click += OnDeleteChannelSplitPreruleDetailClick;
        var btnSaveDetails = new Button { Text = "상세조건 저장", Size = new Size(110, 28) };
        btnSaveDetails.Click += OnSaveChannelSplitPreruleDetailsClick;
        detailButtonPanel.Controls.Add(btnAddDetail);
        detailButtonPanel.Controls.Add(btnDeleteDetail);
        detailButtonPanel.Controls.Add(btnSaveDetails);
        _channelSplitPreruleSaveFeedbackLabel = new Label { AutoSize = true, Padding = new Padding(15, 7, 0, 0), ForeColor = Color.DarkGreen };
        detailButtonPanel.Controls.Add(_channelSplitPreruleSaveFeedbackLabel);

        rightPanel.Controls.Add(summaryPanel, 0, 0);
        rightPanel.Controls.Add(_channelSplitPreruleDetailGrid, 0, 1);
        rightPanel.Controls.Add(detailButtonPanel, 0, 2);

        mainLayout.Controls.Add(leftPanel, 0, 0);
        mainLayout.Controls.Add(rightPanel, 1, 0);
        tabPage.Controls.Add(mainLayout);

        SetChannelSplitPreruleEditorEnabled(false);
        return tabPage;
    }

    private void SetChannelSplitPreruleEditorEnabled(bool enabled)
    {
        _channelSplitPriorityInput.Enabled = enabled;
        _channelSplitPreruleTargetChannelCombo.Enabled = enabled;
        _channelSplitPreruleNoteTextBox.Enabled = enabled;
        _channelSplitPreruleEnabledCheckBox.Enabled = enabled;
        _channelSplitPreruleDetailGrid.Enabled = enabled;
        if (!enabled)
        {
            _channelSplitPriorityInput.Value = 0;
            _channelSplitPreruleNoteTextBox.Text = string.Empty;
            _channelSplitPreruleDetailGrid.DataSource = null;
        }
        UpdateChannelSplitPrerulePreview();
    }

    private void LoadChannelSplitPrerules(string channelCode)
    {
        _channelSplitPreruleGrid.DataSource = new BindingList<AdChannelSplitPrerule>(_channelSplitRepository.GetPrerules(channelCode));
        _selectedChannelSplitPreruleId = -1;
        SetChannelSplitPreruleEditorEnabled(false);
    }

    private void OnChannelSplitPreruleSelectionChanged(object? sender, EventArgs e)
    {
        if (_channelSplitPreruleGrid.CurrentRow?.DataBoundItem is not AdChannelSplitPrerule rule)
        {
            _selectedChannelSplitPreruleId = -1;
            SetChannelSplitPreruleEditorEnabled(false);
            return;
        }

        _selectedChannelSplitPreruleId = rule.Id;
        _channelSplitPriorityInput.Value = rule.Priority;
        _channelSplitPreruleTargetChannelCombo.SelectedItem = rule.TargetChannel;
        _channelSplitPreruleNoteTextBox.Text = rule.Note;
        _channelSplitPreruleEnabledCheckBox.Checked = rule.Enabled;
        _channelSplitPreruleDetailGrid.DataSource = new BindingList<AdChannelSplitPreruleDetail>(_channelSplitRepository.GetPreruleDetails(rule.Id));
        SetChannelSplitPreruleEditorEnabled(true);
    }

    private void OnAddChannelSplitPreruleClick(object? sender, EventArgs e)
    {
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode))
        {
            MessageBox.Show("먼저 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var defaultTarget = GetChannelSplitTargetOptions().FirstOrDefault() ?? string.Empty;
        var newRuleId = _channelSplitRepository.AddPreruleWithDetails(channelCode, 10, defaultTarget, string.Empty, true, []);
        LoadChannelSplitPrerules(channelCode);
        SelectChannelSplitPreruleById(newRuleId);
    }

    private void SelectChannelSplitPreruleById(long ruleId)
    {
        foreach (DataGridViewRow row in _channelSplitPreruleGrid.Rows)
        {
            if (row.DataBoundItem is AdChannelSplitPrerule rule && rule.Id == ruleId)
            {
                _channelSplitPreruleGrid.CurrentCell = row.Cells[0];
                break;
            }
        }
    }

    private void OnDeleteChannelSplitPreruleClick(object? sender, EventArgs e)
    {
        if (_selectedChannelSplitPreruleId < 0) return;
        if (MessageBox.Show("선택한 선판정 규칙과 그 상세조건을 모두 삭제합니다. 계속하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _channelSplitRepository.DeletePrerule(_selectedChannelSplitPreruleId);
        var channelCode = _selectedChannel?.ChannelCode;
        if (string.IsNullOrEmpty(channelCode)) return;
        LoadChannelSplitPrerules(channelCode);
        RebuildChannelSplitResolver();
        ApplyChannelSplitToLoadedItems();
        ApplyUnmappedFilter();
        UpdateAdSummary();
    }

    private void OnSaveChannelSplitPreruleSummaryClick(object? sender, EventArgs e)
    {
        if (_selectedChannelSplitPreruleId < 0) return;
        var ruleId = _selectedChannelSplitPreruleId;
        var targetChannel = _channelSplitPreruleTargetChannelCombo.SelectedItem as string ?? _channelSplitPreruleTargetChannelCombo.Text;
        _channelSplitRepository.UpdatePreruleSummary(ruleId, (int)_channelSplitPriorityInput.Value, targetChannel, _channelSplitPreruleNoteTextBox.Text, _channelSplitPreruleEnabledCheckBox.Checked);

        var channelCode = _selectedChannel?.ChannelCode;
        if (!string.IsNullOrEmpty(channelCode))
        {
            // LoadChannelSplitPrerules가 목록을 다시 불러오며 선택을 초기화하므로, 같은 규칙을
            // 다시 선택해 편집을 이어갈 수 있게 한다.
            LoadChannelSplitPrerules(channelCode);
            SelectChannelSplitPreruleById(ruleId);
            RebuildChannelSplitResolver();
            ApplyChannelSplitToLoadedItems();
            ApplyUnmappedFilter();
            UpdateAdSummary();
        }
        _channelSplitPreruleSaveFeedbackLabel.Text = $"규칙 정보 저장됨 ({DateTime.Now:HH:mm:ss})";
    }

    private void OnAddChannelSplitPreruleDetailClick(object? sender, EventArgs e)
    {
        if (_channelSplitPreruleDetailGrid.DataSource is not BindingList<AdChannelSplitPreruleDetail> details) return;
        details.Add(new AdChannelSplitPreruleDetail { RuleId = _selectedChannelSplitPreruleId, HeaderName = string.Empty, Operator = AdConditionOperator.Equals, TargetValue = string.Empty, Logic = ConditionLogic.And });
        UpdateChannelSplitPrerulePreview();
    }

    private void OnDeleteChannelSplitPreruleDetailClick(object? sender, EventArgs e)
    {
        if (_channelSplitPreruleDetailGrid.DataSource is not BindingList<AdChannelSplitPreruleDetail> details) return;
        if (_channelSplitPreruleDetailGrid.CurrentRow?.DataBoundItem is not AdChannelSplitPreruleDetail detail) return;
        details.Remove(detail);
        UpdateChannelSplitPrerulePreview();
    }

    private void OnSaveChannelSplitPreruleDetailsClick(object? sender, EventArgs e)
    {
        if (_selectedChannelSplitPreruleId < 0) return;
        if (_channelSplitPreruleDetailGrid.DataSource is not BindingList<AdChannelSplitPreruleDetail> details) return;

        _channelSplitRepository.ReplacePreruleDetails(_selectedChannelSplitPreruleId, details.ToList());
        RebuildChannelSplitResolver();
        ApplyChannelSplitToLoadedItems();
        ApplyUnmappedFilter();
        UpdateAdSummary();
        _channelSplitPreruleSaveFeedbackLabel.Text = $"상세조건 저장됨 ({DateTime.Now:HH:mm:ss})";
    }

    /// <summary>현재 불러온 광고비 데이터(_loadedAdItems)에 조건을 즉시 적용해 예상 매칭 건수를 보여준다.</summary>
    private void UpdateChannelSplitPrerulePreview()
    {
        if (_channelSplitPreruleDetailGrid.DataSource is not BindingList<AdChannelSplitPreruleDetail> details || details.Count == 0)
        {
            _channelSplitPrerulePreviewLabel.Text = "예상 매칭 건수: -";
            return;
        }

        if (_loadedAdItems.Count == 0)
        {
            _channelSplitPrerulePreviewLabel.Text = "예상 매칭 건수: (광고비 파일을 불러와야 미리볼 수 있습니다)";
            return;
        }

        var validDetails = details.Where(d => !string.IsNullOrWhiteSpace(d.HeaderName) && (!string.IsNullOrWhiteSpace(d.TargetValue) || d.Operator == AdConditionOperator.IsZero)).ToList();
        if (validDetails.Count == 0)
        {
            _channelSplitPrerulePreviewLabel.Text = "예상 매칭 건수: (헤더/값을 입력하세요)";
            return;
        }

        var matchCount = _loadedAdItems.Count(i => AdChannelSplitEvaluator.Matches(validDetails, i));
        _channelSplitPrerulePreviewLabel.Text = $"예상 매칭 건수: {matchCount}건 / 전체 {_loadedAdItems.Count}건";
    }

}

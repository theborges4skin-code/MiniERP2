using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.DataLoaders;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

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
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly SettingsService _settingsService = new();
    private readonly AdSpendLoader _adSpendLoader = new();
    private readonly AdLegacyMigrationService _legacyMigrationService = new();
    private readonly ProfitFactRepository _profitFactRepository = new();

    private ComboBox _channelComboBox = new();
    private TabControl _tabControl = new();
    private ChannelConfig? _currentChannelConfig;

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
        _channelComboBox = new ComboBox { Size = new Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList };
        _channelComboBox.SelectedIndexChanged += (s, e) => OnChannelChanged();
        topPanel.Controls.Add(_channelComboBox);

        var btnLegacyImport = new Button { Text = "SalesManagerV2 데이터 가져오기", AutoSize = true, Padding = new Padding(8, 5, 8, 5) };
        btnLegacyImport.Click += OnLegacyImportClick;
        topPanel.Controls.Add(btnLegacyImport);

        _tabControl = new TabControl { Dock = DockStyle.Fill };
        _tabControl.TabPages.Add(CreateAdDataTabPage());
        _tabControl.TabPages.Add(CreateTempRuleTabPage());
        _conditionDetailTabPage = CreateConditionDetailTabPage();
        _tabControl.TabPages.Add(_conditionDetailTabPage);
        _tabControl.TabPages.Add(CreateExceptionTabPage());
        mainLayout.Controls.Add(topPanel, 0, 0);
        mainLayout.Controls.Add(_tabControl, 0, 1);
        Controls.Add(mainLayout);
    }

    private void LoadChannels()
    {
        var channels = _salesChannelRepository.GetAll();
        _channelComboBox.DataSource = channels;
        _channelComboBox.DisplayMember = "ChannelName";
        _channelComboBox.ValueMember = "ChannelCode";
    }

    private void OnChannelChanged()
    {
        var channelCode = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(channelCode)) return;

        _currentChannelConfig = _channelConfigService.Load().FirstOrDefault(c => c.ChannelCode == channelCode)
            ?? new ChannelConfig { ChannelCode = channelCode, ChannelName = (_channelComboBox.SelectedItem as SalesChannel)?.ChannelName ?? channelCode };

        _loadedAdItems = [];
        _adDataGrid.DataSource = null;
        UpdateAdSummary();

        LoadTempRules(channelCode);
        LoadConditionRules(channelCode);
        LoadExceptionRules(channelCode);
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
        var channelCode = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(channelCode))
        {
            MessageBox.Show("먼저 규칙을 이관할 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var folderDialog = new FolderBrowserDialog { Description = "SalesManagerV2의 config 폴더(ad_condition_rules.json 등이 있는 폴더)를 선택하세요" };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

        var channelName = (_channelComboBox.SelectedItem as SalesChannel)?.ChannelName ?? channelCode;
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
        var btnLoad = new Button { Text = "광고비 파일 불러오기", Size = new Size(140, 30) };
        btnLoad.Click += OnLoadAdFileClick;
        toolStrip.Controls.Add(btnLoad);

        var btnExport = new Button { Text = "분석결과 내보내기", Size = new Size(120, 30) };
        btnExport.Click += OnExportAdResultClick;
        toolStrip.Controls.Add(btnExport);

        var btnSaveReport = new Button { Text = "보고서에 저장", Size = new Size(100, 30) };
        btnSaveReport.Click += OnSaveAdFactClick;
        toolStrip.Controls.Add(btnSaveReport);

        _unmappedOnlyCheckBox = new CheckBox
        {
            Text = "ubbf8ub9e4ud551ub9cc ubcf4uae30",
            AutoSize = true,
            Padding = new Padding(8, 6, 0, 0),
        };
        _unmappedOnlyCheckBox.CheckedChanged += (s, e) => ApplyUnmappedFilter();
        toolStrip.Controls.Add(_unmappedOnlyCheckBox);

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

        _adSummaryLabel = new Label { Dock = DockStyle.Fill, Text = "광고비 파일을 불러오세요.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        layout.Controls.Add(toolStrip, 0, 0);
        // 우측: 상품그룹별 광고비 집계 패널
        _adGroupGrid = new DataGridView
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
        if (_currentChannelConfig == null || string.IsNullOrEmpty(_channelComboBox.SelectedValue as string))
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

        var channelCode = (string)_channelComboBox.SelectedValue;
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
                    if (dialog.ShowDialog(this) != DialogResult.OK) continue;
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

        _loadedAdItems = allItems;
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
        return form.ShowDialog(this) == DialogResult.OK ? combo.SelectedItem as Models.AdFileLayout : null;
    }

    private void ApplyUnmappedFilter()
    {
        var source = _unmappedOnlyCheckBox.Checked
            ? _loadedAdItems.Where(i => string.IsNullOrEmpty(i.MappedGroup) && i.MatchType != "예외처리").ToList()
            : _loadedAdItems;
        _adDataGrid.DataSource = new BindingList<AdSpendItem>(source);
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
        _adSummaryLabel.Text = $"총 {_loadedAdItems.Count}건 | 매핑 {mapped}건 | 예외 {excluded}건 | 합계 광고비 {totalCost:N0}원";
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
        var channelCode = _channelComboBox.SelectedValue as string ?? string.Empty;
        var channelName = (_channelComboBox.SelectedItem as SalesChannel)?.ChannelName ?? channelCode;
        using var periodDialog = new AdFactPeriodInputDialog(channelCode, channelName, _profitFactRepository);
        if (periodDialog.ShowDialog(this) != DialogResult.OK) return;
        var period = periodDialog.SelectedPeriod;

        Cursor = Cursors.WaitCursor;
        try
        {
            var items = _loadedAdItems.ToList();
            var facts = await Task.Run(() =>
            {
                return items
                    .Where(i => !string.IsNullOrEmpty(i.MappedGroup) && i.MatchType != "예외처리")
                    .GroupBy(i => i.MappedGroup!)
                    .Select(g => new AdFactRow
                    {
                        ProductGroup = g.Key,
                        AdCost = g.Sum(i => i.Cost),
                    })
                    .ToList();
            });

            await Task.Run(() => _profitFactRepository.SaveAdFacts(period, channelCode, channelName, facts));
            MessageBox.Show($"보고서 저장 완료 — {period} / {channelName} / {facts.Count}개 그룹", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private async void OnExportAdResultClick(object? sender, EventArgs e)
    {
        if (_loadedAdItems.Count == 0)
        {
            MessageBox.Show("내보낼 광고비 분석 결과가 없습니다. 먼저 광고비 파일을 불러오세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var channelName = (_channelComboBox.SelectedItem as SalesChannel)?.ChannelName ?? "채널";
        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"{channelName}_광고분析_{DateTime.Now:yyMM}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("AdMappingExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var filePath = sfd.FileName;
        _settingsService.SetLastFolder("AdMappingExport", Path.GetDirectoryName(filePath)!);

        var itemsSnapshot = _loadedAdItems.ToList();

        Cursor = Cursors.WaitCursor;
        try
        {
            await Task.Run(() =>
            {
                ExcelLicense.Ensure();
                using var package = new ExcelPackage();
                WriteAdDetailSheetStatic(package.Workbook.Worksheets.Add("광고매핑상세"), channelName, itemsSnapshot);
                WriteAdGroupSummarySheetStatic(package.Workbook.Worksheets.Add("그룹별_광고비"), channelName, itemsSnapshot);
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
        string[] stdHeaders = ["AD_PRODUCT_NAME", "AD_PRODUCT_ID", "AD_OPTION", "AD_COST", "AD_EXTRA1", "AD_EXTRA2", "MAPPED_GROUP", "MATCH_TYPE", "MAPPING_STATUS"];
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
            row++;
        }
        sheet.Cells.AutoFitColumns();
    }

    private static void WriteAdGroupSummarySheetStatic(ExcelWorksheet sheet, string channelName, List<AdSpendItem> items)
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
    }

    private void OnAddTempRuleFromSelectedAdItem()
    {
        if (_adDataGrid.CurrentRow?.DataBoundItem is not AdSpendItem item) return;
        var channelCode = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(channelCode)) return;

        using var dialog = new AdTargetGroupPromptDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.TargetGroup)) return;

        _adMappingRepository.UpsertTempRule(channelCode, AdMappingEngine.BuildKey(item), dialog.TargetGroup);
        LoadTempRules(channelCode);
        ReapplyMapping(channelCode); // UpdateAdSummary()를 호출해 _adSummaryLabel을 새 요약으로 갱신한다.
        // 노션 5.1 후속 점검: 그리드 재구성 직후 모달을 띄우면 같은 위험군이라 비모달 라벨로 대체.
        _adSummaryLabel.Text = $"[임시 매핑으로 등록했습니다 — {DateTime.Now:HH:mm:ss}] {_adSummaryLabel.Text}";
    }

    private void OnAddConditionRuleFromSelectedAdItem()
    {
        if (_adDataGrid.CurrentRow?.DataBoundItem is not AdSpendItem item) return;
        var channelCode = _channelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(channelCode) || string.IsNullOrWhiteSpace(item.ProductName)) return;

        using var dialog = new AdTargetGroupPromptDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.TargetGroup)) return;

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
        var channelCode = _channelComboBox.SelectedValue as string;
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
        var channelCode = _channelComboBox.SelectedValue as string;
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
        var channelCode = _channelComboBox.SelectedValue as string;
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
        var channelCode = _channelComboBox.SelectedValue as string;
        if (!string.IsNullOrEmpty(channelCode)) { LoadConditionRules(channelCode); ReapplyMapping(channelCode); }
    }

    private void OnSaveConditionSummaryClick(object? sender, EventArgs e)
    {
        if (_selectedConditionRuleId < 0) return;
        var ruleId = _selectedConditionRuleId;
        _adMappingRepository.UpdateConditionRuleSummary(ruleId, _conditionKeyTextBox.Text, _conditionTargetGroupTextBox.Text);

        var channelCode = _channelComboBox.SelectedValue as string;
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
        var channelCode = _channelComboBox.SelectedValue as string;
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
        var channelCode = _channelComboBox.SelectedValue as string;
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
        var channelCode = _channelComboBox.SelectedValue as string;
        if (!string.IsNullOrEmpty(channelCode)) ReapplyMapping(channelCode);
    }

}

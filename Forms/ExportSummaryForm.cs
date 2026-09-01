using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.DataLoaders;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 수출요약보고서 화면(export_summary_report_기획.md). 수출신고·판매·송금 3개 트랙을 각각
/// 독립적으로 불러와 마켓×월로 집계한 뒤 엑셀로 내보낸다. 세 트랙 금액은 서로 일치시키지
/// 않고 나란히 보여주기만 한다(기획서 1절 설계 원칙).
/// </summary>
public class ExportSummaryForm : Form
{
    private readonly ExportSummaryConfigService _configService = new();
    private readonly SettingsService _settingsService = new();
    private ExportSummaryConfig _config = new();

    private readonly BindingList<ExportSummaryLoadedSource> _sources = new();
    private ExcelLikeDataGridView _sourceGrid = new();
    private ExcelLikeDataGridView _pivotGrid = new();
    private Label _statusLabel = new();

    // 설정 탭 컨트롤 — export_summary_config.json을 프로그램 안에서 편집할 수 있게 한다.
    private NumericUpDown _declHeaderRowInput = new();
    private TextBox _declDateColumnBox = new();
    private TextBox _declAmountColumnBox = new();
    private TextBox _declCurrencyColumnBox = new();
    private NumericUpDown _remitHeaderRowInput = new();
    private TextBox _remitDateColumnBox = new();
    private TextBox _remitAmountColumnBox = new();
    private TextBox _remitDescriptionColumnBox = new();
    private ExcelLikeDataGridView _salesMappingGrid = new();
    private Label _settingsStatusLabel = new();

    /// <summary>tidy 원시 한 줄. 마켓×지표×통화×연월로 집계된 최소 단위.</summary>
    private record TidyRow(string MarketName, string MarketGroup, string YearMonth, string Indicator, string Currency, decimal Amount);

    /// <summary>설정 탭의 판매(트랙B) 마켓별 헤더 편집용 그리드 행. 저장 시 _config.SalesMarkets로 반영한다.</summary>
    private class SalesMarketMappingRow
    {
        public string MarketCode { get; set; } = "";
        public string MarketName { get; set; } = "";
        public int HeaderRow { get; set; } = 1;
        public string DateColumn { get; set; } = "";
        public string AmountColumn { get; set; } = "";
    }

    public ExportSummaryForm()
    {
        _config = _configService.Load();
        EnsureUsdUnmatched();
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
    }

    /// <summary>기존 config 파일에 USD_UNMATCHED 마켓/통화매핑이 없으면 런타임에 추가한다(기본값 재생성 없이 업그레이드).</summary>
    private void EnsureUsdUnmatched()
    {
        var changed = false;
        if (!_config.Markets.Any(m => m.MarketCode == "USD_UNMATCHED"))
        {
            _config.Markets.Add(new ExportSummaryMarket { MarketCode = "USD_UNMATCHED", MarketName = "USD(미분리)", Currency = "USD" });
            changed = true;
        }
        if (!_config.DeclarationTrack.CurrencyToMarket.ContainsKey("USD"))
        {
            _config.DeclarationTrack.CurrencyToMarket["USD"] = "USD_UNMATCHED";
            changed = true;
        }
        if (changed) _configService.Save(_config);
    }

    private void InitializeComponent()
    {
        Text = "수출요약보고서";
        Size = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnAddDeclaration = new Button { Text = "A. 수출신고 파일 추가", Size = new Size(140, 30) };
        var btnAddSales = new Button { Text = "B. 판매(마켓선택)", Size = new Size(120, 30) };
        var btnAutoSales = new Button { Text = "B. 파일명 자동탐지", Size = new Size(130, 30) };
        var btnAddRemittance = new Button { Text = "C. 송금 파일 추가", Size = new Size(120, 30) };
        var btnManualEntry = new Button { Text = "수동 입력 편집기", Size = new Size(120, 30) };
        var btnLoadDraft = new Button { Text = "임시저장 불러오기", Size = new Size(130, 30) };
        var btnRemoveSource = new Button { Text = "선택 소스 제거", Size = new Size(110, 30) };
        var btnAggregate = new Button { Text = "집계", Size = new Size(80, 30), Font = new Font(Font, FontStyle.Bold) };
        var btnExport = new Button { Text = "엑셀로 내보내기", Size = new Size(120, 30) };

        btnAddDeclaration.Click += (s, e) => AddDeclarationFile();
        btnAddSales.Click += (s, e) => AddSalesFile();
        btnAutoSales.Click += (s, e) => AddSalesFileAutoDetect();
        btnAddRemittance.Click += (s, e) => AddRemittanceFile();
        btnManualEntry.Click += (s, e) => AddManualEntry();
        btnLoadDraft.Click += (s, e) => LoadAllDrafts();
        btnRemoveSource.Click += (s, e) => RemoveSelectedSource();
        btnAggregate.Click += (s, e) => Aggregate();
        btnExport.Click += (s, e) => Export();

        toolStrip.Controls.Add(btnAddDeclaration);
        toolStrip.Controls.Add(btnAddSales);
        toolStrip.Controls.Add(btnAutoSales);
        toolStrip.Controls.Add(btnAddRemittance);
        toolStrip.Controls.Add(btnManualEntry);
        toolStrip.Controls.Add(btnLoadDraft);
        toolStrip.Controls.Add(btnRemoveSource);
        toolStrip.Controls.Add(btnAggregate);
        toolStrip.Controls.Add(btnExport);

        _sourceGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "ExportSummaryForm.SourceGrid",
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
            MultiSelect = true,
            ReadOnly = true,
        };
        _sourceGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "트랙", Name = "TrackName", DataPropertyName = "TrackName", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "마켓", Name = "MarketName", DataPropertyName = "MarketName", Width = 140 },
            new DataGridViewTextBoxColumn { HeaderText = "파일명", Name = "FileName", DataPropertyName = "FileName", Width = 320 },
            new DataGridViewTextBoxColumn { HeaderText = "건수", Name = "RowCount", DataPropertyName = "RowCount", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "미매핑", Name = "UnmatchedRowCount", DataPropertyName = "UnmatchedRowCount", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        _sourceGrid.DataSource = _sources;

        _pivotGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "ExportSummaryForm.PivotGrid",
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
            MultiSelect = true,
            ReadOnly = true,
        };
        _pivotGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "마켓", Name = "MarketName", DataPropertyName = "MarketName", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "마켓그룹", Name = "MarketGroup", DataPropertyName = "MarketGroup", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "연월", Name = "YearMonth", DataPropertyName = "YearMonth", Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "지표", Name = "Indicator", DataPropertyName = "Indicator", Width = 75 },
            new DataGridViewTextBoxColumn { HeaderText = "통화", Name = "Currency", DataPropertyName = "Currency", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "금액", Name = "Amount", DataPropertyName = "Amount", Width = 140, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );

        _statusLabel = new Label { Dock = DockStyle.Fill, Text = "파일을 추가한 뒤 '집계'를 눌러 마켓×월 표를 만드세요.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(_sourceGrid, 0, 1);
        mainLayout.Controls.Add(_pivotGrid, 0, 2);
        mainLayout.Controls.Add(_statusLabel, 0, 3);

        var tabControl = new TabControl { Dock = DockStyle.Fill };
        var filesTab = new TabPage("파일 불러오기");
        filesTab.Controls.Add(mainLayout);
        var settingsTab = new TabPage("설정");
        BuildSettingsTab(settingsTab);
        tabControl.TabPages.Add(filesTab);
        tabControl.TabPages.Add(settingsTab);

        Controls.Add(tabControl);
    }

    /// <summary>
    /// export_summary_config.json의 헤더 컬럼명을 JSON 파일을 직접 열지 않고도 고칠 수 있는 탭.
    /// 통화→마켓 매핑, 송금 규칙 목록 같은 복잡한 항목은 그대로 JSON 편집에 맡기고, 가장 자주
    /// 바꿔야 하는 값(트랙별 헤더 행/컬럼명)만 다룬다.
    /// </summary>
    private void BuildSettingsTab(TabPage tab)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var declGroup = new GroupBox { Text = "A. 수출신고", Dock = DockStyle.Fill };
        var declLayout = BuildFieldRow(
            ("헤더행", _declHeaderRowInput = new NumericUpDown { Minimum = 1, Maximum = 100, Width = 60 }),
            ("결제일자 컬럼", _declDateColumnBox = new TextBox { Width = 150 }),
            ("결제금액 컬럼", _declAmountColumnBox = new TextBox { Width = 150 }),
            ("통화코드 컬럼", _declCurrencyColumnBox = new TextBox { Width = 120 }));
        declGroup.Controls.Add(declLayout);

        var remitGroup = new GroupBox { Text = "C. 송금", Dock = DockStyle.Fill };
        var remitLayout = BuildFieldRow(
            ("헤더행", _remitHeaderRowInput = new NumericUpDown { Minimum = 1, Maximum = 100, Width = 60 }),
            ("일자 컬럼", _remitDateColumnBox = new TextBox { Width = 120 }),
            ("금액 컬럼", _remitAmountColumnBox = new TextBox { Width = 120 }),
            ("Description 컬럼", _remitDescriptionColumnBox = new TextBox { Width = 150 }));
        remitGroup.Controls.Add(remitLayout);

        var salesGroup = new GroupBox { Text = "B. 판매 (마켓별 헤더)", Dock = DockStyle.Fill };
        _salesMappingGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "ExportSummaryForm.SalesMappingGrid",
            AutoGenerateColumns = false,
        };
        _salesMappingGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "마켓코드", Name = "MarketCode", DataPropertyName = "MarketCode", Width = 70, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "마켓명", Name = "MarketName", DataPropertyName = "MarketName", Width = 150, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "헤더행", Name = "HeaderRow", DataPropertyName = "HeaderRow", Width = 55 },
            new DataGridViewTextBoxColumn { HeaderText = "결제일자 컬럼", Name = "DateColumn", DataPropertyName = "DateColumn", Width = 220 },
            new DataGridViewTextBoxColumn { HeaderText = "판매액 컬럼", Name = "AmountColumn", DataPropertyName = "AmountColumn", Width = 220 }
        );
        salesGroup.Controls.Add(_salesMappingGrid);

        var bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var btnSave = new Button { Text = "저장", Size = new Size(90, 30), Font = new Font(Font, FontStyle.Bold) };
        var btnReload = new Button { Text = "파일에서 다시 불러오기", Size = new Size(160, 30) };
        btnSave.Click += (s, e) => SaveSettings();
        btnReload.Click += (s, e) => LoadSettingsIntoTab();
        bottomPanel.Controls.Add(btnSave);
        bottomPanel.Controls.Add(btnReload);
        _settingsStatusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 8, 0, 0) };
        bottomPanel.Controls.Add(_settingsStatusLabel);

        layout.Controls.Add(declGroup, 0, 0);
        layout.Controls.Add(salesGroup, 0, 1);
        layout.Controls.Add(remitGroup, 0, 2);
        layout.Controls.Add(bottomPanel, 0, 3);
        tab.Controls.Add(layout);

        LoadSettingsIntoTab();
    }

    private static FlowLayoutPanel BuildFieldRow(params (string Label, Control Input)[] fields)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(8) };
        foreach (var (label, input) in fields)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 4, 0) });
            input.Margin = new Padding(0, 3, 16, 0);
            panel.Controls.Add(input);
        }
        return panel;
    }

    /// <summary>_config의 현재 값을 설정 탭 컨트롤에 채운다(초기 표시, '파일에서 다시 불러오기' 둘 다 사용).</summary>
    private void LoadSettingsIntoTab()
    {
        _declHeaderRowInput.Value = Math.Max(1, _config.DeclarationTrack.HeaderRow);
        _declDateColumnBox.Text = _config.DeclarationTrack.DateColumn;
        _declAmountColumnBox.Text = _config.DeclarationTrack.AmountColumn;
        _declCurrencyColumnBox.Text = _config.DeclarationTrack.CurrencyColumn;

        _remitHeaderRowInput.Value = Math.Max(1, _config.RemittanceTrack.HeaderRow);
        _remitDateColumnBox.Text = _config.RemittanceTrack.DateColumn;
        _remitAmountColumnBox.Text = _config.RemittanceTrack.AmountColumn;
        _remitDescriptionColumnBox.Text = _config.RemittanceTrack.DescriptionColumn;

        var rows = _config.SalesMarkets.Select(m => new SalesMarketMappingRow
        {
            MarketCode = m.MarketCode,
            MarketName = _config.Markets.FirstOrDefault(x => x.MarketCode == m.MarketCode)?.MarketName ?? m.MarketCode,
            HeaderRow = m.HeaderRow,
            DateColumn = m.DateColumn,
            AmountColumn = m.AmountColumn,
        }).ToList();
        _salesMappingGrid.DataSource = new BindingList<SalesMarketMappingRow>(rows);

        _settingsStatusLabel.Text = "";
    }

    /// <summary>설정 탭의 현재 값을 _config에 반영하고 export_summary_config.json에 저장한다.</summary>
    private void SaveSettings()
    {
        _config.DeclarationTrack.HeaderRow = (int)_declHeaderRowInput.Value;
        _config.DeclarationTrack.DateColumn = _declDateColumnBox.Text.Trim();
        _config.DeclarationTrack.AmountColumn = _declAmountColumnBox.Text.Trim();
        _config.DeclarationTrack.CurrencyColumn = _declCurrencyColumnBox.Text.Trim();

        _config.RemittanceTrack.HeaderRow = (int)_remitHeaderRowInput.Value;
        _config.RemittanceTrack.DateColumn = _remitDateColumnBox.Text.Trim();
        _config.RemittanceTrack.AmountColumn = _remitAmountColumnBox.Text.Trim();
        _config.RemittanceTrack.DescriptionColumn = _remitDescriptionColumnBox.Text.Trim();

        if (_salesMappingGrid.DataSource is BindingList<SalesMarketMappingRow> rows)
        {
            foreach (var row in rows)
            {
                var mapping = _config.SalesMarkets.FirstOrDefault(m => m.MarketCode == row.MarketCode);
                if (mapping is null) continue;
                mapping.HeaderRow = row.HeaderRow;
                mapping.DateColumn = row.DateColumn.Trim();
                mapping.AmountColumn = row.AmountColumn.Trim();
            }
        }

        _configService.Save(_config);
        _settingsStatusLabel.Text = $"저장 완료 ({DateTime.Now:HH:mm:ss})";
    }

    private void AddDeclarationFile()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel/CSV (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv|All files (*.*)|*.*",
            Title = "수출신고 내역 파일을 선택하세요 (여러 개 선택 가능)",
            Multiselect = true,
            InitialDirectory = _settingsService.GetLastFolder("ExportSummary.Declaration") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("ExportSummary.Declaration", Path.GetDirectoryName(ofd.FileNames[0])!);

        var errors = new List<string>();
        var totalUnmatched = 0;
        string? unmatchedSample = null;

        foreach (var fileName in ofd.FileNames)
        {
            try
            {
                var result = ExportSummaryLoader.LoadDeclarationFile(fileName, _config.DeclarationTrack);
                _sources.Add(new ExportSummaryLoadedSource("A. 수출신고", "(통화별 자동분리)", Path.GetFileName(fileName), result.Entries.Count, result.UnmatchedRowCount, result.Entries, result.UsdRawEntries));
                totalUnmatched += result.UnmatchedRowCount;
                unmatchedSample ??= result.UnmatchedSample;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(fileName)}: {ex.Message}");
            }
        }

        ReportErrors(errors);
        ReportUnmatched(totalUnmatched, unmatchedSample, "통화코드");
    }

    /// <summary>
    /// 마켓마다 컬럼 헤더가 달라 마켓 지정이 필요하지만, 마켓 수만큼 "추가→드롭다운 선택→파일선택"을
    /// 반복하면 번거로워 마켓별 "불러오기" 버튼을 한 창에 나열해 클릭 한 번으로 줄인다.
    /// </summary>
    private void AddSalesFile()
    {
        using var dialog = new SalesFileLoaderDialog(_config.SalesMarkets, _config.Markets, _settingsService, source => _sources.Add(source));
        FormManager.ShowDialogSafe(dialog, this);
    }

    private void AddRemittanceFile()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel/CSV (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv|All files (*.*)|*.*",
            Title = "송금(Payoneer 등) 내역 파일을 선택하세요 (여러 개 선택 가능)",
            Multiselect = true,
            InitialDirectory = _settingsService.GetLastFolder("ExportSummary.Remittance") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("ExportSummary.Remittance", Path.GetDirectoryName(ofd.FileNames[0])!);

        var errors = new List<string>();
        var totalUnmatched = 0;
        string? unmatchedSample = null;

        foreach (var fileName in ofd.FileNames)
        {
            try
            {
                var result = ExportSummaryLoader.LoadRemittanceFile(fileName, _config.RemittanceTrack);
                _sources.Add(new ExportSummaryLoadedSource("C. 송금", "(Description 자동분리)", Path.GetFileName(fileName), result.Entries.Count, result.UnmatchedRowCount, result.Entries));
                totalUnmatched += result.UnmatchedRowCount;
                unmatchedSample ??= result.UnmatchedSample;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(fileName)}: {ex.Message}");
            }
        }

        ReportErrors(errors);
        ReportUnmatched(totalUnmatched, unmatchedSample, "Description 문구");
    }

    private void AddManualEntry()
    {
        using var dialog = new ExportSummaryManualEntryDialog(_config.Markets);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK || dialog.Entries.Count == 0) return;

        foreach (var g in dialog.Entries.GroupBy(e => e.MarketCode))
        {
            var marketName = _config.Markets.FirstOrDefault(m => m.MarketCode == g.Key)?.MarketName ?? g.Key;
            _sources.Add(new ExportSummaryLoadedSource("수동 입력", marketName, "(수동 입력)", g.Count(), 0, g.ToList()));
        }
    }

    /// <summary>DB에 임시저장된 모든 수동 입력 항목을 마켓별로 소스에 추가한다.</summary>
    private void LoadAllDrafts()
    {
        var draftRepo = new MiniERP2.Database.ExportSummaryDraftRepository();
        var all = draftRepo.GetAll();
        if (all.Count == 0)
        {
            MessageBox.Show("임시저장된 수동 입력 항목이 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        foreach (var g in all.GroupBy(r => r.MarketCode))
        {
            var marketName = _config.Markets.FirstOrDefault(m => m.MarketCode == g.Key)?.MarketName ?? g.Key;
            var entries = g.Select(r => new ExportSummaryEntry(
                r.Indicator switch { "신고액" => "A", "매출액" => "B", _ => "C" },
                r.MarketCode, r.YearMonth, r.Amount, r.Indicator, r.Currency)).ToList();
            _sources.Add(new ExportSummaryLoadedSource("수동 입력(DB)", marketName, "(DB 임시저장)", entries.Count, 0, entries));
        }

        _statusLabel.Text = $"임시저장 항목 {all.Count}건을 소스에 추가했습니다.";
    }

    private void RemoveSelectedSource()
    {
        var selected = _sourceGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem)
            .OfType<ExportSummaryLoadedSource>()
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show("제거할 소스를 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        foreach (var source in selected) _sources.Remove(source);
    }

    /// <summary>파일명에서 패턴 매칭으로 마켓을 자동 탐지해 판매 파일을 불러온다.</summary>
    private void AddSalesFileAutoDetect()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel/CSV (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv|All files (*.*)|*.*",
            Title = "판매 파일을 선택하세요 — 파일명에서 마켓을 자동 탐지합니다 (여러 개 선택 가능)",
            Multiselect = true,
            InitialDirectory = _settingsService.GetLastFolder("ExportSummary.Sales") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("ExportSummary.Sales", Path.GetDirectoryName(ofd.FileNames[0])!);

        var errors = new List<string>();
        var undetected = new List<string>();
        var loadedCount = 0;

        foreach (var fileName in ofd.FileNames)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var mapping = _config.SalesMarkets.FirstOrDefault(m =>
                m.FileNamePatterns.Any(pat => baseName.Contains(pat, StringComparison.OrdinalIgnoreCase)));

            if (mapping is null)
            {
                undetected.Add(Path.GetFileName(fileName));
                continue;
            }

            var market = _config.Markets.FirstOrDefault(m => m.MarketCode == mapping.MarketCode);
            var marketName = market?.MarketName ?? mapping.MarketCode;

            try
            {
                var result = ExportSummaryLoader.LoadSalesFile(fileName, mapping, market?.Currency ?? "");
                _sources.Add(new ExportSummaryLoadedSource("B. 판매", marketName, Path.GetFileName(fileName), result.Entries.Count, result.UnmatchedRowCount, result.Entries));
                loadedCount++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(fileName)}: {ex.Message}");
            }
        }

        ReportErrors(errors);

        if (undetected.Count > 0)
        {
            MessageBox.Show(
                $"다음 파일은 파일명에서 마켓을 탐지하지 못했습니다. 'B. 판매(마켓선택)' 버튼으로 수동 선택하거나 설정 탭에서 FileNamePatterns를 추가하세요.\n\n{string.Join("\n", undetected)}",
                "마켓 탐지 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (loadedCount > 0)
            _statusLabel.Text = $"{loadedCount}개 파일 자동탐지 로드 완료.";
    }

    private void ReportUnmatched(int unmatchedRowCount, string? unmatchedSample, string basisLabel)
    {
        if (unmatchedRowCount <= 0) return;
        MessageBox.Show(
            $"{basisLabel} 기준으로 마켓을 특정하지 못한 행이 {unmatchedRowCount}건 있어 집계에서 제외했습니다.\n" +
            $"(예: \"{unmatchedSample}\")\n" +
            "필요하면 '수동 입력 추가'로 해당 금액을 직접 더하세요.",
            "일부 행 미매핑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ReportErrors(List<string> errors)
    {
        if (errors.Count == 0) return;
        MessageBox.Show(
            $"다음 파일을 읽는 중 오류가 발생했습니다:\n{string.Join("\n", errors)}",
            "일부 파일 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void Aggregate()
    {
        var rows = _sources.SelectMany(s => s.Entries)
            .Select(e =>
            {
                // Indicator/Currency가 없는 레거시(수동입력 구버전) 항목 보완
                var indicator = e.Indicator.Length > 0 ? e.Indicator
                    : e.Track switch { "A" => "신고액", "B" => "매출액", "C" => "실익액", _ => e.Track };
                var currency = e.Currency.Length > 0 ? e.Currency
                    : e.Track == "C" ? "USD"
                    : _config.Markets.FirstOrDefault(m => m.MarketCode == e.MarketCode)?.Currency ?? "";
                return (e.MarketCode, e.YearMonth, Indicator: indicator, Currency: currency, e.Amount);
            })
            .GroupBy(x => (x.MarketCode, x.YearMonth, x.Indicator, x.Currency))
            .OrderBy(g => g.Key.MarketCode, StringComparer.Ordinal)
            .ThenBy(g => g.Key.YearMonth, StringComparer.Ordinal)
            .ThenBy(g => IndicatorOrder(g.Key.Indicator))
            .Select(g =>
            {
                var market = _config.Markets.FirstOrDefault(m => m.MarketCode == g.Key.MarketCode);
                return new TidyRow(
                    market?.MarketName ?? g.Key.MarketCode,
                    market?.MarketGroup ?? "",
                    g.Key.YearMonth,
                    g.Key.Indicator,
                    g.Key.Currency,
                    g.Sum(x => x.Amount));
            })
            .ToList();

        _pivotGrid.DataSource = new BindingList<TidyRow>(rows);
        _statusLabel.Text = $"집계 완료 — {rows.Count}행 (마켓×연월×지표×통화).";
    }

    private static int IndicatorOrder(string indicator) => indicator switch
    {
        "신고액" => 0,
        "매출액" => 1,
        "실익액" => 2,
        _ => 9,
    };

    private void Export()
    {
        if (_pivotGrid.DataSource is not BindingList<TidyRow> rows || rows.Count == 0)
        {
            MessageBox.Show("먼저 '집계' 버튼을 눌러 표를 만드세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"수출요약보고서_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("ExportSummary.Export") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;
        _settingsService.SetLastFolder("ExportSummary.Export", Path.GetDirectoryName(filePath)!);

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();

            // tidy 원시 시트 — 마켓×연월×지표×통화 단위 flat list. 이 시트로 피벗테이블을 만든다.
            var sheet = package.Workbook.Worksheets.Add("tidy원시");
            string[] headers = ["마켓", "마켓그룹", "연월", "지표", "통화", "금액"];
            for (var i = 0; i < headers.Length; i++)
            {
                sheet.Cells[1, i + 1].Value = headers[i];
                sheet.Cells[1, i + 1].Style.Font.Bold = true;
            }

            var row = 2;
            foreach (var r in rows)
            {
                sheet.Cells[row, 1].Value = r.MarketName;
                sheet.Cells[row, 2].Value = r.MarketGroup;
                sheet.Cells[row, 3].Value = r.YearMonth;
                sheet.Cells[row, 4].Value = r.Indicator;
                sheet.Cells[row, 5].Value = r.Currency;
                sheet.Cells[row, 6].Value = (double)r.Amount;
                sheet.Cells[row, 6].Style.Numberformat.Format = "#,##0.00";
                row++;
            }

            sheet.Cells[1, 1, 1, headers.Length].AutoFitColumns(8, 30);

            // USD(미분리) 건 상세 시트 — 수출신고 파일에서 USD 행의 날짜·금액 원본 출력
            var usdRawAll = _sources
                .Where(s => s.UsdRawEntries is { Count: > 0 })
                .SelectMany(s => s.UsdRawEntries!)
                .OrderBy(e => e.DateText)
                .ToList();
            if (usdRawAll.Count > 0)
            {
                var usdSheet = package.Workbook.Worksheets.Add("USD상세");
                usdSheet.Cells[1, 1].Value = "신고일자";
                usdSheet.Cells[1, 2].Value = "통화";
                usdSheet.Cells[1, 3].Value = "신고금액";
                var usdRow = 2;
                foreach (var e in usdRawAll)
                {
                    usdSheet.Cells[usdRow, 1].Value = e.DateText;
                    usdSheet.Cells[usdRow, 2].Value = e.Currency;
                    usdSheet.Cells[usdRow, 3].Value = (double)e.Amount;
                    usdSheet.Cells[usdRow, 3].Style.Numberformat.Format = "#,##0.00";
                    usdRow++;
                }
                // 합계 행
                usdSheet.Cells[usdRow, 1].Value = "합계";
                usdSheet.Cells[usdRow, 3].Value = (double)usdRawAll.Sum(e => e.Amount);
                usdSheet.Cells[usdRow, 3].Style.Numberformat.Format = "#,##0.00";
                usdSheet.Cells[usdRow, 1, usdRow, 3].Style.Font.Bold = true;
                usdSheet.Cells[1, 1, usdRow, 3].AutoFitColumns(10, 30);
            }

            ExportHelper.SaveExcel(package, filePath);

            _statusLabel.Text = $"저장 완료: {Path.GetFileName(filePath)}";
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 내보내는 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

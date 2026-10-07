using System.ComponentModel;
using System.Text.RegularExpressions;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.DataLoaders;
using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// CSKU별 통계(CSKU별통계_개발기획서.md). 마감/이익분석이 내보낸 "분석결과상세" 엑셀 여러 개를
/// 로드해 (기간, 파일구분, 채널, CSKU) 단위로 집계한 뒤 배치로 저장하거나 엑셀로 내보낸다.
/// SettlementForm/ProfitFactTable/ClosingOrchestrator와는 완전히 독립된 기능이다(§0).
/// </summary>
public class CskuStatForm : Form
{
    private readonly CskuStatRepository _repo = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly ChannelSkuRepository _channelSkuRepo = new();
    private readonly SettingsService _settingsService = new();
    private readonly ItemRepository _itemRepo = new();
    private List<ChannelConfig> _channelConfigs = [];

    private readonly BindingList<LoadedFileRow> _loadedFiles = [];
    private readonly BindingList<CskuStatLine> _aggregatedLines = [];
    private readonly BindingList<CskuStatMskuRow> _mskuRows = [];

    /// <summary>거래처 마감에서 불러온 행이 이미 알고 있는 MSKU — (채널, CSKU) → MSKU. ChannelSkuTable 조회 실패 시 대체값.</summary>
    private readonly Dictionary<(string Channel, string Csku), string> _knownMsku = [];

    private decimal _currentExchangeRate;
    private bool _amazonRateAutoFilled;
    private long? _loadedBatchId;

    private CheckBox _amazonCheck = new();
    private CheckBox _rocketCheck = new();
    private CheckBox _includeRawCheck = new();
    private TextBox _periodBox = new();
    private TextBox _exchangeRateBox = new();
    private TextBox _memoBox = new();
    private ExcelLikeDataGridView _fileGrid = new();
    private ExcelLikeDataGridView _lineGrid = new();
    private ExcelLikeDataGridView _mskuGrid = new();
    private ComboBox _viewCombo = new();
    private Label _totalsLabel = new();
    private Label _statusLabel = new();

    /// <summary>"로드 파일 목록" 그리드 한 행. 파싱 성공/실패와 원본 행을 함께 들고 있다.</summary>
    private class LoadedFileRow
    {
        public string FilePath { get; init; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public CskuFileKind FileKind { get; init; }
        public string FileKindDisplay => FileKind.ToDisplayName();
        public List<CskuStatSourceRow> Rows { get; init; } = [];
        public List<string> Warnings { get; init; } = [];
        public bool HasError { get; init; }
        public string? ErrorMessage { get; init; }

        public int RowCount => Rows.Count;
        public int SumQty => Rows.Sum(r => r.Qty);
        public decimal SumRevenue => Rows.Sum(r => r.Revenue);
        public decimal SumProfit => Rows.Sum(r => r.Profit);
        public string StatusDisplay => HasError ? $"오류: {ErrorMessage}" : "정상";
    }

    public CskuStatForm()
    {
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
        _channelConfigs = _channelConfigService.Load();
    }

    private void InitializeComponent()
    {
        Text = "CSKU별 통계";
        Size = new Size(1200, 760);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 75));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        // ── 1행: 파일 추가 / 구분 체크박스 / 기간 / 환율 ──────────────────────
        var paramPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnAddFiles = new Button { Text = "파일 추가", Size = new Size(90, 30) };
        _amazonCheck = new CheckBox { Text = "아마존", AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        _rocketCheck = new CheckBox { Text = "로켓그로스", AutoSize = true, Margin = new Padding(10, 8, 0, 0) };
        var periodLabel = new Label { Text = "기간:", AutoSize = true, Margin = new Padding(16, 8, 0, 0) };
        _periodBox = new TextBox { Width = 90, Margin = new Padding(4, 4, 0, 0) };
        var rateLabel = new Label { Text = "환율:", AutoSize = true, Margin = new Padding(16, 8, 0, 0) };
        _exchangeRateBox = new TextBox { Width = 70, Margin = new Padding(4, 4, 0, 0), Enabled = false };
        var memoLabel = new Label { Text = "메모:", AutoSize = true, Margin = new Padding(16, 8, 0, 0) };
        _memoBox = new TextBox { Width = 200, Margin = new Padding(4, 4, 0, 0) };

        _amazonCheck.CheckedChanged += (s, e) => { if (_amazonCheck.Checked) _rocketCheck.Checked = false; };
        _rocketCheck.CheckedChanged += (s, e) => { if (_rocketCheck.Checked) _amazonCheck.Checked = false; };
        btnAddFiles.Click += (s, e) => AddFiles();
        var btnAddPartner = new Button { Text = "거래처 마감 불러오기", Size = new Size(140, 30) };
        btnAddPartner.Click += (s, e) => AddPartnerClosings();

        paramPanel.Controls.Add(btnAddFiles);
        paramPanel.Controls.Add(btnAddPartner);
        paramPanel.Controls.Add(_amazonCheck);
        paramPanel.Controls.Add(_rocketCheck);
        paramPanel.Controls.Add(periodLabel);
        paramPanel.Controls.Add(_periodBox);
        paramPanel.Controls.Add(rateLabel);
        paramPanel.Controls.Add(_exchangeRateBox);
        paramPanel.Controls.Add(memoLabel);
        paramPanel.Controls.Add(_memoBox);

        // ── 2행: 로드 파일 목록 ────────────────────────────────────────────
        _fileGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "CskuStatForm.FileGrid",
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
            MultiSelect = true,
            ReadOnly = true,
        };
        _fileGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "파일명", Name = "FileName", DataPropertyName = "FileName", Width = 260 },
            new DataGridViewTextBoxColumn { HeaderText = "구분", Name = "FileKindDisplay", DataPropertyName = "FileKindDisplay", Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "행수", Name = "RowCount", DataPropertyName = "RowCount", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "매출합", Name = "SumRevenue", DataPropertyName = "SumRevenue", Width = 110, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "상태", Name = "StatusDisplay", DataPropertyName = "StatusDisplay", Width = 240 }
        );
        _fileGrid.DataSource = _loadedFiles;

        var fileToolPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 0, 5, 0) };
        var btnRemoveFile = new Button { Text = "선택 파일 제거", Size = new Size(110, 28) };
        btnRemoveFile.Click += (s, e) => RemoveSelectedFiles();
        fileToolPanel.Controls.Add(btnRemoveFile);

        // ── 3행: 실행 버튼 ────────────────────────────────────────────────
        var actionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnAggregate = new Button { Text = "집계 실행", Size = new Size(90, 30), Font = new Font(Font, FontStyle.Bold) };
        var btnSaveBatch = new Button { Text = "배치 저장", Size = new Size(90, 30) };
        var btnLoadBatch = new Button { Text = "배치 불러오기", Size = new Size(100, 30) };
        var btnExport = new Button { Text = "엑셀 내보내기", Size = new Size(100, 30) };
        _includeRawCheck = new CheckBox { Text = "원본행 포함", AutoSize = true, Margin = new Padding(16, 8, 0, 0) };

        btnAggregate.Click += (s, e) => RunAggregate();
        btnSaveBatch.Click += (s, e) => SaveBatchToDb();
        btnLoadBatch.Click += (s, e) => LoadBatchFromDb();
        btnExport.Click += (s, e) => ExportToExcel();

        actionPanel.Controls.Add(btnAggregate);
        actionPanel.Controls.Add(btnSaveBatch);
        actionPanel.Controls.Add(btnLoadBatch);
        actionPanel.Controls.Add(btnExport);
        actionPanel.Controls.Add(_includeRawCheck);

        // 같은 선물세트라도 채널마다 CSKU가 달라 "채널·CSKU별"로는 흩어진다 — MSKU로 다시 합쳐
        // 온라인+거래처 판매를 한 줄로 보는 보기를 따로 둔다.
        _viewCombo = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(4, 5, 0, 0) };
        _viewCombo.Items.AddRange(["채널·CSKU별", "MSKU별 합산"]);
        _viewCombo.SelectedIndex = 0;
        _viewCombo.SelectedIndexChanged += (s, e) => ApplyView();
        actionPanel.Controls.Add(new Label { Text = "보기:", AutoSize = true, Margin = new Padding(20, 9, 0, 0) });
        actionPanel.Controls.Add(_viewCombo);

        // ── 4행: 총계 3종 ─────────────────────────────────────────────────
        _totalsLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "총 수량: 0    총 매출액: 0    총 이익액: 0",
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
        };

        // ── 5행: 집계 그리드 ──────────────────────────────────────────────
        _lineGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "CskuStatForm.LineGrid",
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
            MultiSelect = true,
            ReadOnly = true,
        };
        _lineGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "구분", Name = "FileKindDisplay", DataPropertyName = "FileKindDisplay", Width = 70 },
            new DataGridViewTextBoxColumn { HeaderText = "채널명", Name = "ChannelName", DataPropertyName = "ChannelName", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "CskuCode", DataPropertyName = "CskuCode", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "상품그룹", Name = "ProductGroup", DataPropertyName = "ProductGroup", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "상품명", Name = "ProductName", DataPropertyName = "ProductName", Width = 160 },
            new DataGridViewTextBoxColumn { HeaderText = "건수", Name = "RowCount", DataPropertyName = "RowCount", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "매출액", Name = "Revenue", DataPropertyName = "Revenue", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "정산액", Name = "Settlement", DataPropertyName = "Settlement", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "배송비", Name = "Shipping", DataPropertyName = "Shipping", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "입출고비", Name = "Fee", DataPropertyName = "Fee", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "이익액", Name = "Profit", DataPropertyName = "Profit", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "마진율", Name = "MarginRate", DataPropertyName = "MarginRate", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "0.0%", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        _lineGrid.DataSource = _aggregatedLines;
        _lineGrid.CellDoubleClick += OnLineGridDoubleClick;

        _mskuGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "CskuStatForm.MskuGrid",
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.RowHeaderSelect,
            MultiSelect = true,
            ReadOnly = true,
            Visible = false,
        };
        var n0 = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight };
        _mskuGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "MSKU", Name = "Msku", DataPropertyName = "Msku", Width = 140 },
            new DataGridViewTextBoxColumn { HeaderText = "상품명", Name = "ProductName", DataPropertyName = "ProductName", Width = 200 },
            new DataGridViewTextBoxColumn { HeaderText = "온라인수량", Name = "OnlineQty", DataPropertyName = "OnlineQty", Width = 75, DefaultCellStyle = n0 },
            new DataGridViewTextBoxColumn { HeaderText = "거래처수량", Name = "PartnerQty", DataPropertyName = "PartnerQty", Width = 75, DefaultCellStyle = n0 },
            new DataGridViewTextBoxColumn { HeaderText = "합계수량", Name = "Qty", DataPropertyName = "Qty", Width = 75, DefaultCellStyle = n0 },
            new DataGridViewTextBoxColumn { HeaderText = "매출액(원)", Name = "Revenue", DataPropertyName = "Revenue", Width = 105, DefaultCellStyle = n0 },
            new DataGridViewTextBoxColumn { HeaderText = "온라인이익", Name = "OnlineProfit", DataPropertyName = "OnlineProfit", Width = 95, DefaultCellStyle = n0 },
            new DataGridViewTextBoxColumn { HeaderText = "거래처이익", Name = "PartnerProfit", DataPropertyName = "PartnerProfit", Width = 95, DefaultCellStyle = n0 },
            new DataGridViewTextBoxColumn { HeaderText = "이익액(원)", Name = "Profit", DataPropertyName = "Profit", Width = 105, DefaultCellStyle = n0 },
            new DataGridViewTextBoxColumn { HeaderText = "마진율", Name = "MarginRate", DataPropertyName = "MarginRate", Width = 65, DefaultCellStyle = new DataGridViewCellStyle { Format = "0.0%", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "채널수", Name = "ChannelCount", DataPropertyName = "ChannelCount", Width = 55, DefaultCellStyle = n0 },
            new DataGridViewTextBoxColumn { HeaderText = "채널/거래처", Name = "Channels", DataPropertyName = "Channels", Width = 250 },
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "CskuCodes", DataPropertyName = "CskuCodes", Width = 200 }
        );
        _mskuGrid.DataSource = _mskuRows;
        var gridHost = new Panel { Dock = DockStyle.Fill };
        gridHost.Controls.Add(_lineGrid);
        gridHost.Controls.Add(_mskuGrid);

        // ── 6행: 상태표시줄 ───────────────────────────────────────────────
        _statusLabel = new Label { Dock = DockStyle.Fill, Text = "파일을 추가하세요.", Padding = new Padding(6, 4, 0, 0) };

        mainLayout.Controls.Add(paramPanel, 0, 0);
        mainLayout.Controls.Add(_fileGrid, 0, 1);
        mainLayout.Controls.Add(fileToolPanel, 0, 2);
        mainLayout.Controls.Add(actionPanel, 0, 3);
        mainLayout.Controls.Add(_totalsLabel, 0, 4);
        mainLayout.Controls.Add(gridHost, 0, 5);
        mainLayout.Controls.Add(_statusLabel, 0, 6);

        Controls.Add(mainLayout);
    }

    // ── 파일 추가 ────────────────────────────────────────────────────────

    private void AddFiles()
    {
        var kind = _amazonCheck.Checked ? CskuFileKind.Amazon : _rocketCheck.Checked ? CskuFileKind.RocketGross : CskuFileKind.General;

        using var ofd = new OpenFileDialog
        {
            Filter = "Excel/CSV (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv|All files (*.*)|*.*",
            Title = "마감/이익분석 결과 엑셀 파일을 선택하세요 (여러 개 선택 가능)",
            Multiselect = true,
            InitialDirectory = _settingsService.GetLastFolder("CskuStat") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("CskuStat", Path.GetDirectoryName(ofd.FileNames[0])!);

        var addedCount = 0;
        foreach (var path in ofd.FileNames)
        {
            var fileName = Path.GetFileName(path);
            using var package = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? CsvWorkbookReader.LoadAsPackage(path)
                : ExcelFileOpener.OpenWithPasswordPrompt(path, this);
            if (package == null) continue; // 암호 입력 취소

            var result = CskuStatFileParser.Parse(package, fileName, kind);
            if (!result.Success)
            {
                _loadedFiles.Add(new LoadedFileRow { FilePath = path, FileKind = kind, HasError = true, ErrorMessage = result.ErrorMessage });
                continue;
            }

            var row = new LoadedFileRow { FilePath = path, FileKind = kind, Rows = result.Rows, Warnings = result.Warnings };
            WarnIfDuplicate(row);
            _loadedFiles.Add(row);
            addedCount++;

            if (kind == CskuFileKind.Amazon && !_amazonRateAutoFilled)
            {
                PrefillExchangeRate(row);
                _amazonRateAutoFilled = true;
            }
        }

        _exchangeRateBox.Enabled = _loadedFiles.Any(f => f.FileKind == CskuFileKind.Amazon);
        _statusLabel.Text = $"파일 {addedCount}개 추가됨.";
    }

    /// <summary>§7 — FileName/RowCount/SumQty/SumRevenue/SumProfit 5개가 모두 일치하면 안내만 하고 그대로 추가한다.</summary>
    private void WarnIfDuplicate(LoadedFileRow row)
    {
        var dupInList = _loadedFiles.Any(f => !f.HasError && f.FileName == row.FileName &&
            f.RowCount == row.RowCount && f.SumQty == row.SumQty && f.SumRevenue == row.SumRevenue && f.SumProfit == row.SumProfit);
        if (dupInList)
        {
            MessageBox.Show(this, $"'{row.FileName}'은 현재 로드 목록에 이미 추가된 파일과 동일합니다.", "중복 파일 안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var dupInDb = _repo.FindDuplicateFile(row.FileName, row.RowCount, row.SumQty, row.SumRevenue, row.SumProfit);
        if (dupInDb != null)
        {
            var b = dupInDb.Value.Batch;
            MessageBox.Show(this,
                $"'{row.FileName}'은 이미 저장된 배치와 동일한 파일입니다.\n배치 #{b.Id} ({b.Period}, {b.Memo}, {b.CreatedAt:yyyy-MM-dd HH:mm})와 겹칩니다.",
                "중복 파일 안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>§3 — 배치 내 첫 아마존 파일의 첫 ChannelCode가 ChannelConfig에 등록돼 있으면 그 ExchangeRate를 프리필.</summary>
    private void PrefillExchangeRate(LoadedFileRow row)
    {
        var firstChannel = row.Rows.FirstOrDefault()?.ChannelCode;
        if (firstChannel == null) return;

        var cfg = _channelConfigs.FirstOrDefault(c => c.ChannelCode == firstChannel);
        if (cfg != null) _exchangeRateBox.Text = cfg.ExchangeRate.ToString();
    }

    private void RemoveSelectedFiles()
    {
        var selected = _fileGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as LoadedFileRow)
            .Where(f => f != null)
            .ToList();
        foreach (var f in selected) _loadedFiles.Remove(f!);
        _exchangeRateBox.Enabled = _loadedFiles.Any(f => f.FileKind == CskuFileKind.Amazon);
    }

    // ── 집계 실행 ────────────────────────────────────────────────────────

    private void RunAggregate()
    {
        if (string.IsNullOrWhiteSpace(_periodBox.Text))
        {
            MessageBox.Show(this, "기간을 입력하세요.", "입력 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var validFiles = _loadedFiles.Where(f => !f.HasError).ToList();
        var hasAmazon = validFiles.Any(f => f.FileKind == CskuFileKind.Amazon);
        decimal exchangeRate = 0;
        if (hasAmazon && (!decimal.TryParse(_exchangeRateBox.Text, out exchangeRate) || exchangeRate <= 0))
        {
            MessageBox.Show(this, "아마존 파일이 있으면 환산환율을 입력해야 합니다.", "입력 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var allRows = validFiles.SelectMany(f => f.Rows).ToList();
        var lines = CskuStatAggregator.Aggregate(allRows, ResolveChannelName);
        foreach (var row in allRows.Where(r => r.Msku.Length > 0)) _knownMsku[(row.ChannelCode, row.CskuCode)] = row.Msku;
        foreach (var l in lines) FillChannelSkuInfo(l);

        _aggregatedLines.Clear();
        foreach (var l in lines) _aggregatedLines.Add(l);
        _currentExchangeRate = exchangeRate;
        _loadedBatchId = null;

        UpdateTotalsLabel();
        RefreshMskuRows();

        var warnCount = validFiles.Sum(f => f.Warnings.Count);
        var excludedCount = allRows.Count(r => r.RowClass == CskuStatRowClass.Excluded);
        var unmappedCount = allRows.Count(r => r.RowClass == CskuStatRowClass.Unmapped);
        _statusLabel.Text = $"집계 완료 — 예외 {excludedCount}건 / 미매핑 {unmappedCount}건" + (warnCount > 0 ? $" (수치 파싱 경고 {warnCount}건)" : string.Empty);
    }

    private string ResolveChannelName(string channelCode)
    {
        var cfg = _channelConfigs.FirstOrDefault(c => c.ChannelCode == channelCode);
        return !string.IsNullOrWhiteSpace(cfg?.ChannelName) ? cfg.ChannelName : channelCode;
    }

    /// <summary>ChannelSkuTable에서 MSKU/송장출력용 상품명을 조회해 채운다. 미등록이면 빈 값.</summary>
    private void FillChannelSkuInfo(CskuStatLine line)
    {
        var csku = _channelSkuRepo.GetByChannelAndCskuCode(line.ChannelCode, line.CskuCode);
        line.Msku = csku?.Msku ?? string.Empty;
        // 미등록 CSKU(주로 수동 거래처 라인 — 채널이 없어 ChannelSkuTable에 없음)는 원본이 알려준 MSKU,
        // 그것도 없으면 CSKU 자체가 마스터 품목 코드인지 확인해 대체한다.
        if (line.Msku.Length == 0)
        {
            if (_knownMsku.TryGetValue((line.ChannelCode, line.CskuCode), out var known)) line.Msku = known;
            else if (line.FileKind == CskuFileKind.Partner && _itemRepo.GetBySku(line.CskuCode) != null) line.Msku = line.CskuCode;
        }
        line.InvoiceDisplayName = csku?.InvoiceDisplayName ?? string.Empty;
    }

    /// <summary>총계 3종은 그리드 필터·정렬과 무관하게 전체 기준(§6). 아마존이 섞이면 환산 원화 합계.</summary>
    private void UpdateTotalsLabel()
    {
        var hasAmazon = _aggregatedLines.Any(l => l.FileKind == CskuFileKind.Amazon);
        decimal ToKrw(CskuStatLine l, decimal value) => l.FileKind == CskuFileKind.Amazon ? value * _currentExchangeRate : value;

        var totalQty = _aggregatedLines.Sum(l => l.Qty);
        var totalRevenue = _aggregatedLines.Sum(l => ToKrw(l, l.Revenue));
        var totalProfit = _aggregatedLines.Sum(l => ToKrw(l, l.Profit));
        var suffix = hasAmazon ? $" (환율 {_currentExchangeRate:0.####} 적용)" : string.Empty;

        _totalsLabel.Text = $"총 수량: {totalQty:N0}    총 매출액: {totalRevenue:N0}{suffix}    총 이익액: {totalProfit:N0}{suffix}";
    }

    // ── 배치 저장/불러오기 ───────────────────────────────────────────────

    private void SaveBatchToDb()
    {
        if (_aggregatedLines.Count == 0)
        {
            MessageBox.Show(this, "먼저 집계를 실행하세요.", "집계 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var batch = new CskuStatBatch { Period = _periodBox.Text.Trim(), Memo = _memoBox.Text.Trim(), ExchangeRate = _currentExchangeRate };
        var files = _loadedFiles.Where(f => !f.HasError)
            .Select(f => new CskuStatFile { FileName = f.FileName, FileKind = f.FileKind, RowCount = f.RowCount, SumQty = f.SumQty, SumRevenue = f.SumRevenue, SumProfit = f.SumProfit })
            .ToList();

        var batchId = _repo.SaveBatch(batch, [.. _aggregatedLines], files);
        _loadedBatchId = batchId;
        _statusLabel.Text = $"배치 #{batchId} 저장 완료.";
    }

    private void LoadBatchFromDb()
    {
        using var picker = new CskuStatBatchPickerDialog(_repo);
        if (FormManager.ShowDialogSafe(picker, this) != DialogResult.OK || picker.SelectedBatchId is not { } batchId) return;

        var batch = _repo.GetBatch(batchId);
        if (batch == null) return;

        _aggregatedLines.Clear();
        foreach (var l in _repo.GetLines(batchId))
        {
            FillChannelSkuInfo(l);
            _aggregatedLines.Add(l);
        }
        RefreshMskuRows();

        _loadedFiles.Clear();
        foreach (var f in _repo.GetFiles(batchId))
        {
            _loadedFiles.Add(new LoadedFileRow { FilePath = f.FileName, FileKind = f.FileKind, Rows = [] });
        }

        _periodBox.Text = batch.Period;
        _memoBox.Text = batch.Memo;
        _exchangeRateBox.Text = batch.ExchangeRate.ToString();
        _exchangeRateBox.Enabled = batch.ExchangeRate > 0;
        _currentExchangeRate = batch.ExchangeRate;
        _loadedBatchId = batchId;
        _amazonRateAutoFilled = true;

        UpdateTotalsLabel();
        _statusLabel.Text = $"배치 #{batchId} ({batch.Period}) 불러옴 — 저장된 집계 결과(스냅샷)입니다. 원본 행이 없어 엑셀 내보내기의 예외·미매핑/원본행 시트는 비어 있습니다.";
    }

    // ── 거래처 마감 불러오기 / MSKU별 보기 ───────────────────────────────

    /// <summary>
    /// 거래처 마감보드 라인을 DB에서 바로 불러와 거래처 1곳 = 로드 목록 1행으로 추가한다(구분 "거래처").
    /// 매출장 파일 대신 DB를 쓰므로 CSKU·원가가 이미 붙어 있다. 같은 거래처를 다시 불러오면 교체한다.
    /// </summary>
    private void AddPartnerClosings()
    {
        var initialPeriod = Regex.IsMatch(_periodBox.Text.Trim(), @"^\d{4}-\d{2}$")
            ? _periodBox.Text.Trim()
            : DateTime.Today.AddMonths(-1).ToString("yyyy-MM");
        using var picker = new PartnerClosingPickerDialog(initialPeriod);
        if (FormManager.ShowDialogSafe(picker, this) != DialogResult.OK) return;

        var added = 0;
        foreach (var summary in picker.SelectedSummaries)
        {
            var name = $"[거래처마감 {picker.Period}] {summary.PartyName}";
            foreach (var old in _loadedFiles.Where(f => f.FilePath == name).ToList()) _loadedFiles.Remove(old);
            _loadedFiles.Add(new LoadedFileRow
            {
                FilePath = name,
                FileKind = CskuFileKind.Partner,
                Rows = PartnerCskuStatSource.ToSourceRows(summary, name),
            });
            added++;
        }

        if (string.IsNullOrWhiteSpace(_periodBox.Text)) _periodBox.Text = picker.Period;
        _statusLabel.Text = $"거래처 마감 {added}곳 추가됨 — [집계 실행]을 누르세요. (택배비·할인 라인은 예외로 빠지고, 거래처별 배부운임은 반영되지 않습니다)";
    }

    private void RefreshMskuRows()
    {
        _mskuRows.Clear();
        foreach (var r in CskuStatMskuSummarizer.Summarize(_aggregatedLines, _currentExchangeRate)) _mskuRows.Add(r);
    }

    private void ApplyView()
    {
        var mskuView = _viewCombo.SelectedIndex == 1;
        _mskuGrid.Visible = mskuView;
        _lineGrid.Visible = !mskuView;
    }

    // ── 엑셀 내보내기 ────────────────────────────────────────────────────

    private void ExportToExcel()
    {
        if (_aggregatedLines.Count == 0)
        {
            MessageBox.Show(this, "먼저 집계를 실행하세요.", "집계 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var period = _periodBox.Text.Trim();
        var defaultName = $"CSKU별통계_{period}_{DateTime.Now:yyyyMMdd}.xlsx";
        var initialDir = _settingsService.GetLastFolder("CskuStat.Export") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel (*.xlsx)|*.xlsx", defaultName, initialDir, "CSKU별 통계 내보내기");
        if (filePath == null) return;
        _settingsService.SetLastFolder("CskuStat.Export", Path.GetDirectoryName(filePath)!);

        var batch = new CskuStatBatch
        {
            Id = _loadedBatchId ?? 0,
            Period = period,
            Memo = _memoBox.Text.Trim(),
            ExchangeRate = _currentExchangeRate,
            CreatedAt = DateTime.Now,
        };
        var sourceRows = _loadedFiles.Where(f => !f.HasError).SelectMany(f => f.Rows).ToList();
        var files = _loadedFiles.Where(f => !f.HasError)
            .Select(f => new CskuStatFile { FileName = f.FileName, FileKind = f.FileKind, RowCount = f.RowCount, SumQty = f.SumQty, SumRevenue = f.SumRevenue, SumProfit = f.SumProfit })
            .ToList();

        try
        {
            CskuStatExporter.Export(batch, [.. _aggregatedLines], sourceRows, files, filePath, _includeRawCheck.Checked, ResolveChannelName);
            _statusLabel.Text = $"저장 완료: {Path.GetFileName(filePath)}";
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"파일을 내보내는 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── 상세조회 ─────────────────────────────────────────────────────────

    private void OnLineGridDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_lineGrid.Rows[e.RowIndex].DataBoundItem is not CskuStatLine line) return;

        using var dialog = new CskuDetailDialog(line.ChannelCode, line.CskuCode);
        FormManager.ShowDialogSafe(dialog, this);
    }
}

using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 이공그로스수동마감(ManualGrowthClosing_Spec.md). 로켓그로스(제트) 거래를 수동 엑셀 거래명세표로
/// 집계한 파일을 읽어 품목별 CSKU·원가·이익 마감을 한다. 마감/이익분석(SettlementForm)과는 별도 창이며,
/// 금액 기준은 그쪽과 같은 VAT포함(매출 = 공급가액 + 세액)이다. 상품명 = CSKU코드로 보고, 처음 보는
/// 상품명은 [미배정 일괄 배정]에서 MSKU를 정해 CSKU를 만든다. 시트는 '제트'/'그로스' + 앞 4자리 YYMM만
/// 대상으로 여러 장을 골라 한 번에 불러올 수 있고, 마감월(YYMM)마다 마감 1건으로 저장된다.
/// </summary>
public class ManualGrowthClosingForm : Form
{
    /// <summary>사용자 확정 채널명(스펙 Q1). 없으면 창을 열 때 생성을 제안한다.</summary>
    public const string DefaultChannelName = "이공그로스수동마감";

    private const string OpenFileFolderKey = "ManualGrowthClosing.OpenFile";
    private const string ExportFolderKey = "ManualGrowthClosing.Export";
    // SettingsService가 문자열 키-값 저장소라 마지막 선택 채널코드도 같은 저장소에 둔다(폴더 경로는 아님).
    private const string LastChannelKey = "ManualGrowthClosing.LastChannelCode";

    private readonly SalesChannelRepository _channelRepo = new();
    private readonly ChannelSkuRepository _channelSkuRepo = new();
    private readonly ItemRepository _itemRepo = new();
    private readonly ManualGrowthClosingRepository _closingRepo = new();
    private readonly ProfitFactRepository _profitFactRepo = new();
    private readonly SettingsService _settings = new();

    private ComboBox _channelCombo = new();
    private Label _fileLabel = new();
    private CheckedListBox _sheetList = new();
    private Label _warningLabel = new();
    private Label _statusLabel = new();
    private TabControl _tabs = new();
    private ExcelLikeDataGridView _lineGrid = new();
    private ExcelLikeDataGridView _summaryGrid = new();
    private ExcelLikeDataGridView _historyGrid = new();
    private Button _assignBtn = new();
    private Button _recalcBtn = new();
    private Button _confirmBtn = new();
    private Button _reportBtn = new();
    private Button _exportBtn = new();

    private string? _filePath;
    private List<SheetItem> _sheetItems = [];
    private List<ManualGrowthSheetResult> _sheetResults = [];
    private List<ManualGrowthClosing> _closings = [];
    /// <summary>이력 탭에서 불러온 확정 스냅샷을 보고 있는지 — 이때는 현재 CSKU로 다시 해석하지 않는다.</summary>
    private bool _viewingHistory;

    private sealed record SheetItem(string SheetName, string Period)
    {
        public override string ToString() => $"{SheetName}  ({Period})";
    }

    public ManualGrowthClosingForm()
    {
        InitializeComponent();
        FormManager.ApplyBoundsTracking(this);
        Load += (s, e) => OnFormLoad();
    }

    private string CurrentChannelCode => (_channelCombo.SelectedItem as SalesChannel)?.ChannelCode ?? "";
    private string CurrentChannelName => (_channelCombo.SelectedItem as SalesChannel)?.ChannelName ?? "";
    private IEnumerable<ManualGrowthLine> AllLines => _closings.SelectMany(c => c.Lines);

    // ─── 레이아웃 ─────────────────────────────────────────────────────────

    private void InitializeComponent()
    {
        Text = "이공그로스수동마감";
        Size = new Size(1300, 820);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1 };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        main.Controls.Add(BuildToolbar(), 0, 0);
        _warningLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), ForeColor = Color.DarkOrange };
        main.Controls.Add(_warningLabel, 0, 1);
        main.Controls.Add(BuildTabs(), 0, 2);
        main.Controls.Add(BuildFooter(), 0, 3);
        _statusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), Text = "파일을 열어 대상 시트를 고른 뒤 [불러오기]를 누르세요." };
        main.Controls.Add(_statusLabel, 0, 4);

        Controls.Add(main);
    }

    private Control BuildToolbar()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(4) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var row1 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        row1.Controls.Add(new Label { Text = "채널:", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
        _channelCombo = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(SalesChannel.ChannelName) };
        _channelCombo.SelectedIndexChanged += (s, e) => OnChannelChanged();
        row1.Controls.Add(_channelCombo);
        var createChannelBtn = new Button { Text = $"'{DefaultChannelName}' 채널 등록", AutoSize = true };
        createChannelBtn.Click += (s, e) => EnsureDefaultChannel(askFirst: false);
        row1.Controls.Add(createChannelBtn);
        left.Controls.Add(row1, 0, 0);

        var row2 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var openBtn = new Button { Text = "파일 열기", Width = 80 };
        openBtn.Click += OnOpenFileClick;
        var loadBtn = new Button { Text = "불러오기", Width = 80, Font = new Font(Font, FontStyle.Bold) };
        loadBtn.Click += (s, e) => LoadCheckedSheets();
        _fileLabel = new Label { AutoSize = true, Text = "(파일 없음)", Margin = new Padding(6, 8, 0, 0) };
        row2.Controls.AddRange([openBtn, loadBtn, _fileLabel]);
        left.Controls.Add(row2, 0, 1);

        panel.Controls.Add(left, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        right.Controls.Add(new Label
        {
            Text = "대상 시트\n(여러 개 선택 시\n월별로 함께 불러옴)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        _sheetList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, MultiColumn = true, ColumnWidth = 190, IntegralHeight = false };
        right.Controls.Add(_sheetList, 1, 0);
        panel.Controls.Add(right, 1, 0);

        return panel;
    }

    private Control BuildTabs()
    {
        _tabs = new TabControl { Dock = DockStyle.Fill };

        _lineGrid = NewGrid("ManualGrowthClosingForm.LineGrid");
        _lineGrid.Columns.AddRange(
            TextCol("Period", "마감월", 65),
            TextCol("LineDate", "날짜", 85),
            TextCol("CskuCode", "상품명(=CSKU)", 220),
            TextCol("MasterSku", "MSKU", 120),
            TextCol("ProductGroup", "품목그룹", 90),
            NumCol("Qty", "수량", 60),
            NumCol("UnitPrice", "단가", 75),
            NumCol("SupplyAmount", "공급가", 95),
            NumCol("Tax", "세액", 80),
            NumCol("Revenue", "매출(VAT포함)", 100),
            NumCol("CostPrice", "원가단가", 75),
            NumCol("Cost", "원가", 95),
            NumCol("Profit", "이익", 95),
            TextCol("MarginRateText", "이익률", 60, right: true),
            TextCol("StatusText", "상태", 110)
        );
        _lineGrid.CellFormatting += OnLineGridCellFormatting;
        var lineMenu = new ContextMenuStrip();
        lineMenu.Items.Add("CSKU 상세", null, (s, e) => OpenCskuDetail(SelectedLine()?.CskuCode));
        lineMenu.Items.Add("이 상품명 MSKU 배정/변경", null, (s, e) => ReassignMsku(SelectedLine()));
        _lineGrid.ContextMenuStrip = lineMenu;

        _summaryGrid = NewGrid("ManualGrowthClosingForm.SummaryGrid");
        _summaryGrid.Columns.AddRange(
            TextCol("Period", "마감월", 65),
            TextCol("CskuCode", "CSKU(상품명)", 240),
            TextCol("MasterSku", "MSKU", 120),
            TextCol("ProductGroup", "품목그룹", 100),
            NumCol("Qty", "수량", 70),
            NumCol("SupplyAmount", "공급가", 110),
            NumCol("Revenue", "매출(VAT포함)", 110),
            NumCol("Cost", "원가", 110),
            NumCol("Profit", "이익", 110),
            TextCol("MarginRateText", "이익률", 65, right: true)
        );
        var summaryMenu = new ContextMenuStrip();
        summaryMenu.Items.Add("CSKU 상세", null, (s, e) =>
            OpenCskuDetail((_summaryGrid.CurrentRow?.DataBoundItem as ManualGrowthCskuSummary)?.CskuCode));
        _summaryGrid.ContextMenuStrip = summaryMenu;

        _historyGrid = NewGrid("ManualGrowthClosingForm.HistoryGrid");
        _historyGrid.Columns.AddRange(
            TextCol("Period", "마감월", 70),
            TextCol("Status", "상태", 60),
            TextCol("SourceSheetName", "시트", 160),
            NumCol("TotalQty", "수량", 70),
            NumCol("TotalSupply", "공급가", 110),
            NumCol("TotalRevenue", "매출(VAT포함)", 110),
            NumCol("TotalCost", "원가", 110),
            NumCol("TotalProfit", "이익", 110),
            TextCol("MarginRateText", "이익률", 65, right: true),
            TextCol("ConfirmedAt", "확정일시", 130),
            TextCol("SourceFileName", "파일", 200)
        );
        _historyGrid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) LoadFromHistory(); };

        var historyPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var historyButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var loadHistoryBtn = new Button { Text = "선택 마감 불러오기", AutoSize = true };
        loadHistoryBtn.Click += (s, e) => LoadFromHistory();
        var unconfirmBtn = new Button { Text = "확정해제", AutoSize = true };
        unconfirmBtn.Click += (s, e) => OnUnconfirmClick();
        var deleteBtn = new Button { Text = "삭제", AutoSize = true };
        deleteBtn.Click += (s, e) => OnDeleteHistoryClick();
        var sendToBoardBtn = new Button { Text = "마감보드 전송", AutoSize = true };
        sendToBoardBtn.Click += (s, e) => OnSendHistoryToBoardClick();
        historyButtons.Controls.AddRange([loadHistoryBtn, unconfirmBtn, deleteBtn, sendToBoardBtn]);
        historyPanel.Controls.Add(historyButtons, 0, 0);
        historyPanel.Controls.Add(_historyGrid, 0, 1);

        _tabs.TabPages.Add(TabWith("라인", _lineGrid));
        _tabs.TabPages.Add(TabWith("CSKU 요약", _summaryGrid));
        _tabs.TabPages.Add(TabWith("마감 이력", historyPanel));
        return _tabs;
    }

    private static TabPage TabWith(string text, Control content)
    {
        var page = new TabPage(text);
        page.Controls.Add(content);
        return page;
    }

    private static ExcelLikeDataGridView NewGrid(string key) => new()
    {
        Dock = DockStyle.Fill,
        PersistenceKey = key,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        ReadOnly = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true,
    };

    private static DataGridViewTextBoxColumn TextCol(string prop, string header, int width, bool right = false) => new()
    {
        Name = prop, DataPropertyName = prop, HeaderText = header, Width = width,
        DefaultCellStyle = right ? new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } : new DataGridViewCellStyle(),
    };

    private static DataGridViewTextBoxColumn NumCol(string prop, string header, int width) => new()
    {
        Name = prop, DataPropertyName = prop, HeaderText = header, Width = width,
        DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight },
    };

    private Control BuildFooter()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4, 4, 4, 0) };
        _assignBtn = new Button { Text = "미배정 일괄 배정", AutoSize = true };
        _assignBtn.Click += (s, e) => OnAssignClick();
        _recalcBtn = new Button { Text = "CSKU/원가 다시 해석", AutoSize = true };
        _recalcBtn.Click += (s, e) => { ResolveAndRefresh(); _statusLabel.Text = "현재 CSKU/원가로 다시 계산했습니다."; };
        _confirmBtn = new Button { Text = "마감 확정", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        _confirmBtn.Click += (s, e) => OnConfirmClick();
        _reportBtn = new Button { Text = "리포트 반영", AutoSize = true };
        _reportBtn.Click += (s, e) => OnReportClick();
        _exportBtn = new Button { Text = "엑셀 내보내기", AutoSize = true };
        _exportBtn.Click += (s, e) => OnExportClick();
        var closeBtn = new Button { Text = "닫기", AutoSize = true };
        closeBtn.Click += (s, e) => Close();
        panel.Controls.AddRange([_assignBtn, _recalcBtn, _confirmBtn, _reportBtn, _exportBtn, closeBtn]);
        UpdateButtons();
        return panel;
    }

    // ─── 채널 ─────────────────────────────────────────────────────────────

    private void OnFormLoad()
    {
        LoadChannels();
        if (_channelCombo.Items.Cast<SalesChannel>().All(c => c.ChannelName != DefaultChannelName))
            EnsureDefaultChannel(askFirst: true);
    }

    private void LoadChannels(string? selectCode = null)
    {
        var channels = _channelRepo.GetAll();
        _channelCombo.Items.Clear();
        foreach (var c in channels) _channelCombo.Items.Add(c);

        selectCode ??= _settings.GetLastFolder(LastChannelKey);
        var target = channels.FirstOrDefault(c => c.ChannelCode == selectCode)
                     ?? channels.FirstOrDefault(c => c.ChannelName == DefaultChannelName);
        if (target != null) _channelCombo.SelectedItem = target;
    }

    private void EnsureDefaultChannel(bool askFirst)
    {
        var channels = _channelRepo.GetAll();
        var existing = channels.FirstOrDefault(c => c.ChannelName == DefaultChannelName);
        if (existing != null)
        {
            LoadChannels(existing.ChannelCode);
            _statusLabel.Text = $"'{DefaultChannelName}' 채널이 이미 있습니다({existing.ChannelCode}).";
            return;
        }

        if (askFirst && MessageBox.Show(
                $"'{DefaultChannelName}' 채널이 아직 없습니다. 지금 등록할까요?\n\n" +
                "상품명 CSKU는 이 채널 아래에 만들어지므로, 기존 쿠팡그로스 CSKU와 섞이지 않습니다.",
                "채널 등록", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var code = ChannelCodeGenerator.GenerateNext(channels.Select(c => c.ChannelCode));
        _channelRepo.Upsert(new SalesChannel { ChannelCode = code, ChannelName = DefaultChannelName });
        LoadChannels(code);
        _statusLabel.Text = $"'{DefaultChannelName}' 채널을 등록했습니다({code}).";
    }

    private void OnChannelChanged()
    {
        if (CurrentChannelCode.Length > 0) _settings.SetLastFolder(LastChannelKey, CurrentChannelCode);
        RefreshHistory();
        if (_closings.Count > 0 && !_viewingHistory) ResolveAndRefresh();
    }

    // ─── 파일/시트 ────────────────────────────────────────────────────────

    private void OnOpenFileClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title = "거래명세표 엑셀 파일을 선택하세요",
            InitialDirectory = _settings.GetLastFolder(OpenFileFolderKey) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _settings.SetLastFolder(OpenFileFolderKey, Path.GetDirectoryName(ofd.FileName)!);

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage(new FileInfo(ofd.FileName));
            var targets = ManualGrowthClosingEngine.FindTargetSheets(package.Workbook.Worksheets.Select(w => w.Name));
            _filePath = ofd.FileName;
            _fileLabel.Text = Path.GetFileName(ofd.FileName);
            _sheetItems = targets.Select(t => new SheetItem(t.SheetName, t.Period)).ToList();

            _sheetList.Items.Clear();
            var latest = _sheetItems.FirstOrDefault()?.Period;
            foreach (var item in _sheetItems)
                _sheetList.Items.Add(item, item.Period == latest);

            _statusLabel.Text = _sheetItems.Count == 0
                ? "이 파일에는 '제트'/'그로스' + YYMM 형식의 시트가 없습니다."
                : $"대상 시트 {_sheetItems.Count}개 — 최신 월({latest})을 기본 선택했습니다. 필요한 시트를 체크 후 [불러오기].";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 열 수 없습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadCheckedSheets()
    {
        if (_filePath == null) { _statusLabel.Text = "먼저 파일을 여세요."; return; }
        var picked = _sheetList.CheckedItems.Cast<SheetItem>().ToList();
        if (picked.Count == 0) { _statusLabel.Text = "불러올 시트를 하나 이상 체크하세요."; return; }

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage(new FileInfo(_filePath));
            var fileName = Path.GetFileName(_filePath);
            _sheetResults = picked
                .Select(p => ManualGrowthClosingEngine.ParseSheet(package.Workbook.Worksheets[p.SheetName], fileName, p.Period))
                .ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"시트를 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _viewingHistory = false;
        _closings = ManualGrowthClosingEngine.BuildClosings(CurrentChannelCode, _sheetResults);
        ResolveAndRefresh();
        _tabs.SelectedIndex = 0;
        _statusLabel.Text = $"{picked.Count}개 시트 → 마감월 {_closings.Count}개({string.Join(", ", _closings.Select(c => c.Period))}), 라인 {AllLines.Count()}건 불러옴.";
    }

    // ─── 계산/표시 ────────────────────────────────────────────────────────

    private void ResolveAndRefresh()
    {
        if (!_viewingHistory && CurrentChannelCode.Length > 0)
        {
            var cskus = _channelSkuRepo.GetAllByChannel(CurrentChannelCode)
                .GroupBy(c => c.CskuCode).ToDictionary(g => g.Key, g => g.First());
            var items = _itemRepo.GetAll().GroupBy(i => i.Sku).ToDictionary(g => g.Key, g => g.First());
            ManualGrowthClosingEngine.Resolve(AllLines, c => cskus.GetValueOrDefault(c), s => items.GetValueOrDefault(s));
            foreach (var closing in _closings)
            {
                closing.ChannelCode = CurrentChannelCode;
                ManualGrowthClosingEngine.RecalculateTotals(closing);
            }
        }
        RefreshGrids();
    }

    private void RefreshGrids()
    {
        var lines = AllLines.ToList();
        _lineGrid.DataSource = new BindingList<ManualGrowthLine>(lines);
        _summaryGrid.DataSource = new BindingList<ManualGrowthCskuSummary>(ManualGrowthClosingEngine.BuildCskuSummary(lines));
        UpdateWarnings();
        UpdateButtons();
    }

    private void UpdateWarnings()
    {
        var lines = AllLines.ToList();
        if (lines.Count == 0) { _warningLabel.Text = ""; return; }

        var parts = new List<string>();
        if (_viewingHistory) parts.Add("[확정 스냅샷 보기]");
        var mismatchSheets = _sheetResults.Where(s => !s.TotalsReconciled).Select(s => s.SheetName).ToList();
        if (!_viewingHistory && mismatchSheets.Count > 0) parts.Add($"총계불일치/총계행없음: {string.Join(", ", mismatchSheets)}");
        int excluded = _viewingHistory ? 0 : _sheetResults.Sum(s => s.ExcludedDividerCount);
        if (excluded > 0) parts.Add($"구분행 제외 {excluded}건");
        int unassigned = lines.Count(l => l.IsUnassigned);
        parts.Add($"미배정 {unassigned}건({lines.Where(l => l.IsUnassigned).Select(l => l.CskuCode).Distinct().Count()}종)");
        int priceMismatch = lines.Count(l => l.PriceMismatch);
        if (priceMismatch > 0) parts.Add($"단가불일치 {priceMismatch}건");
        int dateMismatch = lines.Count(l => l.DateMismatch);
        if (dateMismatch > 0) parts.Add($"마감월과 날짜 월 불일치 {dateMismatch}건");

        decimal revenue = lines.Sum(l => l.Revenue), profit = lines.Sum(l => l.Profit);
        parts.Add($"합계: 수량 {lines.Sum(l => l.Qty):N0} / 공급가 {lines.Sum(l => l.SupplyAmount):N0} / 매출 {revenue:N0} / 이익 {profit:N0}" +
                  (revenue == 0 ? "" : $" ({profit / revenue:0.0%})"));
        _warningLabel.Text = string.Join("  |  ", parts);
        _warningLabel.ForeColor = unassigned > 0 || mismatchSheets.Count > 0 ? Color.OrangeRed : Color.DarkOrange;
    }

    private void UpdateButtons()
    {
        bool hasData = _closings.Count > 0;
        _assignBtn.Enabled = hasData && !_viewingHistory;
        _recalcBtn.Enabled = hasData && !_viewingHistory;
        _confirmBtn.Enabled = hasData && !_viewingHistory;
        _reportBtn.Enabled = hasData;
        _exportBtn.Enabled = hasData;
    }

    private void OnLineGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _lineGrid.Rows[e.RowIndex].DataBoundItem is not ManualGrowthLine line) return;
        var style = _lineGrid.Rows[e.RowIndex].DefaultCellStyle;
        if (line.IsUnassigned) { style.BackColor = Color.FromArgb(255, 210, 210); style.ForeColor = Color.Black; }
        else if (line.PriceMismatch || line.DateMismatch) { style.BackColor = Color.FromArgb(255, 240, 190); style.ForeColor = Color.Black; }
        else { style.BackColor = _lineGrid.DefaultCellStyle.BackColor; style.ForeColor = _lineGrid.DefaultCellStyle.ForeColor; }
    }

    private ManualGrowthLine? SelectedLine() => _lineGrid.CurrentRow?.DataBoundItem as ManualGrowthLine;

    // ─── CSKU 배정 ────────────────────────────────────────────────────────

    private bool RequireChannel()
    {
        if (CurrentChannelCode.Length > 0) return true;
        MessageBox.Show("채널을 먼저 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void OnAssignClick()
    {
        if (!RequireChannel()) return;
        var candidates = AllLines
            .Where(l => l.IsUnassigned && l.CskuCode.Length > 0)
            .GroupBy(l => l.CskuCode)
            .Select(g => new ManualGrowthAssignDialog.Candidate
            {
                CskuCode = g.Key,
                Qty = g.Sum(l => l.Qty),
                UnitPriceVatIncluded = ManualGrowthClosingEngine.ToVatIncludedUnitPrice(g.First()),
            })
            .ToList();
        if (candidates.Count == 0) { _statusLabel.Text = "미배정 상품명이 없습니다."; return; }

        using var dialog = new ManualGrowthAssignDialog(CurrentChannelCode, CurrentChannelName, candidates);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) return;
        ResolveAndRefresh();
        _statusLabel.Text = $"CSKU {dialog.CreatedCount}건을 배정했습니다.";
    }

    private void ReassignMsku(ManualGrowthLine? line)
    {
        if (line == null || _viewingHistory || !RequireChannel()) return;
        using var picker = new MasterSkuPickerDialog(line.CskuCode);
        if (FormManager.ShowDialogSafe(picker, this) != DialogResult.OK || string.IsNullOrEmpty(picker.SelectedSku)) return;

        var existing = _channelSkuRepo.GetByChannelAndCskuCode(CurrentChannelCode, line.CskuCode);
        if (existing == null)
        {
            _channelSkuRepo.CreateIfNew(CurrentChannelCode, line.CskuCode, picker.SelectedSku,
                ManualGrowthClosingEngine.ToVatIncludedUnitPrice(line), line.CskuCode);
        }
        else
        {
            existing.Msku = picker.SelectedSku;
            _channelSkuRepo.Upsert(existing);
        }
        ResolveAndRefresh();
        _statusLabel.Text = $"'{line.CskuCode}' → {picker.SelectedSku} 배정했습니다.";
    }

    private void OpenCskuDetail(string? cskuCode)
    {
        if (string.IsNullOrEmpty(cskuCode) || !RequireChannel()) return;
        if (_channelSkuRepo.GetByChannelAndCskuCode(CurrentChannelCode, cskuCode) == null)
        {
            _statusLabel.Text = $"'{cskuCode}'는 아직 CSKU로 등록되지 않았습니다(미배정).";
            return;
        }
        using var dialog = new CskuDetailDialog(CurrentChannelCode, cskuCode, _channelSkuRepo, _itemRepo);
        FormManager.ShowDialogSafe(dialog, this);
        if (!_viewingHistory) ResolveAndRefresh();
    }

    // ─── 마감 확정 / 이력 ─────────────────────────────────────────────────

    private void OnConfirmClick()
    {
        if (!RequireChannel() || _closings.Count == 0) return;
        ResolveAndRefresh();

        int unassigned = AllLines.Count(l => l.IsUnassigned);
        if (unassigned > 0)
        {
            MessageBox.Show($"미배정 라인이 {unassigned}건 있어 마감을 확정할 수 없습니다.\n[미배정 일괄 배정]으로 먼저 MSKU를 지정하세요.",
                "마감 확정", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var overwrite = _closings
            .Select(c => (c.Period, Existing: _closingRepo.Get(c.Period, CurrentChannelCode)))
            .Where(x => x.Existing != null)
            .Select(x => $"{x.Period} ({x.Existing!.Status}, {x.Existing.SourceSheetName})")
            .ToList();
        var message = $"{CurrentChannelName} / 마감월 {string.Join(", ", _closings.Select(c => c.Period))}을(를) 확정합니다.";
        if (overwrite.Count > 0)
            message += "\n\n이미 저장된 마감이 있어 덮어씁니다:\n" + string.Join("\n", overwrite);
        if (MessageBox.Show(message, "마감 확정", MessageBoxButtons.OKCancel,
                overwrite.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.OK)
            return;

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var closing in _closings)
        {
            closing.Status = ManualGrowthClosing.StatusConfirmed;
            closing.ConfirmedAt = now;
            _closingRepo.Save(closing);
        }
        RefreshHistory();
        _statusLabel.Text = $"마감 확정 완료 — {string.Join(", ", _closings.Select(c => c.Period))}. 필요하면 [리포트 반영]을 누르세요.";

        if (MessageBox.Show("거래처 마감보드에도 확정 상태로 보낼까요?\n(나중에 이력 탭의 [마감보드 전송]으로 보낼 수도 있습니다.)",
                "거래처 마감보드로 보내기", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            SendToPartnerBoard(_closings);
    }

    /// <summary>
    /// 확정된 마감을 거래처 마감보드로 보낸다(채널명과 같은 이름의 수동 거래처, 일자별 라인 그대로).
    /// 마감보드 쪽이 이미 확정돼 있는 달은 건너뛰고 알려준다.
    /// </summary>
    private void SendToPartnerBoard(List<ManualGrowthClosing> closings)
    {
        var transfer = new ManualGrowthBoardTransfer(new PartnerClosingRepository(), new PartnerMasterRepository());
        var done = new List<string>();
        var skipped = new List<string>();
        foreach (var closing in closings)
        {
            try
            {
                var header = transfer.Transfer(closing, CurrentChannelName);
                done.Add($"{closing.Period}  {header.TotalSupply:N0}원(VAT포함)");
            }
            catch (InvalidOperationException ex)
            {
                skipped.Add(ex.Message);
            }
        }

        var message = $"거래처 마감보드 '{CurrentChannelName}'로 {done.Count}건 보냈습니다(확정).";
        if (done.Count > 0) message += "\n" + string.Join("\n", done);
        if (skipped.Count > 0) message += "\n\n건너뜀:\n" + string.Join("\n", skipped);
        MessageBox.Show(message, "거래처 마감보드로 보내기", MessageBoxButtons.OK, skipped.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    private void OnSendHistoryToBoardClick()
    {
        var selected = SelectedHistory().Where(c => c.Status == ManualGrowthClosing.StatusConfirmed).ToList();
        if (selected.Count == 0) { _statusLabel.Text = "마감보드로 보낼 확정 상태 마감을 선택하세요."; return; }
        var full = selected.Select(h => _closingRepo.Get(h.Period, h.ChannelCode)).OfType<ManualGrowthClosing>().ToList();
        SendToPartnerBoard(full);
    }

    private void RefreshHistory()
    {
        _historyGrid.DataSource = CurrentChannelCode.Length == 0
            ? null
            : new BindingList<ManualGrowthClosing>(_closingRepo.GetHeaders(CurrentChannelCode));
    }

    private List<ManualGrowthClosing> SelectedHistory() =>
        _historyGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as ManualGrowthClosing)
            .OfType<ManualGrowthClosing>()
            .OrderBy(c => c.Period, StringComparer.Ordinal)
            .ToList();

    private void LoadFromHistory()
    {
        var selected = SelectedHistory();
        if (selected.Count == 0) { _statusLabel.Text = "이력에서 불러올 마감을 선택하세요."; return; }

        _closings = selected.Select(h => _closingRepo.Get(h.Period, h.ChannelCode)).OfType<ManualGrowthClosing>().ToList();
        _sheetResults = [];
        _viewingHistory = true;
        RefreshGrids();
        _tabs.SelectedIndex = 0;
        _statusLabel.Text = $"저장된 마감 {string.Join(", ", _closings.Select(c => $"{c.Period}({c.Status})"))}을(를) 불러왔습니다. 확정 당시 스냅샷 값입니다.";
    }

    private void OnUnconfirmClick()
    {
        var selected = SelectedHistory().Where(c => c.Status == ManualGrowthClosing.StatusConfirmed).ToList();
        if (selected.Count == 0) { _statusLabel.Text = "확정 상태인 마감을 선택하세요."; return; }
        if (MessageBox.Show($"{string.Join(", ", selected.Select(c => c.Period))} 마감을 확정해제합니다.\n(라인 스냅샷은 남고, 리포트에 반영된 값은 그대로입니다.)",
                "확정해제", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        foreach (var c in selected) _closingRepo.SetStatus(c.Id, ManualGrowthClosing.StatusUnconfirmed);
        RefreshHistory();
        _statusLabel.Text = "확정해제했습니다.";
    }

    private void OnDeleteHistoryClick()
    {
        var selected = SelectedHistory();
        if (selected.Count == 0) return;
        var confirmed = selected.Where(c => c.Status == ManualGrowthClosing.StatusConfirmed).ToList();
        if (confirmed.Count > 0)
        {
            MessageBox.Show("확정 상태인 마감은 삭제할 수 없습니다. 먼저 확정해제하세요.", "삭제", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show($"{string.Join(", ", selected.Select(c => c.Period))} 마감 기록을 삭제합니다.\n(리포트에 반영된 값은 지워지지 않습니다.)",
                "삭제", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        foreach (var c in selected) _closingRepo.Delete(c.Id);
        RefreshHistory();
        _statusLabel.Text = "삭제했습니다.";
    }

    // ─── 리포트 반영 ──────────────────────────────────────────────────────

    private void OnReportClick()
    {
        if (!RequireChannel() || _closings.Count == 0) return;
        if (AllLines.Any(l => l.IsUnassigned))
        {
            MessageBox.Show("미배정 라인이 있어 리포트에 반영할 수 없습니다.", "리포트 반영", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 정산엑셀 전환 후 MonthlyClosing/마감·이익분석도 같은 채널코드로 저장하면 마지막 저장이 덮어쓴다(스펙 §8).
        var existing = _closings.Where(c => _profitFactRepo.HasData(c.Period, CurrentChannelCode)).Select(c => c.Period).ToList();
        var message = $"종합보고서(ProfitFact)에 {CurrentChannelName} / {string.Join(", ", _closings.Select(c => c.Period))}을(를) 품목그룹 단위로 저장합니다.\n" +
                      "매출은 VAT포함(공급가액+세액) 기준입니다.";
        if (existing.Count > 0)
            message += $"\n\n이미 저장된 값이 있는 월: {string.Join(", ", existing)}\n→ 기존 값을 지우고 이번 값으로 바꿉니다.";
        if (MessageBox.Show(message, "리포트 반영", MessageBoxButtons.OKCancel,
                existing.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.OK) return;

        foreach (var closing in _closings)
            _profitFactRepo.SaveProfitFacts(closing.Period, CurrentChannelCode, CurrentChannelName,
                ManualGrowthClosingEngine.BuildProfitFacts(closing.Lines));
        _statusLabel.Text = $"리포트 반영 완료 — {string.Join(", ", _closings.Select(c => c.Period))}.";
    }

    // ─── 엑셀 내보내기 ────────────────────────────────────────────────────

    private void OnExportClick()
    {
        if (_closings.Count == 0) return;
        var periods = string.Join("_", _closings.Select(c => c.Period.Replace("-", "")));
        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"이공그로스수동마감_{periods}_{DateTime.Now:yyyyMMdd}.xlsx",
            _settings.GetLastFolder(ExportFolderKey) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;
        _settings.SetLastFolder(ExportFolderKey, Path.GetDirectoryName(filePath)!);

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var lines = AllLines.ToList();

            WriteSheet(package.Workbook.Worksheets.Add("CSKU요약"),
                ["마감월", "CSKU(상품명)", "MSKU", "품목그룹", "수량", "공급가", "매출(VAT포함)", "원가", "이익", "이익률"],
                ManualGrowthClosingEngine.BuildCskuSummary(lines).Select(s => new object?[]
                {
                    s.Period, s.CskuCode, s.MasterSku, s.ProductGroup, s.Qty, s.SupplyAmount, s.Revenue, s.Cost, s.Profit,
                    s.Revenue == 0 ? null : s.Profit / s.Revenue,
                }), percentCol: 10);

            WriteSheet(package.Workbook.Worksheets.Add("라인"),
                ["마감월", "날짜", "상품명(=CSKU)", "MSKU", "품목그룹", "수량", "단가", "공급가", "세액", "매출(VAT포함)", "원가단가", "원가", "이익", "이익률", "상태"],
                lines.Select(l => new object?[]
                {
                    l.Period, l.LineDate, l.CskuCode, l.MasterSku, l.ProductGroup, l.Qty, l.UnitPrice, l.SupplyAmount, l.Tax,
                    l.Revenue, l.CostPrice, l.Cost, l.Profit, l.MarginRate, l.StatusText,
                }), percentCol: 14);

            ExportHelper.SaveExcel(package, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
            _statusLabel.Text = $"엑셀로 저장했습니다. ({DateTime.Now:HH:mm:ss})";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ExportHelper.DescribeSaveError(ex), "저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void WriteSheet(ExcelWorksheet ws, string[] headers, IEnumerable<object?[]> rows, int percentCol)
    {
        for (int i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
        int r = 1;
        foreach (var row in rows)
        {
            r++;
            for (int i = 0; i < row.Length; i++) ws.Cells[r, i + 1].Value = row[i];
        }
        ws.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
        if (r > 1)
        {
            ws.Cells[2, 1, r, headers.Length].Style.Numberformat.Format = "#,##0";
            ws.Cells[2, 1, r, 1].Style.Numberformat.Format = "@";
            ws.Cells[2, percentCol, r, percentCol].Style.Numberformat.Format = "0.0%";
        }
        ws.View.FreezePanes(2, 1);
        ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }
}

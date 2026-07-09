using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace MiniERP2.Forms;

/// <summary>
/// 종합보고서 — 마감/이익분석 + 광고비 DB를 기간·채널·상품그룹 기준으로 피벗하여 보여주고
/// 엑셀로 내보낸다. ProfitFactTable / AdFactTable에 사전 저장된 데이터를 소스로 한다.
/// </summary>
public class ReportForm : Form
{
    // ──────────── 서비스 ────────────
    private readonly ProfitFactRepository _repo = new();
    private readonly SettingsService _settingsService = new();

    // ──────────── 필터 UI ────────────
    private CheckedListBox _periodList = new();
    private CheckedListBox _channelList = new();
    private CheckedListBox _groupList = new();

    // ──────────── 피벗 설정 UI ────────────
    private ComboBox _rowDimCombo = new();
    private ComboBox _colDimCombo = new();
    private CheckedListBox _metricList = new();

    // ──────────── 결과 그리드 ────────────
    private DataGridView _pivotGrid = new();
    private Label _statusLabel = new();

    // ──────────── 데이터 캐시 ────────────
    private List<ProfitFactRow> _profitRows = [];
    private List<AdFactRow> _adRows = [];

    // 피벗 차원 선택지
    private static readonly string[] DimOptions = ["기간", "채널", "상품그룹"];
    // 지표 선택지
    private static readonly string[] MetricOptions = ["수량", "매출액", "매출총이익", "광고비", "순이익", "마진율", "광고비율"];

    public ReportForm()
    {
        InitializeComponent();
        LoadFilters();
    }

    private void InitializeComponent()
    {
        Text = "종합보고서";
        Size = new Size(1280, 820);
        StartPosition = FormStartPosition.CenterScreen;

        // 좌: 필터 패널 (200px), 우: 피벗 설정 + 결과
        var outer = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 200, FixedPanel = FixedPanel.Panel1 };

        outer.Panel1.Controls.Add(CreateFilterPanel());
        outer.Panel2.Controls.Add(CreateMainPanel());

        Controls.Add(outer);
    }

    // ──────────────────────────────────────────────
    // 좌측 필터 패널
    // ──────────────────────────────────────────────

    private Control CreateFilterPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, Padding = new Padding(8) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _periodList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        _channelList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        _groupList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };

        panel.Controls.Add(new Label { Text = "기간", AutoSize = true });
        panel.Controls.Add(_periodList);
        panel.Controls.Add(new Label { Text = "채널", AutoSize = true });
        panel.Controls.Add(_channelList);
        panel.Controls.Add(new Label { Text = "상품그룹", AutoSize = true });
        panel.Controls.Add(_groupList);

        var btnRefresh = new Button { Text = "조회", Dock = DockStyle.Fill, Height = 32 };
        btnRefresh.Click += OnRefreshClick;
        panel.Controls.Add(btnRefresh);

        return panel;
    }

    // ──────────────────────────────────────────────
    // 우측 메인 패널 (피벗 설정 + 결과 그리드 + 상태바 + 버튼)
    // ──────────────────────────────────────────────

    private Control CreateMainPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        layout.Controls.Add(CreatePivotConfigPanel(), 0, 0);
        layout.Controls.Add(CreatePivotGrid(), 0, 1);
        layout.Controls.Add(CreateButtonStrip(), 0, 2);

        _statusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
        layout.Controls.Add(_statusLabel, 0, 3);

        return layout;
    }

    private Control CreatePivotConfigPanel()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 0, 0) };

        flow.Controls.Add(new Label { Text = "행:", AutoSize = true, Padding = new Padding(0, 4, 4, 0) });
        _rowDimCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        _rowDimCombo.Items.AddRange(DimOptions);
        _rowDimCombo.SelectedIndex = 1; // 채널
        flow.Controls.Add(_rowDimCombo);

        flow.Controls.Add(new Label { Text = "열:", AutoSize = true, Padding = new Padding(8, 4, 4, 0) });
        _colDimCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        _colDimCombo.Items.AddRange(DimOptions);
        _colDimCombo.SelectedIndex = 2; // 상품그룹
        flow.Controls.Add(_colDimCombo);

        flow.Controls.Add(new Label { Text = "지표:", AutoSize = true, Padding = new Padding(12, 4, 4, 0) });
        _metricList = new CheckedListBox { Width = 180, Height = 80 };
        foreach (var m in MetricOptions) _metricList.Items.Add(m, true);
        flow.Controls.Add(_metricList);

        return flow;
    }

    private Control CreatePivotGrid()
    {
        _pivotGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
        };
        return _pivotGrid;
    }

    private Control CreateButtonStrip()
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), FlowDirection = FlowDirection.LeftToRight };

        var btnImport = new Button { Text = "Excel에서 불러오기", Size = new Size(140, 28) };
        btnImport.Click += OnImportFromExcelClick;
        flow.Controls.Add(btnImport);

        var btnExcel = new Button { Text = "Excel 내보내기", Size = new Size(120, 28) };
        btnExcel.Click += OnExportExcelClick;
        flow.Controls.Add(btnExcel);
        return flow;
    }

    // ──────────────────────────────────────────────
    // Excel에서 이익분析 결과 직접 불러오기
    // ──────────────────────────────────────────────

    private void OnImportFromExcelClick(object? sender, EventArgs e)
    {
        using var fileDlg = new OpenFileDialog
        {
            Title = "마감/이익분析 결과 Excel 파일 선택",
            Filter = "Excel 파일|*.xlsx;*.xls",
            Multiselect = false,
        };
        if (fileDlg.ShowDialog(this) != DialogResult.OK) return;

        var input = ShowImportInputDialog(this);
        if (input is null) return;
        var (channelName, period) = input.Value;

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage(new FileInfo(fileDlg.FileName));

            // 분析요약(상품그룹별) 시트 우선, 없으면 첫 번째 시트
            var sheet = package.Workbook.Worksheets["분析요약(상품그룹별)"]
                ?? package.Workbook.Worksheets.FirstOrDefault()
                ?? throw new InvalidOperationException("시트를 찾을 수 없습니다.");

            // 헤더: 상품그룹(1) 건수(2) 수량(3) 매출액(4) 배송비(5) 입출고비(6) 순이익(7)
            // 헤더행 자동 탐지: 1행에 "상품그룹" 있으면 2행부터, 없으면 에러
            int headerRow = 1;
            if (!string.Equals(sheet.Cells[1, 1].Text?.Trim(), "상품그룹", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A1 셀이 '상품그룹'이 아닙니다. 마감/이익분析 내보내기 파일의 '분析요약(상품그룹별)' 시트를 선택하세요.");

            int colQty = FindHeaderCol(sheet, headerRow, "수량");
            int colRevenue = FindHeaderCol(sheet, headerRow, "매출액");
            int colProfit = FindHeaderCol(sheet, headerRow, "순이익");

            var lastRow = sheet.Dimension?.End.Row ?? headerRow;
            var facts = new List<ProfitFactRow>();
            for (int row = headerRow + 1; row <= lastRow; row++)
            {
                var group = sheet.Cells[row, 1].Text?.Trim();
                if (string.IsNullOrEmpty(group) || group == "합계") continue;

                facts.Add(new ProfitFactRow
                {
                    ProductGroup = group,
                    Qty = (int)ToDouble(sheet.Cells[row, colQty]),
                    Revenue = (decimal)ToDouble(sheet.Cells[row, colRevenue]),
                    GrossProfit = (decimal)ToDouble(sheet.Cells[row, colProfit]),
                });
            }

            if (facts.Count == 0)
            {
                _statusLabel.Text = "파싱된 상품그룹 데이터가 없습니다. 파일 형식을 확인하세요.";
                return;
            }

            if (_repo.HasData(period, channelName))
            {
                if (MessageBox.Show($"{period} / {channelName} 데이터가 이미 있습니다. 덮어쓰시겠습니까?",
                    "덮어쓰기 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            _repo.SaveProfitFacts(period, channelName, channelName, facts);
            LoadFilters();
            _statusLabel.Text = $"불러오기 완료 — {period} / {channelName} / {facts.Count}개 그룹";
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = $"불러오기 오류: {ex.Message}";
        }
    }

    private static (string ChannelName, string Period)? ShowImportInputDialog(IWin32Window owner)
    {
        using var form = new Form
        {
            Text = "채널 및 기간 입력",
            Size = new Size(340, 190),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 8), RowCount = 3, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var txtChannel = new TextBox { Dock = DockStyle.Fill };
        var txtPeriod = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "예: 2026-06" };

        layout.Controls.Add(new Label { Text = "채널명:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(txtChannel, 1, 0);
        layout.Controls.Add(new Label { Text = "기간:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 1);
        layout.Controls.Add(txtPeriod, 1, 1);

        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 4, 0, 0) };
        var btnOk = new Button { Text = "확인", Size = new Size(75, 28) };
        var btnCancel = new Button { Text = "취소", Size = new Size(75, 28), DialogResult = DialogResult.Cancel };
        btnFlow.Controls.Add(btnCancel);
        btnFlow.Controls.Add(btnOk);
        layout.Controls.Add(btnFlow, 1, 2);

        form.Controls.Add(layout);
        form.CancelButton = btnCancel;

        btnOk.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(txtChannel.Text)) { MessageBox.Show("채널명을 입력하세요.", "입력 오류"); return; }
            if (!System.Text.RegularExpressions.Regex.IsMatch(txtPeriod.Text.Trim(), @"^\d{4}-\d{2}$")) { MessageBox.Show("기간은 YYYY-MM 형식으로 입력하세요.", "입력 오류"); return; }
            form.DialogResult = DialogResult.OK;
        };

        return form.ShowDialog(owner) == DialogResult.OK
            ? (txtChannel.Text.Trim(), txtPeriod.Text.Trim())
            : null;
    }

    private static int FindHeaderCol(ExcelWorksheet sheet, int headerRow, string header)
    {
        var lastCol = sheet.Dimension?.End.Column ?? 10;
        for (int c = 1; c <= lastCol; c++)
            if (string.Equals(sheet.Cells[headerRow, c].Text?.Trim(), header, StringComparison.OrdinalIgnoreCase))
                return c;
        throw new InvalidOperationException($"헤더 '{header}'를 찾을 수 없습니다.");
    }

    private static double ToDouble(ExcelRange cell) => cell.Value switch
    {
        double d => d,
        int i => i,
        decimal dec => (double)dec,
        _ => double.TryParse(cell.Text?.Trim(), out var v) ? v : 0,
    };

    // ──────────────────────────────────────────────
    // 필터 목록 로드
    // ──────────────────────────────────────────────

    private void LoadFilters()
    {
        var profitPeriods = _repo.GetDistinctProfitPeriods();
        var adPeriods = _repo.GetDistinctAdPeriods();
        var allPeriods = profitPeriods.Union(adPeriods).OrderByDescending(p => p).ToList();

        _periodList.Items.Clear();
        foreach (var p in allPeriods) _periodList.Items.Add(p, true);

        // 채널/그룹은 조회 후 채워지므로 일단 비워둔다
        _channelList.Items.Clear();
        _groupList.Items.Clear();

        // 최초 조회
        OnRefreshClick(null, EventArgs.Empty);
    }

    // ──────────────────────────────────────────────
    // 조회 + 피벗 렌더
    // ──────────────────────────────────────────────

    private void OnRefreshClick(object? sender, EventArgs e)
    {
        var periods = GetChecked(_periodList);
        _profitRows = _repo.GetProfitFacts(periods.Count > 0 ? periods : null);
        _adRows = _repo.GetAdFacts(periods.Count > 0 ? periods : null);

        // 채널/그룹 목록 갱신
        RefreshDimList(_channelList, _profitRows.Select(r => r.ChannelName).Union(_adRows.Select(r => r.ChannelName)).Distinct().OrderBy(x => x).ToList());
        RefreshDimList(_groupList, _profitRows.Select(r => r.ProductGroup).Union(_adRows.Select(r => r.ProductGroup)).Distinct().OrderBy(x => x).ToList());

        RenderPivot();
    }

    private void RenderPivot()
    {
        var selectedChannels = GetChecked(_channelList).ToHashSet();
        var selectedGroups = GetChecked(_groupList).ToHashSet();
        var selectedMetrics = GetChecked(_metricList);

        // 필터 적용
        var profit = _profitRows
            .Where(r => selectedChannels.Count == 0 || selectedChannels.Contains(r.ChannelName))
            .Where(r => selectedGroups.Count == 0 || selectedGroups.Contains(r.ProductGroup))
            .ToList();
        var ad = _adRows
            .Where(r => selectedChannels.Count == 0 || selectedChannels.Contains(r.ChannelName))
            .Where(r => selectedGroups.Count == 0 || selectedGroups.Contains(r.ProductGroup))
            .ToList();

        string rowDim = _rowDimCombo.SelectedItem?.ToString() ?? "채널";
        string colDim = _colDimCombo.SelectedItem?.ToString() ?? "상품그룹";

        // 차원 키 추출 함수
        Func<ProfitFactRow, string> profitRowKey = rowDim switch
        {
            "기간" => r => r.Period,
            "채널" => r => r.ChannelName,
            _ => r => r.ProductGroup,
        };
        Func<ProfitFactRow, string> profitColKey = colDim switch
        {
            "기간" => r => r.Period,
            "채널" => r => r.ChannelName,
            _ => r => r.ProductGroup,
        };
        Func<AdFactRow, string> adRowKey = rowDim switch
        {
            "기간" => r => r.Period,
            "채널" => r => r.ChannelName,
            _ => r => r.ProductGroup,
        };
        Func<AdFactRow, string> adColKey = colDim switch
        {
            "기간" => r => r.Period,
            "채널" => r => r.ChannelName,
            _ => r => r.ProductGroup,
        };

        var rowKeys = profit.Select(profitRowKey).Union(ad.Select(adRowKey)).Distinct().OrderBy(k => k).ToList();
        var colKeys = profit.Select(profitColKey).Union(ad.Select(adColKey)).Distinct().OrderBy(k => k).ToList();

        // 셀 집계
        var cells = new Dictionary<(string, string), PivotCell>();
        foreach (var r in profit)
        {
            var key = (profitRowKey(r), profitColKey(r));
            if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = new PivotCell();
            cell.Qty += r.Qty;
            cell.Revenue += r.Revenue;
            cell.GrossProfit += r.GrossProfit;
        }
        foreach (var r in ad)
        {
            var key = (adRowKey(r), adColKey(r));
            if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = new PivotCell();
            cell.AdCost += r.AdCost;
        }

        // 그리드 구성
        _pivotGrid.Columns.Clear();
        _pivotGrid.Rows.Clear();
        _pivotGrid.AutoGenerateColumns = false;

        var firstCol = new DataGridViewTextBoxColumn { HeaderText = rowDim, Width = 120, ReadOnly = true };
        _pivotGrid.Columns.Add(firstCol);

        foreach (var col in colKeys)
        {
            foreach (var metric in selectedMetrics)
            {
                _pivotGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = $"{col}\n{metric}",
                    Width = 90,
                    ReadOnly = true,
                    DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight },
                });
            }
        }

        // 행 합계 열
        if (colKeys.Count > 1)
        {
            foreach (var metric in selectedMetrics)
            {
                _pivotGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = $"합계\n{metric}",
                    Width = 90,
                    ReadOnly = true,
                    DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, BackColor = Color.FromArgb(245, 245, 220) },
                });
            }
        }

        foreach (var rowKey in rowKeys)
        {
            var rowValues = new List<object?> { rowKey };
            foreach (var col in colKeys)
            {
                var cell = cells.TryGetValue((rowKey, col), out var c) ? c : new PivotCell();
                foreach (var metric in selectedMetrics)
                    rowValues.Add(FormatMetric(metric, cell));
            }
            if (colKeys.Count > 1)
            {
                var totalCell = colKeys.Aggregate(new PivotCell(), (acc, col) =>
                {
                    if (cells.TryGetValue((rowKey, col), out var c))
                    {
                        acc.Qty += c.Qty;
                        acc.Revenue += c.Revenue;
                        acc.GrossProfit += c.GrossProfit;
                        acc.AdCost += c.AdCost;
                    }
                    return acc;
                });
                foreach (var metric in selectedMetrics)
                    rowValues.Add(FormatMetric(metric, totalCell));
            }

            var gridRow = new DataGridViewRow();
            gridRow.CreateCells(_pivotGrid, rowValues.Select(v => v ?? (object)string.Empty).ToArray());
            _pivotGrid.Rows.Add(gridRow);
        }

        _statusLabel.Text = $"총 {rowKeys.Count}개 행 × {colKeys.Count}개 열 | 이익데이터 {profit.Count}건, 광고데이터 {ad.Count}건";
    }

    private static string FormatMetric(string metric, PivotCell c)
    {
        decimal netProfit = c.GrossProfit - c.AdCost;
        return metric switch
        {
            "수량" => c.Qty.ToString("N0"),
            "매출액" => c.Revenue.ToString("N0"),
            "매출총이익" => c.GrossProfit.ToString("N0"),
            "광고비" => c.AdCost.ToString("N0"),
            "순이익" => netProfit.ToString("N0"),
            "마진율" => c.Revenue == 0 ? "-" : (netProfit / c.Revenue).ToString("P1"),
            "광고비율" => c.Revenue == 0 ? "-" : (c.AdCost / c.Revenue).ToString("P1"),
            _ => string.Empty,
        };
    }

    // ──────────────────────────────────────────────
    // Excel 내보내기
    // ──────────────────────────────────────────────

    private async void OnExportExcelClick(object? sender, EventArgs e)
    {
        ExcelLicense.Ensure();
        using var sfd = new SaveFileDialog
        {
            Title = "종합보고서 엑셀 저장",
            Filter = "Excel 파일 (*.xlsx)|*.xlsx",
            FileName = $"종합보고서_{DateTime.Today:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("ReportExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (sfd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("ReportExport", Path.GetDirectoryName(sfd.FileName)!);

        var filePath = sfd.FileName;
        var periods = GetChecked(_periodList);
        var selectedChannels = GetChecked(_channelList).ToHashSet();
        var selectedGroups = GetChecked(_groupList).ToHashSet();
        var selectedMetrics = GetChecked(_metricList);
        var profit = _profitRows
            .Where(r => selectedChannels.Count == 0 || selectedChannels.Contains(r.ChannelName))
            .Where(r => selectedGroups.Count == 0 || selectedGroups.Contains(r.ProductGroup))
            .ToList();
        var ad = _adRows
            .Where(r => selectedChannels.Count == 0 || selectedChannels.Contains(r.ChannelName))
            .Where(r => selectedGroups.Count == 0 || selectedGroups.Contains(r.ProductGroup))
            .ToList();

        string rowDim = _rowDimCombo.SelectedItem?.ToString() ?? "채널";
        string colDim = _colDimCombo.SelectedItem?.ToString() ?? "상품그룹";

        Cursor = Cursors.WaitCursor;
        _statusLabel.Text = "엑셀 생성 중...";
        try
        {
            await Task.Run(() => WriteExcel(filePath, profit, ad, rowDim, colDim, selectedMetrics));
            _statusLabel.Text = $"저장 완료: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀 저장 오류: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "저장 오류";
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static void WriteExcel(string filePath, List<ProfitFactRow> profit, List<AdFactRow> ad, string rowDim, string colDim, List<string> metrics)
    {
        // 셀 집계
        Func<ProfitFactRow, string> profitRowKey = rowDim switch { "기간" => r => r.Period, "채널" => r => r.ChannelName, _ => r => r.ProductGroup };
        Func<ProfitFactRow, string> profitColKey = colDim switch { "기간" => r => r.Period, "채널" => r => r.ChannelName, _ => r => r.ProductGroup };
        Func<AdFactRow, string> adRowKey = rowDim switch { "기간" => r => r.Period, "채널" => r => r.ChannelName, _ => r => r.ProductGroup };
        Func<AdFactRow, string> adColKey = colDim switch { "기간" => r => r.Period, "채널" => r => r.ChannelName, _ => r => r.ProductGroup };

        var rowKeys = profit.Select(profitRowKey).Union(ad.Select(adRowKey)).Distinct().OrderBy(k => k).ToList();
        var colKeys = profit.Select(profitColKey).Union(ad.Select(adColKey)).Distinct().OrderBy(k => k).ToList();

        var cells = new Dictionary<(string, string), PivotCell>();
        foreach (var r in profit)
        {
            var key = (profitRowKey(r), profitColKey(r));
            if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = new PivotCell();
            cell.Qty += r.Qty; cell.Revenue += r.Revenue; cell.GrossProfit += r.GrossProfit;
        }
        foreach (var r in ad)
        {
            var key = (adRowKey(r), adColKey(r));
            if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = new PivotCell();
            cell.AdCost += r.AdCost;
        }

        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("종합보고서");

        // 헤더 행 1: 행차원 + 열 키
        int headerRow = 1;
        ws.Cells[headerRow, 1].Value = rowDim;
        var col = 2;
        foreach (var colKey in colKeys)
        {
            ws.Cells[headerRow, col].Value = colKey;
            ws.Cells[headerRow, col, headerRow, col + metrics.Count - 1].Merge = true;
            col += metrics.Count;
        }
        ws.Cells[headerRow, col].Value = "합계";
        ws.Cells[headerRow, col, headerRow, col + metrics.Count - 1].Merge = true;

        // 헤더 행 2: 지표명
        col = 2;
        for (int ci = 0; ci < colKeys.Count + 1; ci++)
        {
            foreach (var metric in metrics)
                ws.Cells[headerRow + 1, col++].Value = metric;
        }

        // 데이터 행
        int row = headerRow + 2;
        foreach (var rowKey in rowKeys)
        {
            ws.Cells[row, 1].Value = rowKey;
            col = 2;
            var totalCell = new PivotCell();
            foreach (var colKey in colKeys)
            {
                var c = cells.TryGetValue((rowKey, colKey), out var found) ? found : new PivotCell();
                totalCell.Qty += c.Qty; totalCell.Revenue += c.Revenue; totalCell.GrossProfit += c.GrossProfit; totalCell.AdCost += c.AdCost;
                foreach (var metric in metrics)
                    WriteMetricCell(ws.Cells[row, col++], metric, c);
            }
            foreach (var metric in metrics)
                WriteMetricCell(ws.Cells[row, col++], metric, totalCell);
            row++;
        }

        // 합계 행
        ws.Cells[row, 1].Value = "합계";
        col = 2;
        var grandTotal = new PivotCell();
        foreach (var rowKey in rowKeys) foreach (var colKey in colKeys)
        {
            if (cells.TryGetValue((rowKey, colKey), out var c))
            { grandTotal.Qty += c.Qty; grandTotal.Revenue += c.Revenue; grandTotal.GrossProfit += c.GrossProfit; grandTotal.AdCost += c.AdCost; }
        }
        for (int ci = 0; ci < colKeys.Count + 1; ci++)
            foreach (var metric in metrics) WriteMetricCell(ws.Cells[row, col++], metric, grandTotal);

        // 서식
        var headerFill = ws.Cells[1, 1, 2, col - 1];
        headerFill.Style.Fill.PatternType = ExcelFillStyle.Solid;
        headerFill.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(68, 114, 196));
        headerFill.Style.Font.Color.SetColor(System.Drawing.Color.White);
        headerFill.Style.Font.Bold = true;
        headerFill.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        var totalRowCells = ws.Cells[row, 1, row, col - 1];
        totalRowCells.Style.Font.Bold = true;
        totalRowCells.Style.Fill.PatternType = ExcelFillStyle.Solid;
        totalRowCells.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(217, 225, 242));

        ws.Cells[1, 1, row, col - 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
        ws.Cells.AutoFitColumns(8, 25);

        // 월별 시계열 시트 (기간×채널 or 기간×그룹)
        WriteTimeSeriesSheet(package, profit, ad);

        ExportHelper.SaveExcel(package, filePath);
    }

    private static void WriteMetricCell(ExcelRange cell, string metric, PivotCell c)
    {
        decimal net = c.GrossProfit - c.AdCost;
        switch (metric)
        {
            case "수량":        cell.Value = c.Qty;                    cell.Style.Numberformat.Format = "#,##0"; break;
            case "매출액":      cell.Value = (double)c.Revenue;        cell.Style.Numberformat.Format = "#,##0"; break;
            case "매출총이익":  cell.Value = (double)c.GrossProfit;    cell.Style.Numberformat.Format = "#,##0"; break;
            case "광고비":      cell.Value = (double)c.AdCost;         cell.Style.Numberformat.Format = "#,##0"; break;
            case "순이익":      cell.Value = (double)net;              cell.Style.Numberformat.Format = "#,##0"; break;
            case "마진율":      cell.Value = c.Revenue == 0 ? 0 : (double)(net / c.Revenue); cell.Style.Numberformat.Format = "0.0%"; break;
            case "광고비율":    cell.Value = c.Revenue == 0 ? 0 : (double)(c.AdCost / c.Revenue); cell.Style.Numberformat.Format = "0.0%"; break;
        }
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
    }

    private static void WriteTimeSeriesSheet(ExcelPackage package, List<ProfitFactRow> profit, List<AdFactRow> ad)
    {
        var periods = profit.Select(r => r.Period).Union(ad.Select(r => r.Period)).Distinct().OrderBy(p => p).ToList();
        var channels = profit.Select(r => r.ChannelName).Union(ad.Select(r => r.ChannelName)).Distinct().OrderBy(c => c).ToList();

        if (periods.Count < 2) return; // 단일 기간이면 시계열 의미 없음

        var ws = package.Workbook.Worksheets.Add("월별시계열");

        // 헤더: 채널(1열), 지표(2열), 기간(3열~)
        ws.Cells[1, 1].Value = "채널";
        ws.Cells[1, 2].Value = "지표";
        for (int pi = 0; pi < periods.Count; pi++)
            ws.Cells[1, 3 + pi].Value = periods[pi];

        // 데이터: 채널 × 지표(수량/매출액/순이익/광고비)
        var tsMetrics = new[] { "수량", "매출액", "순이익", "광고비" };
        int row = 2;
        foreach (var ch in channels)
        {
            // 채널명은 지표 수만큼 행에 걸쳐 병합
            ws.Cells[row, 1].Value = ch;
            if (tsMetrics.Length > 1)
                ws.Cells[row, 1, row + tsMetrics.Length - 1, 1].Merge = true;

            for (int mi = 0; mi < tsMetrics.Length; mi++)
            {
                ws.Cells[row + mi, 2].Value = tsMetrics[mi];

                for (int pi = 0; pi < periods.Count; pi++)
                {
                    var p = periods[pi];
                    var pf = profit.Where(r => r.ChannelName == ch && r.Period == p).ToList();
                    var af = ad.Where(r => r.ChannelName == ch && r.Period == p).ToList();
                    var cell = new PivotCell
                    {
                        Qty = pf.Sum(r => r.Qty),
                        Revenue = pf.Sum(r => r.Revenue),
                        GrossProfit = pf.Sum(r => r.GrossProfit),
                        AdCost = af.Sum(r => r.AdCost),
                    };
                    WriteMetricCell(ws.Cells[row + mi, 3 + pi], tsMetrics[mi], cell);
                }
            }
            row += tsMetrics.Length;
        }

        ws.Cells[1, 1, row - 1, 3 + periods.Count].AutoFitColumns(8, 20);
    }

    // ──────────────────────────────────────────────
    // 유틸
    // ──────────────────────────────────────────────

    private static List<string> GetChecked(CheckedListBox list)
        => list.CheckedItems.Cast<string>().ToList();

    private static void RefreshDimList(CheckedListBox list, List<string> items)
    {
        var checked_ = GetChecked(list).ToHashSet();
        list.Items.Clear();
        foreach (var item in items)
            list.Items.Add(item, checked_.Count == 0 || checked_.Contains(item));
    }
}

/// <summary>피벗 셀 집계 결과.</summary>
internal sealed class PivotCell
{
    public int Qty { get; set; }
    public decimal Revenue { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal AdCost { get; set; }
}

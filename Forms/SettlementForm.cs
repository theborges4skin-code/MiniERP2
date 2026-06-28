using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.DataLoaders;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 기획서 5.6절 '마감/이익분석' 창.
/// 이익분석(자동) 탭과 마감 대조(수기) 탭을 한 화면에서 다룬다.
/// </summary>
public class SettlementForm : Form
{
    private readonly SettingsService _settingsService = new();
    private readonly ChannelConfigService _channelConfigService = new();
    private readonly MappingRepository _mappingRepository = new();
    private readonly ItemRepository _itemRepository = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly SettlementRepository _settlementRepository = new();
    private readonly OutboundRepository _outboundRepository = new();
    private readonly SettlementLoader _settlementLoader = new();
    private readonly SalesChannelRepository _salesChannelRepository = new();

    private ExcelLikeDataGridView _settlementGrid = new();
    private BindingList<SettlementData> _settlementRows = new();
    private MappingForm? _subscribedMappingForm;
    private ToolStripStatusLabel _statusLabel = new();

    private ExcelLikeDataGridView _summaryGrid = new();
    private CheckBox _unmappedOnlyCheckBox = new();
    private Label _summaryTotalsLabel = new();

    private ExcelLikeDataGridView _outboundGrid = new();
    private DataGridView _statementGrid = new();
    private DateTimePicker _fromDatePicker = new();
    private DateTimePicker _toDatePicker = new();
    private ComboBox _reconcileChannelComboBox = new();

    public SettlementForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "마감/이익분석";
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;

        var tabControl = new TabControl { Dock = DockStyle.Fill };
        tabControl.TabPages.Add(CreateProfitAnalysisTab());
        tabControl.TabPages.Add(CreateReconciliationTab());

        Controls.Add(tabControl);

        FormClosing += (s, e) =>
        {
            _settlementGrid.SaveLayout();
            _outboundGrid.SaveLayout();
        };
    }

    // ===================== 이익분석(자동) =====================

    private TabPage CreateProfitAnalysisTab()
    {
        var tabPage = new TabPage("이익분석(자동)");

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnLoad = new Button { Text = "정산파일 로드", Size = new Size(120, 30) };
        var btnSave = new Button { Text = "결과 저장", Size = new Size(100, 30) };
        var btnExport = new Button { Text = "엑셀로 내보내기", Size = new Size(120, 30) };

        btnLoad.Click += OnLoadSettlementClick;
        btnSave.Click += OnSaveSettlementClick;
        btnExport.Click += OnExportSettlementClick;

        toolStrip.Controls.Add(btnLoad);
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnExport);

        // 99.1: 기본값은 미매핑/확인필요 건만 보이게 — 체크 해제하면 전체(미매핑이 위로 정렬된 채)를 본다.
        _unmappedOnlyCheckBox = new CheckBox { Text = "미매핑건만 보기", AutoSize = true, Checked = true, Padding = new Padding(10, 7, 0, 0) };
        _unmappedOnlyCheckBox.CheckedChanged += (s, e) => RefreshProfitAnalysisView();
        toolStrip.Controls.Add(_unmappedOnlyCheckBox);

        _settlementGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "SettlementForm.SettlementGrid",
            AutoGenerateColumns = false,
        };
        // 사용자 요청(2026-06-28): 매핑유무/채널/상품그룹/매핑SKU/상품명/옵션명/수량/매출액/배송비
        // 순으로 고정 노출하고, 채널설정에서 표준필드로 매핑되지 않은 원본파일의 나머지 열은
        // 이 뒤에 그대로 나열한다(RebuildRawTailColumns) — 판매정보를 상세히 보고 상품을 식별해
        // 매핑하기 쉽게 하기 위함. 정산액/입출고비/이익액은 손익계산에는 계속 쓰이지만 식별용
        // 그리드에는 더 이상 노출하지 않는다(상품그룹별 요약 패널에서 확인).
        _settlementGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "매핑유무", Name = "Status", DataPropertyName = "Status", Width = 100, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "채널", Name = "ChannelCode", DataPropertyName = "ChannelCode", Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "상품그룹", Name = "ProductGroup", DataPropertyName = "ProductGroup", Width = 100, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "매핑 SKU", Name = "Msku", DataPropertyName = "Msku", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "상품명", Name = "ProductName", DataPropertyName = "ProductName", Width = 220 },
            new DataGridViewTextBoxColumn { HeaderText = "옵션명", Name = "OptionName", DataPropertyName = "OptionName", Width = 180 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "매출액", Name = "Revenue", DataPropertyName = "Revenue", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "배송비", Name = "Shipping", DataPropertyName = "Shipping", Width = 90 }
        );
        _settlementGrid.RowPrePaint += OnSettlementGridRowPrePaint;
        _settlementGrid.EditingControlShowing += OnSettlementGridEditingControlShowing;
        _settlementGrid.CellValidating += OnSettlementGridCellValidating;
        _settlementGrid.CellEndEdit += OnSettlementGridCellEndEdit;
        _settlementGrid.CellFormatting += OnSettlementGridCellFormatting;
        SetupSettlementGridContextMenu();

        // 99.1: 상단(전체/필터된 목록) + 하단(상품그룹별 요약) 분할. 사용자가 조절한 폭은 기억된다.
        var split = new PersistentSplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 420, PersistenceKey = "SettlementForm.ProfitSplit" };
        split.Panel1.Controls.Add(_settlementGrid);

        var summaryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _summaryTotalsLabel = new Label { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        _summaryGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "SettlementForm.SummaryGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
        };
        _summaryGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "상품그룹", Name = "ProductGroup", DataPropertyName = "ProductGroup", Width = 180 },
            new DataGridViewTextBoxColumn { HeaderText = "건수", Name = "RowCount", DataPropertyName = "RowCount", Width = 70 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 70 },
            new DataGridViewTextBoxColumn { HeaderText = "매출액", Name = "Revenue", DataPropertyName = "Revenue", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "배송비", Name = "Shipping", DataPropertyName = "Shipping", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "입출고비", Name = "Fee", DataPropertyName = "Fee", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "순이익", Name = "Profit", DataPropertyName = "Profit", Width = 110 }
        );
        _summaryGrid.RowPrePaint += OnSummaryGridRowPrePaint;

        summaryLayout.Controls.Add(_summaryTotalsLabel, 0, 0);
        summaryLayout.Controls.Add(_summaryGrid, 0, 1);
        split.Panel2.Controls.Add(WithGroupLabel("상품그룹별 요약 (광고비/택배비는 별도 집계라 미포함)", summaryLayout));

        var statusStrip = new StatusStrip { Dock = DockStyle.Bottom };
        _statusLabel = new ToolStripStatusLabel("준비");
        statusStrip.Items.Add(_statusLabel);

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(split, 0, 1);
        mainLayout.Controls.Add(statusStrip, 0, 2);

        tabPage.Controls.Add(mainLayout);
        RefreshProfitAnalysisView();
        return tabPage;
    }

    /// <summary>
    /// 99.1: 미매핑건만 보기 토글/정렬을 그리드에 반영하고, 하단 상품그룹별 요약을 다시 집계한다.
    /// "결과 저장"/"엑셀로 내보내기"는 항상 <see cref="_settlementRows"/>(원본 전체)을 직접 참조하므로
    /// 여기서 그리드에 보여주는 필터링된 뷰와 무관하게 항상 전체 데이터를 대상으로 동작한다.
    /// </summary>
    private const string TotalRowLabel = "합계";

    /// <summary>진단용: RefreshProfitAnalysisView 내부 단계별 소요 시간(가장 최근 호출 기준).</summary>
    public string LastRefreshDiagnostics { get; private set; } = string.Empty;

    private void RefreshProfitAnalysisView()
    {
        DiagnosticsLogger.Log($"[SettlementForm] RefreshProfitAnalysisView 시작 (_settlementRows={_settlementRows.Count}건)");
        var totalStopwatch = Stopwatch.StartNew();

        var filterStopwatch = Stopwatch.StartNew();
        var view = _unmappedOnlyCheckBox.Checked
            ? _settlementRows.Where(SettlementRowStatus.IsUnresolved).ToList()
            : _settlementRows.OrderByDescending(SettlementRowStatus.IsUnresolved).ToList();
        filterStopwatch.Stop();
        DiagnosticsLogger.Log($"[SettlementForm] 필터링 완료 — 표시대상 {view.Count}건 ({filterStopwatch.Elapsed.TotalSeconds:F2}s)");

        // 성능: 열을 먼저 채운 뒤 데이터를 바인딩한다(반대 순서로 하면 이미 수천 행이 바인딩된
        // 그리드에 동적 열을 하나씩 추가할 때마다 전체 재배치가 일어나 수 분까지 걸릴 수 있다 —
        // "파일 로드는 빠른데 그 다음 처리가 오래 걸린다"는 신고의 원인이었다).
        var unbindStopwatch = Stopwatch.StartNew();
        _settlementGrid.DataSource = null;
        unbindStopwatch.Stop();
        DiagnosticsLogger.Log($"[SettlementForm] 그리드 분리 완료 ({unbindStopwatch.Elapsed.TotalSeconds:F2}s)");

        var rebuildColumnsStopwatch = Stopwatch.StartNew();
        RebuildRawTailColumns(view);
        rebuildColumnsStopwatch.Stop();
        DiagnosticsLogger.Log($"[SettlementForm] 원본열 구성 완료 — 그리드 전체 열수={_settlementGrid.Columns.Count} ({rebuildColumnsStopwatch.Elapsed.TotalSeconds:F2}s)");

        var bindStopwatch = Stopwatch.StartNew();
        _settlementGrid.DataSource = new BindingList<SettlementData>(view);
        bindStopwatch.Stop();
        DiagnosticsLogger.Log($"[SettlementForm] 데이터 바인딩 완료 ({bindStopwatch.Elapsed.TotalSeconds:F2}s)");

        var summaryStopwatch = Stopwatch.StartNew();
        var groups = _settlementRows
            .GroupBy(ResolveProductGroupLabel)
            .Select(g => new ProfitGroupSummary
            {
                ProductGroup = g.Key,
                RowCount = g.Count(),
                Qty = g.Sum(x => x.Qty),
                Revenue = g.Sum(x => x.Revenue),
                Settlement = g.Sum(x => x.Settlement),
                Shipping = g.Sum(x => x.Shipping),
                Fee = g.Sum(x => x.Fee),
                Profit = g.Sum(x => x.Profit),
            })
            .OrderByDescending(s => s.Profit)
            .ToList();

        // 사용자 요청: 합계를 라벨 텍스트로만 보여주니 가시성이 떨어졌다 — 그리드 맨 아래에
        // 별도 행으로 추가하고(OnSummaryGridRowPrePaint) 굵게 강조한다.
        var rowsWithTotal = groups.ToList();
        if (groups.Count > 0)
        {
            rowsWithTotal.Add(new ProfitGroupSummary
            {
                ProductGroup = TotalRowLabel,
                RowCount = groups.Sum(g => g.RowCount),
                Qty = groups.Sum(g => g.Qty),
                Revenue = groups.Sum(g => g.Revenue),
                Settlement = groups.Sum(g => g.Settlement),
                Shipping = groups.Sum(g => g.Shipping),
                Fee = groups.Sum(g => g.Fee),
                Profit = groups.Sum(g => g.Profit),
            });
        }
        _summaryGrid.DataSource = new BindingList<ProfitGroupSummary>(rowsWithTotal);

        _summaryTotalsLabel.Text = BuildTotalsText(groups, _settlementRows.Count(SettlementRowStatus.IsUnresolved));
        summaryStopwatch.Stop();
        totalStopwatch.Stop();

        LastRefreshDiagnostics = $"필터링 {filterStopwatch.Elapsed.TotalSeconds:F2}s, 그리드분리 {unbindStopwatch.Elapsed.TotalSeconds:F2}s, " +
            $"원본열구성 {rebuildColumnsStopwatch.Elapsed.TotalSeconds:F2}s, 데이터바인딩 {bindStopwatch.Elapsed.TotalSeconds:F2}s, " +
            $"요약집계 {summaryStopwatch.Elapsed.TotalSeconds:F2}s, 합계 {totalStopwatch.Elapsed.TotalSeconds:F2}s";
        DiagnosticsLogger.Log($"[SettlementForm] RefreshProfitAnalysisView 완료 — {LastRefreshDiagnostics}");
    }

    /// <summary>
    /// 사용자 요청: 채널설정에서 표준필드로 매핑되지 않은 정산파일 원본 열들을, 고정 9개 열 뒤에
    /// 그대로 나열한다(판매정보를 상세히 보고 상품을 식별해 매핑하기 쉽게 하기 위함). 채널/파일마다
    /// 원본 헤더 구성이 달라지므로 로드할 때마다 동적 열을 다시 만든다.
    /// </summary>
    private void RebuildRawTailColumns(List<SettlementData> rows)
    {
        for (int i = _settlementGrid.Columns.Count - 1; i >= 0; i--)
        {
            if (_settlementGrid.Columns[i].Tag is string) _settlementGrid.Columns.RemoveAt(i);
        }

        if (rows.Count == 0) return;

        var channelConfigs = _channelConfigService.Load();
        var mappedHeaders = rows
            .Select(r => r.ChannelCode)
            .Distinct()
            .SelectMany(code => (IEnumerable<FieldMapping>?)channelConfigs.FirstOrDefault(c => c.ChannelCode == code)?.SettlementFieldMappings.Values ?? [])
            .Where(m => !string.IsNullOrEmpty(m.Column))
            .Select(m => m.Column!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var headerScanStopwatch = Stopwatch.StartNew();
        var rawHeaders = rows
            .Where(r => r.RawValues != null)
            .SelectMany(r => r.RawValues!.Keys)
            .Distinct()
            .Where(h => !mappedHeaders.Contains(h))
            .ToList();
        headerScanStopwatch.Stop();
        DiagnosticsLogger.Log($"[SettlementForm] RebuildRawTailColumns: 원본헤더 {rawHeaders.Count}개 식별({rows.Count}행 스캔, {headerScanStopwatch.Elapsed.TotalSeconds:F2}s) — 열 추가 시작");

        var addColumnsStopwatch = Stopwatch.StartNew();
        foreach (var header in rawHeaders)
        {
            _settlementGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = $"Raw_{_settlementGrid.Columns.Count}",
                HeaderText = header,
                Tag = header,
                ReadOnly = true,
                Width = 150,
            });
        }
        DiagnosticsLogger.Log($"[SettlementForm] RebuildRawTailColumns: 열 {rawHeaders.Count}개 추가 완료 ({addColumnsStopwatch.Elapsed.TotalSeconds:F2}s)");
    }

    private void OnSettlementGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_settlementGrid.Columns[e.ColumnIndex].Tag is not string rawHeader) return;
        if (_settlementGrid.Rows[e.RowIndex].DataBoundItem is not SettlementData data) return;

        e.Value = data.RawValues?.GetValueOrDefault(rawHeader, string.Empty);
        e.FormattingApplied = true;
    }

    private void OnSummaryGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _summaryGrid.Rows.Count) return;

        var row = _summaryGrid.Rows[e.RowIndex];
        if (row.DataBoundItem is not ProfitGroupSummary summary) return;

        row.DefaultCellStyle.Font = summary.ProductGroup == TotalRowLabel
            ? new Font(_summaryGrid.Font, FontStyle.Bold)
            : _summaryGrid.Font;
    }

    private string BuildTotalsText(List<ProfitGroupSummary> groups, int unresolvedCount)
    {
        if (_settlementRows.Count == 0) return "전체 0건";

        var (shipmentCount, isEstimated) = ComputeActualShipmentCount();
        var shipmentNote = isEstimated ? "송장번호 없음 — 배송비÷3,000원으로 추정" : "송장번호 기준(중복 제외)";

        return $"전체 {_settlementRows.Count}건 (미매핑/확인필요 {unresolvedCount}건) | " +
               $"매출액 합계 {groups.Sum(g => g.Revenue):N0} | 수량 합계 {groups.Sum(g => g.Qty):N0}개 | " +
               $"순이익 합계 {groups.Sum(g => g.Profit):N0} | 배송비 합계 {groups.Sum(g => g.Shipping):N0} | 입출고비 합계 {groups.Sum(g => g.Fee):N0}\n" +
               $"실제발송송장수: {shipmentCount:N0}건 ({shipmentNote})";
    }

    private (int Count, bool IsEstimated) ComputeActualShipmentCount() => ShipmentCountEstimator.Compute(_settlementRows);

    /// <summary>
    /// SettlementData.ProductGroup은 SettlementLoader가 매핑 시점에 채워둔다(마스터SKU의
    /// 상품그룹). 미매핑/그룹 미지정 행은 요약에서 구분할 수 있게 라벨을 보정한다.
    /// </summary>
    private static string ResolveProductGroupLabel(SettlementData data)
    {
        if (string.IsNullOrWhiteSpace(data.Msku)) return "(미매핑)";
        return string.IsNullOrWhiteSpace(data.ProductGroup) ? "(미지정)" : data.ProductGroup!;
    }

    private async void OnLoadSettlementClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Multiselect = true,
            Title = "정산 파일을 선택하세요",
            InitialDirectory = _settingsService.GetLastFolder("SettlementLoad") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        using var channelDialog = new SelectChannelDialog();
        if (channelDialog.ShowDialog(this) != DialogResult.OK || channelDialog.SelectedChannel == null)
        {
            _statusLabel.Text = "채널이 선택되지 않아 작업을 취소했습니다.";
            return;
        }

        var channelConfig = _channelConfigService.Load().FirstOrDefault(c => c.ChannelCode == channelDialog.SelectedChannel.ChannelCode);
        if (channelConfig == null)
        {
            GuideToChannelConfig(channelDialog.SelectedChannel);
            return;
        }

        var skuMapper = new SkuMapper(_mappingRepository, channelConfig.ChannelCode);

        // 데이터가 많으면 로드가 수 초 이상 걸릴 수 있는데, 창은 떠 있지만 조작이 안 되니 멈춘
        // 것처럼 보일 수 있다는 피드백 — 진행 안내창을 띄워 "작업 중"임을 분명히 보여준다.
        // 비밀번호 입력 등 다른 모달 창이 이 창을 owner로 열리면 그 창이 닫힐 때 owner의 Enabled를
        // 강제로 되돌려놓으므로, 여기서는 Enabled를 건드리지 않고 진행 안내창 + 대기 커서만으로
        // "작업 중"을 표시한다.
        using var progressDialog = new LoadingProgressDialog($"'{channelDialog.SelectedChannel.ChannelName}' 채널의 정산 파일을 불러오는 중입니다...");
        progressDialog.Show(this);
        Cursor = Cursors.WaitCursor;
        _statusLabel.ForeColor = SystemColors.ControlText; // 이전 오류로 빨간 글씨가 남아있을 수 있어 매번 초기화한다.
        _statusLabel.Text = $"'{channelDialog.SelectedChannel.ChannelName}' 채널의 설정으로 정산 파일을 읽는 중입니다...";

        try
        {
            _settingsService.SetLastFolder("SettlementLoad", Path.GetDirectoryName(ofd.FileNames[0])!);
            DiagnosticsLogger.Log($"[SettlementForm] 정산파일 로드 시작 — 채널={channelConfig.ChannelCode}, 파일 {ofd.FileNames.Length}개: {string.Join(", ", ofd.FileNames.Select(Path.GetFileName))}");

            var loadStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < ofd.FileNames.Length; i++)
            {
                var file = ofd.FileNames[i];
                var fileName = Path.GetFileName(file);
                if (ofd.FileNames.Length > 1)
                {
                    progressDialog.SetProgress($"({i + 1}/{ofd.FileNames.Length}) {fileName} 처리 중...", i, ofd.FileNames.Length);
                }
                else
                {
                    progressDialog.SetIndeterminate($"{fileName} 처리 중...");
                }

                DiagnosticsLogger.Log($"[SettlementForm] ({i + 1}/{ofd.FileNames.Length}) '{fileName}' LoadSettlementFileWithPasswordRetryAsync 호출");
                var loadedRows = await LoadSettlementFileWithPasswordRetryAsync(skuMapper, channelConfig, file);
                if (loadedRows == null) continue; // 사용자가 비밀번호 입력을 취소함
                foreach (var row in loadedRows) _settlementRows.Add(row);
                DiagnosticsLogger.Log($"[SettlementForm] ({i + 1}/{ofd.FileNames.Length}) '{fileName}' 완료 — {loadedRows.Count}건 (총 누적 {_settlementRows.Count}건, {loadStopwatch.Elapsed.TotalSeconds:F2}s)");
            }
            loadStopwatch.Stop();
            DiagnosticsLogger.Log($"[SettlementForm] 전체 파일 처리 완료 ({loadStopwatch.Elapsed.TotalSeconds:F2}s) — RefreshProfitAnalysisView 호출 시작");

            progressDialog.SetIndeterminate("화면을 갱신하는 중입니다...");
            var refreshStopwatch = Stopwatch.StartNew();
            RefreshProfitAnalysisView();
            refreshStopwatch.Stop();

            var unresolvedCount = _settlementRows.Count(SettlementRowStatus.IsUnresolved);
            var diagnosticsText = $"파일처리 {loadStopwatch.Elapsed.TotalSeconds:F1}s / 화면갱신 {refreshStopwatch.Elapsed.TotalSeconds:F1}s ({LastRefreshDiagnostics})";
            _statusLabel.Text = $"총 {_settlementRows.Count}건의 정산 데이터가 로드되었습니다. (미매핑/확인필요 {unresolvedCount}건) [{diagnosticsText}]";

            Cursor = Cursors.Default;
            progressDialog.Close();
            DiagnosticsLogger.Log("[SettlementForm] progressDialog.Close() 완료");

            // 진단 결과(2026-06-28): progressDialog.Close() 직후 모달 MessageBox를 띄우면(순서를
            // 바꿔도, BeginInvoke로 다음 메시지 루프 틱에 미뤄도) 그 MessageBox 창이 생성은 되지만
            // (Win32 EnumWindows로 확인됨) Visible=False로 그려지지 않는 경쟁 상태가 이 환경에서
            // 반복 재현됐다. 모달이라 owner(이 창)는 비활성화된 채 안내창은 안 보이니, 사용자에게는
            // "데이터는 보이는데 클릭이 안 되는" 멈춘 상태로 보인다 — 두 차례의 타이밍 보정으로도
            // 못 고쳤으므로, 이 알림은 모달 없이 상태표시줄만으로 대체한다(미매핑 건수/내용은 이미
            // 목록 상단 정렬 + 하단 요약 패널에도 그대로 보이므로 정보 손실은 없다).
            if (unresolvedCount > 0)
            {
                _statusLabel.Text += $"  ⚠ 미매핑/확인필요 {unresolvedCount}건 — 목록 상단에 표시됨";
            }
            DiagnosticsLogger.Log("[SettlementForm] OnLoadSettlementClick try 블록 끝까지 도달");
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Log($"[SettlementForm] 예외 발생: {ex}");
            Cursor = Cursors.Default;
            progressDialog.Close();
            // 위와 같은 이유로 모달 MessageBox는 믿을 수 없으니, 상태표시줄에 오류 내용을 그대로
            // 남긴다(모달이 실제로는 떠도 안 보일 수 있어, 보이는 곳에 항상 기록해두는 쪽이 안전하다).
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = $"오류 발생: {ex.Message}";
            DiagnosticsLogger.Log($"[SettlementForm] catch 블록 처리 완료 — 상태표시줄에 오류 표시함");
        }
        finally
        {
            Cursor = Cursors.Default;
            progressDialog.Close();
        }
    }

    /// <summary>
    /// 파일이 암호로 보호되어 있으면 비밀번호를 물어보고 재시도합니다. 사용자가 취소하면 null을 반환합니다.
    /// </summary>
    private async Task<List<SettlementData>?> LoadSettlementFileWithPasswordRetryAsync(SkuMapper skuMapper, ChannelConfig channelConfig, string file)
    {
        try
        {
            return await _settlementLoader.LoadFromFileAsync(skuMapper, _itemRepository, channelConfig, file, channelSkuRepository: _channelSkuRepository);
        }
        catch (EncryptedExcelFileException)
        {
            using var dialog = new PasswordPromptDialog(Path.GetFileName(file));
            if (dialog.ShowDialog(this) != DialogResult.OK) return null;

            return await _settlementLoader.LoadFromFileAsync(skuMapper, _itemRepository, channelConfig, file, dialog.Password, _channelSkuRepository);
        }
    }

    /// <summary>
    /// 선택한 채널에 ChannelConfig가 없을 때 안내 후 채널 설정 창을 열어 해당 채널을 바로 보여줍니다.
    /// </summary>
    private void GuideToChannelConfig(SalesChannel channel)
    {
        MessageBox.Show(
            $"'{channel.ChannelName}' 채널의 설정이 없습니다.\n채널 설정 창에서 정산 파일을 읽는 방법을 먼저 설정해주세요.",
            "채널 설정 없음", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        var configForm = Application.OpenForms.OfType<ChannelConfigForm>().FirstOrDefault() ?? new ChannelConfigForm();
        if (!configForm.Visible) configForm.Show();
        configForm.BringToFront();
        configForm.SelectChannelByCode(channel.ChannelCode);
    }

    private async void OnSaveSettlementClick(object? sender, EventArgs e)
    {
        if (_settlementRows.Count == 0)
        {
            MessageBox.Show("저장할 이익분석 결과가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show($"{_settlementRows.Count}건의 이익분석 결과를 저장하시겠습니까?", "저장 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        Cursor = Cursors.WaitCursor;
        try
        {
            var rowsToSave = _settlementRows.ToList();
            await Task.Run(() => _settlementRepository.Insert(rowsToSave));
            _statusLabel.Text = $"{rowsToSave.Count}건 저장 완료.";

            // 99.1: 저장 시 분석 결과 요약을 별도로 보여준다(하단 상품그룹별 요약 그리드는 항상 떠 있음).
            // RefreshProfitAnalysisView()처럼 그리드를 크게 다시 그리는 호출 직후 모달을 띄우면 그
            // 모달이 안 보이게 생성되는 경쟁 상태가 반복 재현돼서(정산파일 로드 멈춤과 동일 원인),
            // 여기도 모달 대신 상태표시줄로 안내한다.
            RefreshProfitAnalysisView();
            _statusLabel.Text = $"{rowsToSave.Count}건 저장 완료. {_summaryTotalsLabel.Text}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 중 오류가 발생했습니다.\n{ex.Message}", "저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void OnExportSettlementClick(object? sender, EventArgs e)
    {
        if (_settlementRows.Count == 0)
        {
            MessageBox.Show("내보낼 이익분석 결과가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"이익분석_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("SettlementExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var filePath = sfd.FileName;
        _settingsService.SetLastFolder("SettlementExport", Path.GetDirectoryName(filePath)!);

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();

            WriteDetailSheet(package, "분석결과상세", _settlementRows);
            WriteSummarySheet(package.Workbook.Worksheets.Add("분석요약(상품그룹별)"));
            WriteDetailSheet(package, "미매핑·예외건", _settlementRows.Where(d => SettlementRowStatus.IsUnresolved(d) || SettlementRowStatus.IsExcludedByExceptionRule(d)).ToList());
            WriteRawDataSheet(package, "원본데이터");

            package.SaveAs(new FileInfo(filePath));

            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 내보내는 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static readonly string[] DetailHeaders = ["채널", "상품그룹", "상품명", "옵션명", "매핑SKU", "수량", "매출액", "정산액", "배송비", "입출고비", "이익액", "상태"];

    /// <summary>
    /// SalesManagerV2의 다중 시트 결과 저장 양식(상세/요약/미매핑/원본)을 참고해, "분석결과상세"와
    /// "미매핑·예외건" 두 시트에 공통으로 쓰는 표 형식.
    /// </summary>
    private static void WriteDetailSheet(ExcelPackage package, string sheetName, IReadOnlyList<SettlementData> rows)
    {
        var sheet = package.Workbook.Worksheets.Add(sheetName);
        for (int i = 0; i < DetailHeaders.Length; i++) sheet.Cells[1, i + 1].Value = DetailHeaders[i];

        int row = 2;
        foreach (var data in rows)
        {
            sheet.Cells[row, 1].Value = data.ChannelCode;
            sheet.Cells[row, 2].Value = ResolveProductGroupLabel(data);
            sheet.Cells[row, 3].Value = data.ProductName;
            sheet.Cells[row, 4].Value = data.OptionName;
            sheet.Cells[row, 5].Value = data.Msku;
            sheet.Cells[row, 6].Value = data.Qty;
            sheet.Cells[row, 7].Value = data.Revenue;
            sheet.Cells[row, 8].Value = data.Settlement;
            sheet.Cells[row, 9].Value = data.Shipping;
            sheet.Cells[row, 10].Value = data.Fee;
            sheet.Cells[row, 11].Value = data.Profit;
            sheet.Cells[row, 12].Value = data.Status;
            row++;
        }
        sheet.Cells.AutoFitColumns();
    }

    private void WriteSummarySheet(ExcelWorksheet sheet)
    {
        string[] headers = ["상품그룹", "건수", "수량", "매출액", "배송비", "입출고비", "순이익"];
        for (int i = 0; i < headers.Length; i++) sheet.Cells[1, i + 1].Value = headers[i];

        var groups = _settlementRows
            .GroupBy(ResolveProductGroupLabel)
            .Select(g => new ProfitGroupSummary
            {
                ProductGroup = g.Key,
                RowCount = g.Count(),
                Qty = g.Sum(x => x.Qty),
                Revenue = g.Sum(x => x.Revenue),
                Settlement = g.Sum(x => x.Settlement),
                Shipping = g.Sum(x => x.Shipping),
                Fee = g.Sum(x => x.Fee),
                Profit = g.Sum(x => x.Profit),
            })
            .OrderByDescending(s => s.Profit)
            .ToList();

        int row = 2;
        foreach (var g in groups)
        {
            sheet.Cells[row, 1].Value = g.ProductGroup;
            sheet.Cells[row, 2].Value = g.RowCount;
            sheet.Cells[row, 3].Value = g.Qty;
            sheet.Cells[row, 4].Value = g.Revenue;
            sheet.Cells[row, 5].Value = g.Shipping;
            sheet.Cells[row, 6].Value = g.Fee;
            sheet.Cells[row, 7].Value = g.Profit;
            row++;
        }

        sheet.Cells[row, 1].Value = TotalRowLabel;
        sheet.Cells[row, 2].Value = groups.Sum(g => g.RowCount);
        sheet.Cells[row, 3].Value = groups.Sum(g => g.Qty);
        sheet.Cells[row, 4].Value = groups.Sum(g => g.Revenue);
        sheet.Cells[row, 5].Value = groups.Sum(g => g.Shipping);
        sheet.Cells[row, 6].Value = groups.Sum(g => g.Fee);
        sheet.Cells[row, 7].Value = groups.Sum(g => g.Profit);
        sheet.Cells[row, 1, row, 7].Style.Font.Bold = true;

        var (shipmentCount, isEstimated) = ComputeActualShipmentCount();
        row += 2;
        sheet.Cells[row, 1].Value = "실제발송송장수";
        sheet.Cells[row, 2].Value = shipmentCount;
        sheet.Cells[row, 3].Value = isEstimated ? "송장번호 없음 — 배송비÷3,000원으로 추정" : "송장번호 기준(중복 제외)";

        sheet.Cells.AutoFitColumns();
    }

    /// <summary>
    /// 정산 파일에서 읽은 원본 행(SettlementLoader가 RawValues에 채워둔 헤더->값)을 그대로 출력한다.
    /// 파일마다 헤더 구성이 다를 수 있으므로, 전체 행에서 등장하는 모든 헤더의 합집합을 열로 쓴다.
    /// </summary>
    private void WriteRawDataSheet(ExcelPackage package, string sheetName)
    {
        var sheet = package.Workbook.Worksheets.Add(sheetName);
        var rowsWithRaw = _settlementRows.Where(d => d.RawValues is { Count: > 0 }).ToList();
        if (rowsWithRaw.Count == 0)
        {
            sheet.Cells[1, 1].Value = "원본 데이터가 없습니다.";
            return;
        }

        var headers = rowsWithRaw.SelectMany(d => d.RawValues!.Keys).Distinct().ToList();
        for (int i = 0; i < headers.Count; i++) sheet.Cells[1, i + 1].Value = headers[i];

        int row = 2;
        foreach (var data in rowsWithRaw)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                sheet.Cells[row, i + 1].Value = data.RawValues!.GetValueOrDefault(headers[i], string.Empty);
            }
            row++;
        }
        sheet.Cells.AutoFitColumns();
    }

    private void OnSettlementGridRowPrePaint(object? sender, DataGridViewRowPrePaintEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _settlementGrid.Rows.Count) return;

        var row = _settlementGrid.Rows[e.RowIndex];
        if (row.DataBoundItem is not SettlementData data) return;

        if (string.IsNullOrWhiteSpace(data.Msku) || data.Status == "원가 정보 없음")
        {
            // 다크모드에서 기본 글자색이 흰색으로 바뀌어도 강조 배경에서 글자가 보이도록 검은색으로 고정한다.
            row.DefaultCellStyle.BackColor = Color.MistyRose;
            row.DefaultCellStyle.ForeColor = Color.Black;
        }
        else
        {
            row.DefaultCellStyle.BackColor = _settlementGrid.DefaultCellStyle.BackColor;
            row.DefaultCellStyle.ForeColor = _settlementGrid.DefaultCellStyle.ForeColor;
        }
    }

    // ===================== 미매핑 행 즉석 매핑(1:1 자동완성 + 우클릭 메뉴) =====================

    private string? _msEditOriginalValue;

    /// <summary>
    /// "매핑 SKU" 셀 편집을 시작할 때, 마스터SKU/현재 채널의 CSKU 코드 목록으로 자동완성을 단다.
    /// 사용자가 요청한 안전장치: 자동완성 목록에 있는 코드를 그대로 입력해야만(대소문자 무관 완전
    /// 일치) 1:1 규칙으로 확정되고, 그 외 임의 문자열은 OnSettlementGridCellValidating에서 막는다.
    /// </summary>
    private void OnSettlementGridEditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (_settlementGrid.CurrentCell?.OwningColumn?.Name != "Msku" || e.Control is not TextBox textBox) return;

        if (_settlementGrid.CurrentRow?.DataBoundItem is SettlementData data)
        {
            _msEditOriginalValue = data.Msku;
        }

        var codes = BuildSkuAutoCompleteSource((_settlementGrid.CurrentRow?.DataBoundItem as SettlementData)?.ChannelCode);
        var source = new AutoCompleteStringCollection();
        source.AddRange(codes.ToArray());

        textBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        textBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        textBox.AutoCompleteCustomSource = source;
    }

    /// <summary>등록된 마스터SKU 코드 + 현재 채널의 CSKU 코드를 합친, 중복 제거된 자동완성 후보 목록.</summary>
    private List<string> BuildSkuAutoCompleteSource(string? channelCode)
    {
        var codes = _itemRepository.GetAll().Select(i => i.Sku).ToList();
        if (!string.IsNullOrEmpty(channelCode))
        {
            codes.AddRange(_channelSkuRepository.GetAllByChannel(channelCode).Select(c => c.CskuCode));
        }
        return codes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 빈 값은 허용(매핑 해제 의도)하고, 값이 있으면 등록된 SKU/CSKU 코드와 완전히 일치할 때만
    /// 커밋을 허용한다. 오타로 잘못된 1:1 규칙이 즉시 저장되는 걸 막기 위한 안전장치.
    /// </summary>
    private void OnSettlementGridCellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_settlementGrid.Columns[e.ColumnIndex].Name != "Msku") return;

        var text = e.FormattedValue?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return; // 비우는 것은 허용 — CellEndEdit에서 매핑 해제 처리.

        var channelCode = (_settlementGrid.Rows[e.RowIndex].DataBoundItem as SettlementData)?.ChannelCode;
        var candidates = BuildSkuAutoCompleteSource(channelCode);
        if (!candidates.Contains(text.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            _statusLabel.Text = $"'{text}'는 등록된 SKU/CSKU 코드가 아닙니다 — 자동완성 목록에서 선택해주세요.";
            e.Cancel = true;
        }
    }

    /// <summary>
    /// 검증을 통과한 "매핑 SKU" 입력을 1:1 규칙으로 저장하고, 그 행의 매핑/이익을 즉시 다시
    /// 계산한다(이미 같은 키로 저장된 규칙이 있으면 덮어쓴다 — UpsertExactRule은 멱등).
    /// 값을 비우면 매핑을 해제한다(규칙 자체를 삭제하지는 않음 — 다른 행에 영향 줄 수 있어서).
    /// </summary>
    private void OnSettlementGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_settlementGrid.Columns[e.ColumnIndex].Name != "Msku") return;
        if (_settlementGrid.Rows[e.RowIndex].DataBoundItem is not SettlementData data) return;

        if (string.IsNullOrWhiteSpace(data.Msku))
        {
            data.Status = "매핑 실패";
            data.Profit = 0m;
            RefreshProfitAnalysisView();
            return;
        }

        if (string.Equals(data.Msku, _msEditOriginalValue, StringComparison.OrdinalIgnoreCase)) return; // 변경 없음.
        if (string.IsNullOrEmpty(data.ChannelCode)) return;

        var key = BuildExactMappingKey(data);
        if (string.IsNullOrWhiteSpace(key)) return;

        _mappingRepository.UpsertExactRule(data.ChannelCode, key, data.Msku.Trim());
        ReapplyMappingAndProfit(data);
        _statusLabel.Text = $"'{data.ProductName} {data.OptionName}' → '{data.Msku}' 1:1 매핑 규칙을 저장하고 적용했습니다.";
    }

    /// <summary>SkuMapper.ApplyMapping과 동일한 1:1 매핑 키(상품명+옵션명, 구분자 없음) 형식.</summary>
    private static string BuildExactMappingKey(SettlementData data) => (data.ProductName ?? "") + (data.OptionName ?? "");

    /// <summary>
    /// 채널/매핑 규칙이 바뀐 뒤 한 행만 다시 매핑·손익 계산한다. 정산파일을 다시 불러오지 않고도
    /// 즉석 매핑 결과가 바로 반영되도록 SettlementLoader의 행 단위 계산 로직을 재사용한다.
    /// </summary>
    private void ReapplyMappingAndProfit(SettlementData data)
    {
        if (string.IsNullOrEmpty(data.ChannelCode)) return;
        var channelConfig = _channelConfigService.Load().FirstOrDefault(c => c.ChannelCode == data.ChannelCode);
        if (channelConfig == null) return;

        var skuMapper = new SkuMapper(_mappingRepository, data.ChannelCode, _channelSkuRepository);
        SettlementLoader.ApplyMappingAndProfit(data, skuMapper, _itemRepository, channelConfig, _channelSkuRepository);
        RefreshProfitAnalysisView();
    }

    /// <summary>
    /// 매핑관리창(MappingForm.MappingRulesChanged)에서 규칙이 바뀌었다는 신호를 받았을 때, 지금
    /// 로드돼 있는 정산 데이터 전체를 다시 매핑·손익 계산한다. "정산파일 로드"를 다시 실행하지
    /// 않아도(엑셀을 다시 읽지 않음) 미매핑 목록이 즉시 갱신되도록 채널별로 SkuMapper를 한 번씩만
    /// 만들어 재사용한다.
    /// </summary>
    private void ReapplyMappingForAllRows()
    {
        if (_settlementRows.Count == 0) return;

        var channelConfigs = _channelConfigService.Load();
        var mapperCache = new Dictionary<string, (SkuMapper Mapper, ChannelConfig Config)>();

        foreach (var data in _settlementRows)
        {
            if (string.IsNullOrEmpty(data.ChannelCode)) continue;

            if (!mapperCache.TryGetValue(data.ChannelCode, out var entry))
            {
                var channelConfig = channelConfigs.FirstOrDefault(c => c.ChannelCode == data.ChannelCode);
                if (channelConfig == null) continue;

                entry = (new SkuMapper(_mappingRepository, data.ChannelCode, _channelSkuRepository), channelConfig);
                mapperCache[data.ChannelCode] = entry;
            }

            SettlementLoader.ApplyMappingAndProfit(data, entry.Mapper, _itemRepository, entry.Config, _channelSkuRepository);
        }

        RefreshProfitAnalysisView();
        _statusLabel.Text = $"매핑관리창에서 변경된 규칙을 반영해 미매핑 목록을 갱신했습니다. ({DateTime.Now:HH:mm:ss})";
    }

    /// <summary>
    /// 미매핑 행에서 곧바로 매핑을 실행할 수 있는 우클릭 메뉴(1:1/조건부/임시/예외처리). 기본
    /// 복사/붙여넣기 항목보다 앞에 끼워넣어, 그리드의 동적 "이 창의 기능" 메뉴 갱신 로직이
    /// 이 항목들을 지우지 않도록 한다(ExcelLikeDataGridView.OnContextMenuOpening 참고).
    /// </summary>
    private void SetupSettlementGridContextMenu()
    {
        var menu = _settlementGrid.ContextMenuStrip!;
        menu.Items.Insert(0, new ToolStripSeparator());
        menu.Items.Insert(0, new ToolStripMenuItem("이 행 예외처리(매핑 제외)", null, OnExcludeSettlementRowClick));
        menu.Items.Insert(0, new ToolStripMenuItem("임시 매핑으로 등록", null, OnAddTempRuleFromSettlementRowClick));
        menu.Items.Insert(0, new ToolStripMenuItem("조건부 매핑 규칙 추가", null, OnAddConditionRuleFromSettlementRowClick));
        menu.Items.Insert(0, new ToolStripMenuItem("SKU 매핑 도우미", null, OnOpenMappingHelperFromSettlementRowClick));
    }

    private SettlementData? GetSelectedSettlementRow()
    {
        if (_settlementGrid.CurrentRow?.DataBoundItem is not SettlementData data) return null;
        if (string.IsNullOrEmpty(data.ChannelCode))
        {
            MessageBox.Show("이 행에는 채널 정보가 없어 매핑을 실행할 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        return data;
    }

    /// <summary>CSKU/납품가/송장표시명까지 한 번에 설정하거나 임시SKU를 새로 등록할 수 있는 기존 도우미를 재사용한다.</summary>
    private void OnOpenMappingHelperFromSettlementRowClick(object? sender, EventArgs e)
    {
        var data = GetSelectedSettlementRow();
        if (data == null) return;

        var syntheticItem = new OfsOrderItem { ProductName = data.ProductName, OptionName = data.OptionName, Quantity = data.Qty };
        using var dialog = new OrderSkuMappingDialog(syntheticItem, data.ChannelCode);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        ReapplyMappingAndProfit(data);
    }

    /// <summary>
    /// 매핑관리창을 열어 이 행의 상품명/옵션명을 조건으로 채운 새 조건부 매핑 규칙을 만든다.
    /// 다른 창을 새로 띄운(또는 닫은) 직후 같은 틱에서 MessageBox 같은 모달을 띄우면 그 모달이
    /// Visible=False로 생성되는 경쟁 상태가 이 환경에서 반복 재현됐다(정산파일 로드 멈춤 신고와
    /// 동일 원인). 안내는 모달 대신 이 창의 상태표시줄로 대체한다.
    /// </summary>
    private void OnAddConditionRuleFromSettlementRowClick(object? sender, EventArgs e)
    {
        var data = GetSelectedSettlementRow();
        if (data == null) return;

        var mappingForm = Application.OpenForms.OfType<MappingForm>().FirstOrDefault() ?? new MappingForm();
        if (!mappingForm.Visible) mappingForm.Show();
        mappingForm.BringToFront();

        // 매핑관리창에서 규칙을 저장할 때마다 이 창의 미매핑 목록을 자동으로 다시 매핑한다(정산파일을
        // 다시 읽지 않고도 즉시 반영). 같은 매핑관리창 인스턴스에 중복 구독되지 않도록 한 번만 건다.
        if (!ReferenceEquals(_subscribedMappingForm, mappingForm))
        {
            if (_subscribedMappingForm != null) _subscribedMappingForm.MappingRulesChanged -= ReapplyMappingForAllRows;
            mappingForm.MappingRulesChanged += ReapplyMappingForAllRows;
            mappingForm.FormClosed += (_, _) =>
            {
                if (ReferenceEquals(_subscribedMappingForm, mappingForm)) _subscribedMappingForm = null;
            };
            _subscribedMappingForm = mappingForm;
        }

        // 발주서가 없어도(보통 마감/이익분석은 OFS를 거치지 않음) "예상 매칭 건수"를 정산파일
        // 기준으로 미리볼 수 있게 지금 로드된 정산 데이터를 넘긴다(참조 그대로 — 나중에 다시
        // 매핑하거나 정산파일을 새로 불러와도 항상 최신 상태로 반영됨).
        mappingForm.SetSettlementPreviewData(_settlementRows);
        mappingForm.StartNewConditionRuleFor(data.ChannelCode!, data.ProductName, data.OptionName);

        _statusLabel.Text = "매핑관리창에 새 조건부 매핑 규칙을 만들었습니다. 거기서 대상 SKU/CSKU와 조건을 마무리해 저장하면 이 목록에 자동으로 반영됩니다.";
    }

    /// <summary>정확히 같은 (상품명+옵션명) 키에만 매칭되는 임시 매핑 규칙으로 등록한다.</summary>
    private void OnAddTempRuleFromSettlementRowClick(object? sender, EventArgs e)
    {
        var data = GetSelectedSettlementRow();
        if (data == null) return;

        using var dialog = new TextPromptDialog("임시 매핑으로 등록", $"'{data.ProductName} {data.OptionName}'을 매핑할 SKU/CSKU 코드를 입력하세요:");
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value)) return;

        var key = BuildExactMappingKey(data);
        _mappingRepository.UpsertRule(MappingRuleType.Temp, data.ChannelCode!, key, dialog.Value);
        ReapplyMappingAndProfit(data);
        // 다이얼로그를 닫은 직후 모달을 띄우면 안 보이게 생성되는 같은 경쟁 상태를 피하려고
        // MessageBox 대신 상태표시줄로 안내한다.
        _statusLabel.Text = $"'{data.ProductName} {data.OptionName}' → '{dialog.Value}' 임시 매핑으로 등록하고 적용했습니다.";
    }

    /// <summary>이 (상품명+옵션명) 조합을 앞으로 계속 매핑 대상에서 제외하는 예외 규칙으로 저장한다(배송비/수수료 안내 행 등).</summary>
    private void OnExcludeSettlementRowClick(object? sender, EventArgs e)
    {
        var data = GetSelectedSettlementRow();
        if (data == null) return;

        var confirm = MessageBox.Show(
            $"'{data.ProductName} {data.OptionName}' 조합을 앞으로 매핑 대상에서 제외(예외처리)하시겠습니까?\n실제 상품이 아닌 배송비/수수료 안내 행 등에 사용하세요.",
            "예외처리 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        var key = BuildExactMappingKey(data);
        _mappingRepository.UpsertRule(MappingRuleType.Exception, data.ChannelCode!, key, SkuMapper.ExcludedTargetSku);
        ReapplyMappingAndProfit(data);
    }

    // ===================== 마감 대조(수기) =====================

    private TabPage CreateReconciliationTab()
    {
        var tabPage = new TabPage("마감 대조(수기)");

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        _reconcileChannelComboBox = new ComboBox { Size = new Size(160, 25), DropDownStyle = ComboBoxStyle.DropDownList };
        _reconcileChannelComboBox.DataSource = _salesChannelRepository.GetAll();
        _reconcileChannelComboBox.DisplayMember = "ChannelName";
        _reconcileChannelComboBox.ValueMember = "ChannelCode";

        _fromDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), Width = 100 };
        _toDatePicker = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = DateTime.Today, Width = 100 };

        var btnLoadOutbound = new Button { Text = "출고내역 조회", Size = new Size(110, 30) };
        var btnLoadStatement = new Button { Text = "거래처 마감내역 불러오기", Size = new Size(170, 30) };
        var btnExportOutbound = new Button { Text = "출고내역 엑셀로 내보내기", Size = new Size(170, 30) };

        btnLoadOutbound.Click += OnLoadOutboundClick;
        btnLoadStatement.Click += OnLoadStatementClick;
        btnExportOutbound.Click += OnExportOutboundClick;

        toolStrip.Controls.Add(new Label { Text = "채널:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        toolStrip.Controls.Add(_reconcileChannelComboBox);
        toolStrip.Controls.Add(new Label { Text = "기간:", AutoSize = true, Padding = new Padding(8, 5, 2, 0) });
        toolStrip.Controls.Add(_fromDatePicker);
        toolStrip.Controls.Add(new Label { Text = "~", AutoSize = true, Padding = new Padding(2, 5, 2, 0) });
        toolStrip.Controls.Add(_toDatePicker);
        toolStrip.Controls.Add(btnLoadOutbound);
        toolStrip.Controls.Add(btnExportOutbound);
        toolStrip.Controls.Add(btnLoadStatement);
        toolStrip.Controls.Add(new Label
        {
            Text = "운송장번호 등록/발송확인 처리는 OFS의 \"발주/출고 이력\" 창으로 옮겼습니다.",
            AutoSize = true,
            Padding = new Padding(15, 6, 0, 0),
            ForeColor = Color.DimGray,
        });

        var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };

        _outboundGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "SettlementForm.OutboundGrid",
            AutoGenerateColumns = false,
        };
        _outboundGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "주문번호", Name = "OrderNo", DataPropertyName = "OrderNo", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "운송장번호", Name = "TrackingNo", DataPropertyName = "TrackingNo", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "SKU", Name = "MskuCode", DataPropertyName = "MskuCode", Width = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "납품가", Name = "SupplyPrice", DataPropertyName = "SupplyPrice", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "출고일시", Name = "CreatedAt", DataPropertyName = "CreatedAt", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "발주이력 상태", Name = "Status", DataPropertyName = "Status", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "확정일시", Name = "ConfirmedAt", DataPropertyName = "ConfirmedAt", Width = 130 }
        );

        _statementGrid = new ExcelLikeDataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = true, AllowUserToAddRows = false, ReadOnly = true };

        var outboundPanel = new Panel { Dock = DockStyle.Fill };
        outboundPanel.Controls.Add(WithGroupLabel("출고내역(시스템)", _outboundGrid));
        var statementPanel = new Panel { Dock = DockStyle.Fill };
        statementPanel.Controls.Add(WithGroupLabel("거래처 마감내역(외부 파일)", _statementGrid));

        splitContainer.Panel1.Controls.Add(outboundPanel);
        splitContainer.Panel2.Controls.Add(statementPanel);

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(splitContainer, 0, 1);

        tabPage.Controls.Add(mainLayout);
        return tabPage;
    }

    private static Control WithGroupLabel(string title, Control content)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = new Font(Control.DefaultFont, FontStyle.Bold) }, 0, 0);
        content.Dock = DockStyle.Fill;
        layout.Controls.Add(content, 0, 1);
        return layout;
    }

    private void OnLoadOutboundClick(object? sender, EventArgs e)
    {
        var channelCode = _reconcileChannelComboBox.SelectedValue as string;
        if (string.IsNullOrEmpty(channelCode))
        {
            MessageBox.Show("조회할 채널을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var details = _outboundRepository.GetByChannel(channelCode, _fromDatePicker.Value.Date, _toDatePicker.Value.Date.AddDays(1).AddTicks(-1));
        _outboundGrid.DataSource = new BindingList<OutboundDetail>(details);
        _statusLabel.Text = $"출고내역 {details.Count}건 조회됨.";
    }

    private void OnExportOutboundClick(object? sender, EventArgs e)
    {
        if (_outboundGrid.DataSource is not BindingList<OutboundDetail> details || details.Count == 0)
        {
            MessageBox.Show("내보낼 출고내역이 없습니다. 먼저 조회하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"출고내역_{DateTime.Now:yyyyMMdd}.xlsx",
            InitialDirectory = _settingsService.GetLastFolder("OutboundExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        var filePath = sfd.FileName;
        _settingsService.SetLastFolder("OutboundExport", Path.GetDirectoryName(filePath)!);

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("출고내역");

            string[] headers = ["주문번호", "운송장번호", "SKU", "수량", "납품가", "출고일시"];
            for (int i = 0; i < headers.Length; i++) sheet.Cells[1, i + 1].Value = headers[i];

            int row = 2;
            foreach (var detail in details)
            {
                sheet.Cells[row, 1].Value = detail.OrderNo;
                sheet.Cells[row, 2].Value = detail.TrackingNo;
                sheet.Cells[row, 3].Value = detail.MskuCode;
                sheet.Cells[row, 4].Value = detail.Qty;
                sheet.Cells[row, 5].Value = detail.SupplyPrice;
                sheet.Cells[row, 6].Value = detail.CreatedAt;
                row++;
            }

            sheet.Cells.AutoFitColumns();
            package.SaveAs(new FileInfo(filePath));

            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 내보내는 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "내보내기 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnLoadStatementClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title = "거래처 제공 마감내역 파일을 선택하세요",
            InitialDirectory = _settingsService.GetLastFolder("StatementLoad") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _settingsService.SetLastFolder("StatementLoad", Path.GetDirectoryName(ofd.FileName)!);

            using var package = ExcelFileOpener.OpenWithPasswordPrompt(ofd.FileName, this);
            if (package == null) return;

            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet?.Dimension == null)
            {
                MessageBox.Show("엑셀 파일에서 데이터를 찾을 수 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var table = new DataTable();
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[1, col].Value?.ToString() ?? $"열{col}";
                table.Columns.Add(header);
            }

            for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
            {
                var dataRow = table.NewRow();
                for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
                {
                    dataRow[col - 1] = worksheet.Cells[row, col].Value?.ToString() ?? string.Empty;
                }
                table.Rows.Add(dataRow);
            }

            _statementGrid.DataSource = table;
            _statusLabel.Text = $"거래처 마감내역 {table.Rows.Count}건을 불러왔습니다. 좌측 출고내역과 수기로 대조하세요.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "로드 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

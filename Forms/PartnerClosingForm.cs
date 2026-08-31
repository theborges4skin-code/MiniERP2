using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감보드(거래처마감보드_개발기획서.md). 채널 경유 거래는 OutboundDetailTable에서
/// 자동 집계하고, 미경유 거래처는 수동으로 금액을 입력해 같은 화면에서 함께 대조·확정한다.
/// 기존 `MonthlyClosingForm`(정산파일 폴더 자동화)과는 목적이 달라 별도 창으로 유지한다.
/// </summary>
public class PartnerClosingForm : Form
{
    private readonly PartnerClosingRepository _closingRepo = new();
    private readonly PartnerMasterRepository _masterRepo = new();
    private readonly SalesChannelRepository _channelRepo = new();
    private readonly DocPartyRepository _docPartyRepo = new();
    private readonly DocHistoryRepository _docHistoryRepo = new();
    private readonly OutboundRepository _outboundRepo = new();
    private readonly ChannelSkuRepository _channelSkuRepo = new();

    private ComboBox _periodCombo = new();
    private CheckBox _includeAllCheck = new();
    private CheckBox _vatExcludedCheck = new();
    private Label _statusSummaryLabel = new();
    private Label _statusLabel = new();

    private ExcelLikeDataGridView _partyGrid = new();
    private ExcelLikeDataGridView _lineGrid = new();

    private Dictionary<string, string> _channelNames = new();

    public PartnerClosingForm()
    {
        InitializeComponent();
        Load += (s, e) => RefreshBoard();
    }

    private void InitializeComponent()
    {
        Text = "거래처 마감보드";
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        mainLayout.Controls.Add(BuildToolbar(), 0, 0);
        mainLayout.Controls.Add(BuildBody(), 0, 1);

        _statusLabel = new Label { Dock = DockStyle.Fill, Text = "조회 중...", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };
        mainLayout.Controls.Add(_statusLabel, 0, 2);

        Controls.Add(mainLayout);
    }

    private Control BuildToolbar()
    {
        // 버튼이 계속 늘어 한 줄로는 창 너비를 넘어서므로(엑셀 일괄 추가 추가 시점) 조회/입력 계열과
        // 출력/기타 계열 두 줄로 나눈다(사용자 요청).
        var container = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var row1 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 4, 5, 0) };
        var row2 = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 0, 5, 4) };

        _periodCombo = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDown };
        var now = DateTime.Today;
        for (var i = 0; i < 24; i++) _periodCombo.Items.Add(now.AddMonths(-i).ToString("yyyy-MM"));
        _periodCombo.Text = now.ToString("yyyy-MM");
        _periodCombo.SelectedIndexChanged += (s, e) => RefreshBoard();

        var btnRefresh = new Button { Text = "새로고침", Size = new Size(80, 28) };
        btnRefresh.Click += (s, e) => RefreshBoard();

        _includeAllCheck = new CheckBox { Text = "전체 거래처 보기", AutoSize = true, Padding = new Padding(6, 5, 0, 0) };
        _includeAllCheck.CheckedChanged += (s, e) => RefreshBoard();

        var btnAddManual = new Button { Text = "수동 거래처 추가", Size = new Size(120, 28) };
        btnAddManual.Click += OnAddManualPartnerClick;

        var btnManualEntry = new Button { Text = "금액입력/비고", Size = new Size(100, 28) };
        btnManualEntry.Click += OnManualEntryClick;

        var btnAddManualOrder = new Button { Text = "수동 주문 추가", Size = new Size(110, 28) };
        btnAddManualOrder.Click += OnAddManualOrderClick;

        var btnBulkImport = new Button { Text = "엑셀 일괄 추가", Size = new Size(100, 28) };
        btnBulkImport.Click += OnBulkImportClick;

        var btnConfirm = new Button { Text = "마감확정", Size = new Size(80, 28) };
        btnConfirm.Click += OnConfirmClick;

        var btnCancelClosing = new Button { Text = "확정취소", Size = new Size(80, 28) };
        btnCancelClosing.Click += OnCancelClosingClick;

        // 명세표/매출장 공통 VAT 기준 — 이전에는 미리보기/발행마다 매번 물었으나(명세표만, 매출장은
        // 항상 VAT별도로 고정), 한 번 골라두면 계속 유지되는 체크박스로 바꿨다(사용자 요청).
        _vatExcludedCheck = new CheckBox { Text = "VAT 별도", AutoSize = true, Checked = true, Padding = new Padding(10, 5, 0, 0) };
        // 명세표/매출장 발행뿐 아니라 좌측 거래처 목록(공급가/이익)과 우측 라인 상세(단가/원가/이익)도
        // 이 체크박스 기준으로 즉시 다시 보여준다(사용자 요청). 전체 재조회(RefreshBoard) 대신
        // 그리드만 다시 그려서, 토글해도 현재 선택된 거래처가 풀리지 않게 한다.
        _vatExcludedCheck.CheckedChanged += (s, e) => { _partyGrid.Invalidate(); LoadLineGrid(); };

        var btnPreviewStatement = new Button { Text = "명세표 미리보기", Size = new Size(100, 28) };
        btnPreviewStatement.Click += (s, e) => OnPreviewClick(isLedger: false);

        var btnPreviewLedger = new Button { Text = "매출장 미리보기", Size = new Size(100, 28) };
        btnPreviewLedger.Click += (s, e) => OnPreviewClick(isLedger: true);

        var btnPublishStatement = new Button { Text = "명세표 발행", Size = new Size(90, 28) };
        btnPublishStatement.Click += (s, e) => OnPublishClick(isLedger: false);

        var btnPublishLedger = new Button { Text = "매출장 발행", Size = new Size(90, 28) };
        btnPublishLedger.Click += (s, e) => OnPublishClick(isLedger: true);

        var btnExportBoard = new Button { Text = "현황판 엑셀저장", Size = new Size(110, 28) };
        btnExportBoard.Click += OnExportBoardClick;

        // 샘플발송이력관리_개발기획서.md §6.2: 마감 집계에서 빠지는 샘플·CS 발송을 별도 화면에서
        // 추적한다. 기존 그리드를 토글로 재활용하지 않는 이유는 그 다이얼로그 클래스 주석 참고.
        var btnNonSale = new Button { Text = "비매출 내역", Size = new Size(90, 28) };
        btnNonSale.Click += (s, e) =>
        {
            using var dialog = new NonSaleClosingDialog(CurrentPeriod);
            FormManager.ShowDialogSafe(dialog, this);
        };

        _statusSummaryLabel = new Label { AutoSize = true, Padding = new Padding(10, 6, 0, 0), Text = "상태요약: -" };

        row1.Controls.Add(new Label { Text = "기간:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        row1.Controls.Add(_periodCombo);
        row1.Controls.Add(btnRefresh);
        row1.Controls.Add(_includeAllCheck);
        row1.Controls.Add(btnAddManual);
        row1.Controls.Add(btnManualEntry);
        row1.Controls.Add(btnAddManualOrder);
        row1.Controls.Add(btnBulkImport);
        row1.Controls.Add(btnConfirm);
        row1.Controls.Add(btnCancelClosing);

        row2.Controls.Add(_vatExcludedCheck);
        row2.Controls.Add(btnPreviewStatement);
        row2.Controls.Add(btnPreviewLedger);
        row2.Controls.Add(btnPublishStatement);
        row2.Controls.Add(btnPublishLedger);
        row2.Controls.Add(btnExportBoard);
        row2.Controls.Add(btnNonSale);
        row2.Controls.Add(_statusSummaryLabel);

        container.Controls.Add(row1, 0, 0);
        container.Controls.Add(row2, 0, 1);
        return container;
    }

    private Control BuildBody()
    {
        var split = new PersistentSplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            PersistenceKey = "PartnerClosingForm.MainSplit",
        };

        var leftGroup = new GroupBox { Text = "거래처 목록 (★즐겨찾기 → 최근 3개월 활동 → 전체보기 시 전체)", Dock = DockStyle.Fill };
        _partyGrid = BuildPartyGrid();
        leftGroup.Controls.Add(_partyGrid);

        var rightGroup = new GroupBox { Text = "선택 거래처 라인 상세", Dock = DockStyle.Fill };
        _lineGrid = BuildLineGrid();
        rightGroup.Controls.Add(_lineGrid);

        split.Panel1.Controls.Add(leftGroup);
        split.Panel2.Controls.Add(rightGroup);

        split.HandleCreated += (s, e) =>
        {
            if (split.Width > 0) split.SplitterDistance = (int)(split.Width * 0.35);
        };

        return split;
    }

    private ExcelLikeDataGridView BuildPartyGrid()
    {
        var grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "PartnerClosingForm.PartyGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
        };
        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "★", Name = "FavoriteMark", DataPropertyName = "FavoriteMark", Width = 26, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } },
            new DataGridViewTextBoxColumn { HeaderText = "거래처", Name = "PartyName", DataPropertyName = "PartyName", Width = 150 },
            new DataGridViewTextBoxColumn { HeaderText = "출고건", Name = "OutboundCount", DataPropertyName = "OutboundCount", Width = 55, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "TotalQty", DataPropertyName = "TotalQty", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "공급가합", Name = "TotalSupply", DataPropertyName = "TotalSupply", Width = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "이익", Name = "TotalProfit", DataPropertyName = "TotalProfit", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "상태", Name = "Status", DataPropertyName = "Status", Width = 70 },
            new DataGridViewTextBoxColumn { HeaderText = "미출고건", Name = "UnshippedCount", DataPropertyName = "UnshippedCount", Width = 60, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "경고", Name = "Warning", DataPropertyName = "Warning", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.DarkOrange } },
            new DataGridViewTextBoxColumn { HeaderText = "비고", Name = "ReconcileNote", DataPropertyName = "ReconcileNote", Width = 150, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        grid.SelectionChanged += (s, e) => LoadLineGrid();
        grid.CellFormatting += OnPartyGridCellFormatting;

        var menu = new ContextMenuStrip();
        var favoriteItem = new ToolStripMenuItem("즐겨찾기 켜기/끄기");
        favoriteItem.Click += OnToggleFavoriteClick;
        menu.Items.Add(favoriteItem);
        var openHistoryItem = new ToolStripMenuItem("출고이력 관리창에서 열기");
        openHistoryItem.Click += OnOpenPartyInHistoryClick;
        menu.Items.Add(openHistoryItem);
        grid.ContextMenuStrip = menu;

        return grid;
    }

    /// <summary>
    /// 거래처 목록에서 우클릭 시, 그 채널·현재 조회 중인 기간으로 미리 필터링해서 출고이력
    /// 관리창을 연다(수동 거래처거나 선택이 애매하면 빈 창으로 연다).
    /// </summary>
    private void OnOpenPartyInHistoryClick(object? sender, EventArgs e)
    {
        var selected = SelectedPartyRows();
        if (selected.Count != 1 || selected[0].IsManual)
        {
            FormManager.Show<OutboundHistoryForm>();
            return;
        }

        var row = selected[0];
        var channelCode = row.PartyKey["CH:".Length..];
        var periodStart = DateTime.ParseExact(CurrentPeriod, "yyyy-MM", CultureInfo.InvariantCulture);
        var form = new OutboundHistoryForm(null, channelCode, null, periodStart, periodStart.AddMonths(1).AddDays(-1));
        FormManager.ApplyBoundsTracking(form);
        form.Show();
    }

    private ExcelLikeDataGridView BuildLineGrid()
    {
        var grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "PartnerClosingForm.LineGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
        };
        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "일자", Name = "LineDateText", DataPropertyName = "LineDateText", Width = 90, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "상태", Name = "StatusText", DataPropertyName = "StatusText", Width = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "CskuCode", DataPropertyName = "CskuCode", Width = 100, ReadOnly = true },
            // 마감 대조 중 CSKU 미등록 등으로 품목명이 비어있는 경우가 있어(§1) 여기서 바로 고칠 수
            // 있게 편집을 허용한다. 단가도 마찬가지 이유로 편집 허용(HandleUnitPriceEdit 참고).
            new DataGridViewTextBoxColumn { HeaderText = "품목", Name = "ItemName", DataPropertyName = "ItemName", Width = 150 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 55, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "단가", Name = "UnitPrice", DataPropertyName = "UnitPrice", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "원가", Name = "CostPrice", DataPropertyName = "CostPrice", Width = 80, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "이익", Name = "Profit", DataPropertyName = "Profit", Width = 80, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );
        grid.CellEndEdit += OnLineGridCellEndEdit;
        grid.CellFormatting += OnLineGridCellFormatting;

        var menu = new ContextMenuStrip();
        var reassignItem = new ToolStripMenuItem("귀속월 변경");
        reassignItem.Click += OnReassignPeriodClick;
        menu.Items.Add(reassignItem);
        var markShippedItem = new ToolStripMenuItem("출고확정 처리");
        markShippedItem.Click += OnMarkLinesShippedClick;
        menu.Items.Add(markShippedItem);
        var deleteItem = new ToolStripMenuItem("선택 라인 삭제");
        deleteItem.Click += OnDeleteLinesClick;
        menu.Items.Add(deleteItem);
        var openHistoryItem = new ToolStripMenuItem("이 라인 출고이력 관리창에서 열기(추적)");
        openHistoryItem.Click += OnOpenLineInHistoryClick;
        menu.Items.Add(openHistoryItem);
        grid.ContextMenuStrip = menu;

        return grid;
    }

    /// <summary>미출고 라인은 아직 확정 전이라는 걸 눈에 띄게 옅은 주황 배경으로 강조한다.</summary>
    private void OnLineGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _lineGrid.Rows[e.RowIndex].DataBoundItem is not LineRow line) return;
        var (back, fore) = line.IsUnshipped
            ? (Color.FromArgb(255, 228, 196), Color.Black)
            : (_lineGrid.DefaultCellStyle.BackColor, _lineGrid.DefaultCellStyle.ForeColor);
        _lineGrid.Rows[e.RowIndex].DefaultCellStyle.BackColor = back;
        _lineGrid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = fore;
    }

    /// <summary>
    /// 상태별 행 강조는 라이트/다크 테마와 무관하게 항상 밝은 파스텔 배경 + 검정 글자로 고정한다
    /// (Forms/OfsForm.cs의 발송대기/메모 행 강조와 같은 관례 — 배경을 밝게 정한 이상 글자색도
    /// 같이 고정해야 다크모드에서 흰 글자가 밝은 배경 위에 묻혀 안 보이는 문제가 없다). 강조가
    /// 없는 "미확인" 행은 색을 건드리지 않고 그리드의 테마 기본색(라이트/다크 자동 반영)을 그대로 쓴다.
    /// </summary>
    private void OnPartyGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _partyGrid.Rows[e.RowIndex].DataBoundItem is not PartyRow row) return;

        var columnName = _partyGrid.Columns[e.ColumnIndex].Name;
        if (columnName == "TotalSupply")
        {
            e.Value = VatCalculator.ToDisplay(row.TotalSupply, _vatExcludedCheck.Checked);
        }
        else if (columnName == "TotalProfit")
        {
            e.Value = VatCalculator.ToDisplay(row.TotalProfit, _vatExcludedCheck.Checked);
        }

        var (back, fore) = row.Status switch
        {
            "대조중" => (Color.LightYellow, Color.Black),
            "확정" => (Color.FromArgb(220, 245, 220), Color.Black),
            "발행완료" => (Color.Gainsboro, Color.Black),
            _ => (_partyGrid.DefaultCellStyle.BackColor, _partyGrid.DefaultCellStyle.ForeColor),
        };
        _partyGrid.Rows[e.RowIndex].DefaultCellStyle.BackColor = back;
        _partyGrid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = fore;
    }

    private string CurrentPeriod => _periodCombo.Text.Trim();

    private void RefreshBoard()
    {
        var period = CurrentPeriod;
        if (!Regex.IsMatch(period, @"^\d{4}-\d{2}$"))
        {
            _statusLabel.Text = "귀속월 형식이 올바르지 않습니다(YYYY-MM).";
            return;
        }

        _channelNames = _channelRepo.GetAll().ToDictionary(c => c.ChannelCode, c => c.ChannelName);
        var favoriteKeys = _masterRepo.GetFavorites().Select(f => f.PartyKey).ToHashSet();
        var manualMaster = _masterRepo.GetAll().Where(p => p.IsManual).ToDictionary(p => p.PartyKey);

        var keys = _closingRepo.GetVisiblePartyKeys(period, _includeAllCheck.Checked);
        var rows = new List<PartyRow>();
        foreach (var key in keys)
        {
            var nameHint = key.StartsWith("CH:", StringComparison.Ordinal)
                ? _channelNames.GetValueOrDefault(key["CH:".Length..], key["CH:".Length..])
                : manualMaster.GetValueOrDefault(key)?.PartyName ?? key;
            var summary = _closingRepo.GetSummary(period, key, nameHint);
            rows.Add(new PartyRow(summary, favoriteKeys.Contains(key)));
        }

        _partyGrid.DataSource = new BindingList<PartyRow>(rows);
        _lineGrid.DataSource = null;

        var counts = rows.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
        _statusSummaryLabel.Text = $"상태요약: 미확인 {counts.GetValueOrDefault("미확인")} / 대조중 {counts.GetValueOrDefault("대조중")} / 확정 {counts.GetValueOrDefault("확정")} / 발행완료 {counts.GetValueOrDefault("발행완료")}";
        _statusLabel.Text = $"{period} 거래처 {rows.Count}건 조회됨. ({DateTime.Now:HH:mm:ss})";
    }

    private void LoadLineGrid()
    {
        if (_partyGrid.CurrentRow?.DataBoundItem is not PartyRow row)
        {
            _lineGrid.DataSource = null;
            return;
        }
        var vatExcluded = _vatExcludedCheck.Checked;
        var rows = row.Source.Lines.Select(l => new LineRow(l, isUnshipped: false, vatExcluded))
            .Concat(row.Source.UnshippedLines.Select(l => new LineRow(l, isUnshipped: true, vatExcluded)))
            .ToList();
        _lineGrid.DataSource = new BindingList<LineRow>(rows);
    }

    private List<PartyRow> SelectedPartyRows() =>
        _partyGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as PartyRow)
            .Where(r => r != null)
            .Cast<PartyRow>()
            .DistinctBy(r => r.PartyKey)
            .ToList();

    private void OnAddManualPartnerClick(object? sender, EventArgs e)
    {
        using var dlg = new SimpleTextPromptDialog("수동 거래처 추가", "거래처명:");
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK) return;
        if (string.IsNullOrWhiteSpace(dlg.Value))
        {
            MessageBox.Show("거래처명을 입력하세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var key = _masterRepo.AddManualPartner(dlg.Value);
        RefreshBoard();
        _statusLabel.Text = $"수동 거래처 '{dlg.Value}'({key})를 추가했습니다. ({DateTime.Now:HH:mm:ss})";
    }

    private void OnManualEntryClick(object? sender, EventArgs e)
    {
        var selected = SelectedPartyRows();
        if (selected.Count != 1)
        {
            MessageBox.Show("금액/비고를 입력할 거래처 1개를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var row = selected[0];
        if (row.Status is "확정" or "발행완료")
        {
            MessageBox.Show("이미 확정된 거래처입니다. 수정하려면 먼저 [확정취소]를 하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new PartnerManualEntryDialog(row.PartyName, row.IsManual, row.TotalQty, row.TotalSupply, row.TotalProfit, row.ReconcileNote);
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK) return;

        if (row.IsManual)
            _closingRepo.SaveManualDraft(CurrentPeriod, row.PartyKey, row.PartyName, dlg.Qty, dlg.Supply, dlg.Profit, "대조중", dlg.Note);
        else
            _closingRepo.SetReconcileNoteForParty(CurrentPeriod, row.PartyKey, row.PartyName, dlg.Note);

        RefreshBoard();
    }

    /// <summary>
    /// MiniERP2(OFS)를 경유하지 않은 주문을 채널 경유 거래처의 발주/출고 이력에 수동으로 편입한다
    /// (거래처마감보드_개발기획서.md §2). 항상 "출고확정" 상태+지정한 날짜로 즉시 확정되므로 마감
    /// 집계에 바로 잡히고, 정상 OutboundDetailTable 행이라 발주/출고 이력 관리창에서도 그대로 조회된다.
    /// </summary>
    private void OnAddManualOrderClick(object? sender, EventArgs e)
    {
        var selected = SelectedPartyRows();
        if (selected.Count != 1 || selected[0].IsManual)
        {
            MessageBox.Show("수동 주문을 추가할 채널 경유 거래처 1개를 선택하세요(수동 거래처는 [금액입력/비고]를 이용하세요).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var row = selected[0];
        var channelCode = row.PartyKey["CH:".Length..];

        using var dlg = new PartnerManualOrderDialog(channelCode, row.PartyName);
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK) return;

        var detail = new OutboundDetail
        {
            ChannelCode = channelCode,
            MskuCode = dlg.CskuCode,
            CskuCode = dlg.CskuCode,
            Qty = (int)dlg.Qty,
            SupplyPrice = dlg.UnitPrice,
            PurchasePrice = dlg.CostPrice,
            ProductName = dlg.ItemName,
            Remark = dlg.Note,
            ConfirmedAt = dlg.OrderDate,
        };
        _outboundRepo.AddManualEntry(detail);

        // 고른 날짜가 지금 보고 있는 귀속월과 다르면, 방금 넣은 건이 바로 보이도록 그 달로 화면을 옮긴다.
        _periodCombo.Text = dlg.OrderDate.ToString("yyyy-MM");
        RefreshBoard();
        SelectPartyByKey(row.PartyKey);
        _statusLabel.Text = $"수동 주문을 추가했습니다({dlg.OrderDate:yyyy-MM-dd}, {dlg.CskuCode} x{dlg.Qty}). ({DateTime.Now:HH:mm:ss})";
    }

    /// <summary>"수동 주문 추가"의 엑셀 일괄 버전. 같은 선택 가드(채널 경유 거래처 1개)를 쓴다.</summary>
    private void OnBulkImportClick(object? sender, EventArgs e)
    {
        var selected = SelectedPartyRows();
        if (selected.Count != 1 || selected[0].IsManual)
        {
            MessageBox.Show("엑셀로 일괄 추가할 채널 경유 거래처 1개를 선택하세요(수동 거래처는 [금액입력/비고]를 이용하세요).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var row = selected[0];
        var channelCode = row.PartyKey["CH:".Length..];

        using var dlg = new PartnerBulkOrderImportDialog(channelCode, row.PartyName);
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK || dlg.LatestSaleDate == null) return;

        _periodCombo.Text = dlg.LatestSaleDate.Value.ToString("yyyy-MM");
        RefreshBoard();
        SelectPartyByKey(row.PartyKey);
        _statusLabel.Text = $"엑셀 일괄 추가로 {dlg.ImportedCount}건을 등록했습니다. ({DateTime.Now:HH:mm:ss})";
    }

    private void SelectPartyByKey(string partyKey)
    {
        foreach (DataGridViewRow gridRow in _partyGrid.Rows)
        {
            if (gridRow.DataBoundItem is not PartyRow pr || pr.PartyKey != partyKey) continue;
            _partyGrid.ClearSelection();
            gridRow.Selected = true;
            _partyGrid.CurrentCell = gridRow.Cells[0];
            LoadLineGrid();
            break;
        }
    }

    private void OnConfirmClick(object? sender, EventArgs e)
    {
        var selected = SelectedPartyRows();
        if (selected.Count == 0)
        {
            MessageBox.Show("마감확정할 거래처를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show($"{selected.Count}건을 마감확정하시겠습니까?\n확정 후에는 원본 라인이 바뀌어도 발행 내용에는 영향이 없습니다.", "마감확정 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var period = CurrentPeriod;
        var errors = new List<string>();
        foreach (var row in selected)
        {
            try
            {
                if (row.IsManual)
                {
                    var header = _closingRepo.GetHeader(period, row.PartyKey);
                    if (header == null)
                    {
                        errors.Add($"{row.PartyName}: 먼저 [금액입력/비고]로 금액을 입력하세요.");
                        continue;
                    }
                    _closingRepo.ConfirmManual(period, row.PartyKey, row.PartyName, header.TotalQty, header.TotalSupply, header.TotalProfit, header.ReconcileNote);
                }
                else
                {
                    _closingRepo.Confirm(period, row.PartyKey, row.PartyName);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{row.PartyName}: {ex.Message}");
            }
        }

        RefreshBoard();
        _statusLabel.Text = errors.Count == 0
            ? $"{selected.Count}건 마감확정 완료. ({DateTime.Now:HH:mm:ss})"
            : $"일부 실패: {string.Join(" / ", errors)}";
    }

    private void OnCancelClosingClick(object? sender, EventArgs e)
    {
        var selected = SelectedPartyRows().Where(r => r.ClosingId != null).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("확정취소할, 이미 확정된 거래처를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var publishedCount = selected.Count(r => r.Status == "발행완료");
        var warning = publishedCount > 0 ? $"\n⚠ {publishedCount}건은 이미 발행완료 상태입니다(발행 문서 자체는 이력에 남습니다)." : "";
        if (MessageBox.Show($"{selected.Count}건의 마감확정을 취소하시겠습니까?{warning}", "확정취소 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        foreach (var row in selected) _closingRepo.Cancel(row.ClosingId!.Value);

        RefreshBoard();
        _statusLabel.Text = $"{selected.Count}건 확정취소 완료. ({DateTime.Now:HH:mm:ss})";
    }

    /// <summary>
    /// 명세표/매출장을 파일로 저장하지 않고 새 창(비모달)으로 바로 확인한다(버그수정 §1 — 매번
    /// 파일로 받아 엑셀을 열어 확인하고 다시 고치는 왕복이 번거롭다는 요청). 선택 1건 기준이며,
    /// 마감확정 전(대조중/미확인)이어도 라이브 집계로 미리 계산 결과를 볼 수 있다.
    /// </summary>
    private void OnPreviewClick(bool isLedger)
    {
        var selected = SelectedPartyRows();
        if (selected.Count != 1)
        {
            MessageBox.Show("미리보기할 거래처 1개를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var row = selected[0];

        var vatExcluded = _vatExcludedCheck.Checked;
        var docType = vatExcluded ? MiniERP2.Models.DocType.TradeStatementVatExcl : MiniERP2.Models.DocType.TradeStatementVatIncl;
        var ignoreDateForLedger = false;
        if (isLedger)
        {
            var groupChoice = MessageBox.Show("날짜별로 구분해서 CSKU를 합산하시겠습니까?\n(아니오 = 날짜 무관 CSKU 전체합산, 취소 = 중단)", "매출장 집계 방식", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (groupChoice == DialogResult.Cancel) return;
            ignoreDateForLedger = groupChoice == DialogResult.No;
        }

        var supplier = _docPartyRepo.GetDefaultSupplier();
        if (supplier == null)
        {
            MessageBox.Show("공급자(기본 거래처) 프로필이 설정되어 있지 않습니다. 문서관리 화면에서 먼저 등록하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var buyer = row.IsManual
            ? new DocParty { CompanyName = row.PartyName }
            : _docPartyRepo.GetByChannelCode(row.PartyKey["CH:".Length..]);
        if (buyer == null)
        {
            MessageBox.Show("공급받는자 프로필이 이 채널에 연결되어 있지 않습니다(거래처 관리에서 채널 연결 필요). 미리보기는 공급받는자 정보가 비어있는 채로 표시됩니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            buyer = new DocParty { CompanyName = row.PartyName };
        }

        var summary = _closingRepo.GetSummary(CurrentPeriod, row.PartyKey, row.PartyName);
        var previewForm = isLedger
            ? DocumentPreviewForm.ForSalesLedger(PartnerClosingDocumentBuilder.BuildSalesLedger(summary, supplier, buyer, ignoreDateForLedger, vatExcluded))
            : DocumentPreviewForm.ForTradeStatement(PartnerClosingDocumentBuilder.BuildTradeStatement(summary, docType, supplier, buyer));
        previewForm.Show(this);
    }

    /// <summary>
    /// 명세표/매출장 발행(§9). DocsForm의 그리드 주입 방식 대신 DocumentExporter를 헤드리스로 직접
    /// 호출한다 — 선택 1건이면 SaveFileDialog로 파일명을 묻고, 여러 건이면(배치) 폴더 하나만 골라
    /// "{거래처명}_{기간}_{문서}.xlsx"로 자동 저장한다. 확정(§상태=확정/발행완료) 상태인 거래처만 대상.
    /// 매출장은 여러 거래처를 골랐을 때 "통합 출력"(현황표 시트 + 확정 거래처별 시트를 파일 1개로)도
    /// 고를 수 있다(RunCombinedLedgerExport).
    /// </summary>
    private void OnPublishClick(bool isLedger)
    {
        var allSelected = SelectedPartyRows();
        var selected = allSelected.Where(r => r.Status is "확정" or "발행완료").ToList();
        var skippedNotConfirmed = allSelected.Count - selected.Count;
        if (allSelected.Count == 0)
        {
            MessageBox.Show("발행할 거래처를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var vatExcluded = _vatExcludedCheck.Checked;
        var docType = vatExcluded ? MiniERP2.Models.DocType.TradeStatementVatExcl : MiniERP2.Models.DocType.TradeStatementVatIncl;
        var ignoreDateForLedger = false;
        if (!isLedger)
        {
            if (selected.Count == 0)
            {
                MessageBox.Show("발행할 확정 상태 거래처를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        else
        {
            var groupChoice = MessageBox.Show("날짜별로 구분해서 CSKU를 합산하시겠습니까?\n(아니오 = 날짜 무관 CSKU 전체합산, 취소 = 발행 중단)", "매출장 집계 방식", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (groupChoice == DialogResult.Cancel) return;
            ignoreDateForLedger = groupChoice == DialogResult.No;

            if (allSelected.Count > 1)
            {
                var combineChoice = MessageBox.Show(
                    "여러 거래처를 골랐습니다. 현황표(전체 거래처 마감확정 여부)+확정된 거래처별 매출장 시트를 파일 1개로 통합 출력하시겠습니까?\n(아니오 = 거래처별 개별 파일, 취소 = 발행 중단)",
                    "매출장 출력 방식", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (combineChoice == DialogResult.Cancel) return;
                if (combineChoice == DialogResult.Yes)
                {
                    RunCombinedLedgerExport(allSelected, selected, ignoreDateForLedger, vatExcluded);
                    return;
                }
            }

            if (selected.Count == 0)
            {
                MessageBox.Show("발행할 확정 상태 거래처를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        var supplier = _docPartyRepo.GetDefaultSupplier();
        if (supplier == null)
        {
            MessageBox.Show("공급자(기본 거래처) 프로필이 설정되어 있지 않습니다. 문서관리 화면에서 먼저 등록하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string? folder = null;
        if (selected.Count > 1)
        {
            using var fbd = new FolderBrowserDialog { Description = "발행 파일을 저장할 폴더를 선택하세요." };
            if (fbd.ShowDialog(this) != DialogResult.OK) return;
            folder = fbd.SelectedPath;
        }

        var docLabel = isLedger ? "매출장" : "거래명세표";
        var errors = new List<string>();
        var successCount = 0;
        string? lastFilePath = null;

        foreach (var row in selected)
        {
            var buyer = row.IsManual
                ? new DocParty { CompanyName = row.PartyName }
                : _docPartyRepo.GetByChannelCode(row.PartyKey["CH:".Length..]);
            if (buyer == null)
            {
                errors.Add($"{row.PartyName}: 공급받는자 프로필 미연결(거래처 관리에서 채널 연결 필요)");
                continue;
            }

            var summary = _closingRepo.GetSummary(CurrentPeriod, row.PartyKey, row.PartyName);
            var fileName = PartnerClosingDocumentBuilder.DefaultFileName(summary, docLabel);

            string filePath;
            if (folder != null)
            {
                filePath = Path.Combine(folder, fileName);
            }
            else
            {
                var picked = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx", fileName,
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                if (picked == null) continue;
                filePath = picked;
            }

            try
            {
                decimal totalAmount;
                if (isLedger)
                {
                    var doc = PartnerClosingDocumentBuilder.BuildSalesLedger(summary, supplier, buyer, ignoreDateForLedger, vatExcluded);
                    DocumentExporter.ExportSalesLedger(doc, filePath);
                    totalAmount = doc.TotalSupply;
                }
                else
                {
                    var doc = PartnerClosingDocumentBuilder.BuildTradeStatement(summary, docType, supplier, buyer);
                    DocumentExporter.ExportTradeStatement(doc, filePath);
                    totalAmount = doc.GrandTotal;
                }

                byte[]? fileBytes = null;
                try { fileBytes = File.ReadAllBytes(filePath); } catch { /* 백업 실패해도 이력 저장은 계속 */ }

                var docHistoryId = _docHistoryRepo.Add(new DocHistoryRecord
                {
                    DocType = isLedger ? "SalesLedger" : docType.ToString(),
                    IssueDate = DateTime.Today,
                    BuyerName = buyer.CompanyName,
                    TotalAmount = totalAmount,
                    FilePath = filePath,
                    CreatedAt = DateTime.Now,
                    FileBytes = fileBytes,
                    ChannelCode = row.IsManual ? "" : row.PartyKey["CH:".Length..],
                    Period = CurrentPeriod,
                });

                if (row.ClosingId != null) _closingRepo.MarkPublished(row.ClosingId.Value, docHistoryId);
                successCount++;
                lastFilePath = filePath;
            }
            catch (Exception ex)
            {
                errors.Add($"{row.PartyName}: {ExportHelper.DescribeSaveError(ex)}");
            }
        }

        RefreshBoard();

        if (successCount == 1 && lastFilePath != null)
        {
            ExportHelper.ShowPostExportDialog(this, lastFilePath);
        }

        var msg = $"{successCount}건 발행 완료.";
        if (errors.Count > 0) msg += " 실패: " + string.Join(" / ", errors);
        if (skippedNotConfirmed > 0) msg += $" ({skippedNotConfirmed}건은 미확정이라 제외)";
        _statusLabel.Text = msg;
    }

    /// <summary>
    /// 매출장 통합 출력: 파일 1개 안에 "현황" 시트(선택한 거래처 전부 — 미확정 포함, 거래금액·
    /// 마감확정 여부) + 확정된 거래처별 매출장 시트를 이어붙인다. 미확정 거래처는 현황표에만
    /// 나타나고 매출장 시트는 생기지 않는다.
    /// </summary>
    private void RunCombinedLedgerExport(List<PartyRow> allSelected, List<PartyRow> confirmed, bool ignoreDateForLedger, bool vatExcluded)
    {
        var supplier = _docPartyRepo.GetDefaultSupplier();
        if (supplier == null)
        {
            MessageBox.Show("공급자(기본 거래처) 프로필이 설정되어 있지 않습니다. 문서관리 화면에서 먼저 등록하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var combinedFilePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"매출장_통합_{CurrentPeriod}_{DateTime.Now:yyyyMMdd}.xlsx",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (combinedFilePath == null) return;

        var overviewRows = new List<SalesLedgerOverviewRow>();
        var ledgers = new List<(string PartyName, SalesLedgerDoc Doc)>();
        var confirmedForHistory = new List<(PartyRow Row, PartnerClosingSummary Summary)>();
        var errors = new List<string>();

        foreach (var row in allSelected)
        {
            var summary = _closingRepo.GetSummary(CurrentPeriod, row.PartyKey, row.PartyName);
            var isConfirmed = row.Status is "확정" or "발행완료";
            // 같은 파일의 개별 매출장 시트가 vatExcluded 기준으로 변환되므로, "현황" 시트 합계도
            // 같은 기준으로 맞춰야 두 시트를 비교할 때 금액이 어긋나 보이지 않는다.
            overviewRows.Add(new SalesLedgerOverviewRow
            {
                PartyName = row.PartyName,
                Status = row.Status,
                IsConfirmed = isConfirmed,
                TotalQty = summary.TotalQty,
                TotalSupply = VatCalculator.ToDisplay(summary.TotalSupply, vatExcluded),
                TotalProfit = VatCalculator.ToDisplay(summary.TotalProfit, vatExcluded),
            });
            if (!isConfirmed) continue;

            var buyer = row.IsManual
                ? new DocParty { CompanyName = row.PartyName }
                : _docPartyRepo.GetByChannelCode(row.PartyKey["CH:".Length..]);
            if (buyer == null)
            {
                errors.Add($"{row.PartyName}: 공급받는자 프로필 미연결");
                continue;
            }

            ledgers.Add((row.PartyName, PartnerClosingDocumentBuilder.BuildSalesLedger(summary, supplier, buyer, ignoreDateForLedger, vatExcluded)));
            confirmedForHistory.Add((row, summary));
        }

        if (ledgers.Count == 0)
        {
            MessageBox.Show("통합 출력할 확정 상태 거래처가 없습니다(현황표만으로는 출력하지 않습니다).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            DocumentExporter.ExportSalesLedgersCombined(overviewRows, ledgers, combinedFilePath);

            byte[]? fileBytes = null;
            try { fileBytes = File.ReadAllBytes(combinedFilePath); } catch { /* 백업 실패해도 이력 저장은 계속 */ }

            foreach (var (row, summary) in confirmedForHistory)
            {
                var docHistoryId = _docHistoryRepo.Add(new DocHistoryRecord
                {
                    DocType = "SalesLedger",
                    IssueDate = DateTime.Today,
                    BuyerName = row.PartyName,
                    TotalAmount = summary.TotalSupply,
                    FilePath = combinedFilePath,
                    CreatedAt = DateTime.Now,
                    FileBytes = fileBytes,
                    ChannelCode = row.IsManual ? "" : row.PartyKey["CH:".Length..],
                    Period = CurrentPeriod,
                });
                if (row.ClosingId != null) _closingRepo.MarkPublished(row.ClosingId.Value, docHistoryId);
            }

            RefreshBoard();
            ExportHelper.ShowPostExportDialog(this, combinedFilePath);

            var msg = $"통합 매출장 출력 완료 — 현황 {overviewRows.Count}건, 매출장 시트 {ledgers.Count}건.";
            if (errors.Count > 0) msg += " 실패: " + string.Join(" / ", errors);
            _statusLabel.Text = msg;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"통합 출력 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 좌측 거래처 목록(현황판)을 지금 화면에 보이는 그대로(★/거래처/출고건/수량/공급가합/이익/상태/
    /// 미출고건/경고/비고, VAT별도 체크박스 기준 환산 포함) 엑셀 1장으로 저장한다.
    /// </summary>
    private void OnExportBoardClick(object? sender, EventArgs e)
    {
        if (_partyGrid.DataSource is not BindingList<PartyRow> rows || rows.Count == 0)
        {
            MessageBox.Show("내보낼 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var period = CurrentPeriod;
        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"거래처마감현황_{period}_{DateTime.Now:yyyyMMdd}.xlsx",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;

        try
        {
            ExcelLicense.Ensure();

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("거래처마감현황");

            var headers = new[] { "★", "거래처", "출고건", "수량", "공급가합", "이익", "상태", "미출고건", "경고", "비고" };
            for (var i = 0; i < headers.Length; i++) worksheet.Cells[1, i + 1].Value = headers[i];

            var vatExcluded = _vatExcludedCheck.Checked;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var r = i + 2;
                worksheet.Cells[r, 1].Value = row.FavoriteMark;
                worksheet.Cells[r, 2].Value = row.PartyName;
                worksheet.Cells[r, 3].Value = row.OutboundCount;
                worksheet.Cells[r, 4].Value = row.TotalQty;
                worksheet.Cells[r, 5].Value = VatCalculator.ToDisplay(row.TotalSupply, vatExcluded);
                worksheet.Cells[r, 6].Value = VatCalculator.ToDisplay(row.TotalProfit, vatExcluded);
                worksheet.Cells[r, 7].Value = row.Status;
                worksheet.Cells[r, 8].Value = row.UnshippedCount;
                worksheet.Cells[r, 9].Value = row.Warning;
                worksheet.Cells[r, 10].Value = row.ReconcileNote;
            }

            worksheet.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
            worksheet.Cells[2, 5, rows.Count + 1, 6].Style.Numberformat.Format = "#,##0";
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            ExportHelper.SaveExcel(package, filePath);

            ExportHelper.ShowPostExportDialog(this, filePath);
            _statusLabel.Text = $"현황판을 엑셀로 저장했습니다({rows.Count}건). ({DateTime.Now:HH:mm:ss})";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀 저장 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnToggleFavoriteClick(object? sender, EventArgs e)
    {
        var selected = SelectedPartyRows();
        if (selected.Count == 0) return;
        foreach (var row in selected) _masterRepo.SetFavorite(row.PartyKey, !row.IsFavorite);
        RefreshBoard();
    }

    private void OnReassignPeriodClick(object? sender, EventArgs e)
    {
        var ids = _lineGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => (r.DataBoundItem as LineRow)?.Source.OutboundDetailId)
            .Where(id => id != null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            MessageBox.Show("귀속월을 변경할 라인을 선택하세요(원본 라인이 있는 건만 가능합니다).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SimpleTextPromptDialog("귀속월 변경", "새 귀속월 (YYYY-MM):", CurrentPeriod, SimpleTextPromptDialog.PeriodValidator);
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK) return;

        _closingRepo.ReassignPeriod(ids, dlg.Value);
        RefreshBoard();
        _statusLabel.Text = $"{ids.Count}건의 귀속월을 {dlg.Value}(으)로 변경했습니다. ({DateTime.Now:HH:mm:ss})";
    }

    private List<LineRow> SelectedUnshippedLineRows() =>
        _lineGrid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as LineRow)
            .Where(l => l != null && l.IsUnshipped && l.Source.OutboundDetailId != null)
            .Cast<LineRow>()
            .DistinctBy(l => l.Source.OutboundDetailId)
            .ToList();

    /// <summary>
    /// 마감보드 라인 상세에 함께 보이는 미출고 건을 확정 처리 없이도(=출고이력 관리창을 따로 열지
    /// 않고도) 바로 출고확정 시킨다. 확인 없이 누르면 되돌리기 번거로워질 수 있어 한 번 더 물어본다.
    /// </summary>
    private void OnMarkLinesShippedClick(object? sender, EventArgs e)
    {
        var lines = SelectedUnshippedLineRows();
        if (lines.Count == 0)
        {
            MessageBox.Show("출고확정 처리할 미출고 라인을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show($"선택한 {lines.Count}건을 출고확정 처리하시겠습니까?", "출고확정 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _outboundRepo.MarkAsShipped(lines.Select(l => l.Source.OutboundDetailId!.Value));

        var partyKey = (_partyGrid.CurrentRow?.DataBoundItem as PartyRow)?.PartyKey;
        RefreshBoard();
        if (partyKey != null) SelectPartyByKey(partyKey);
        _statusLabel.Text = $"{lines.Count}건을 출고확정 처리했습니다. ({DateTime.Now:HH:mm:ss})";
    }

    /// <summary>
    /// 미출고 건을 라인 상세에서 바로 삭제한다(이미 출고확정/마감확정된 라인은 대상에서 제외).
    /// </summary>
    private void OnDeleteLinesClick(object? sender, EventArgs e)
    {
        var lines = SelectedUnshippedLineRows();
        if (lines.Count == 0)
        {
            MessageBox.Show("삭제할 미출고 라인을 선택하세요(이미 출고확정된 라인은 여기서 삭제할 수 없습니다 — 출고이력 관리창을 이용하세요).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show($"선택한 {lines.Count}건을 삭제하시겠습니까? 되돌릴 수 없습니다.", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _outboundRepo.DeleteByIds(lines.Select(l => l.Source.OutboundDetailId!.Value));

        var partyKey = (_partyGrid.CurrentRow?.DataBoundItem as PartyRow)?.PartyKey;
        RefreshBoard();
        if (partyKey != null) SelectPartyByKey(partyKey);
        _statusLabel.Text = $"{lines.Count}건을 삭제했습니다. ({DateTime.Now:HH:mm:ss})";
    }

    /// <summary>
    /// 라인 상세의 품목/단가 편집을 처리한다(§1). 품목은 바로 저장되고, 단가는 이번 건에만 적용할지
    /// 이 날짜 이후 같은 CSKU 전체(+등록 단가)에 적용할지 물어본다. 스냅샷(확정 후)이나 원본 라인이
    /// 없는 건(OutboundDetailId=null)은 편집 대상에서 제외한다.
    /// </summary>
    private void OnLineGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _lineGrid.Rows.Count) return;
        if (_lineGrid.Rows[e.RowIndex].DataBoundItem is not LineRow line) return;
        if (line.Source.OutboundDetailId is not { } detailId)
        {
            MessageBox.Show("원본 발주/출고 라인이 없는 항목(확정 스냅샷 등)은 여기서 수정할 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshBoard();
            return;
        }

        var columnName = _lineGrid.Columns[e.ColumnIndex].Name;
        if (columnName == "ItemName")
        {
            _outboundRepo.UpdateProductName(detailId, line.ItemName);
            _statusLabel.Text = $"품목명을 '{line.ItemName}'(으)로 수정했습니다. ({DateTime.Now:HH:mm:ss})";
        }
        else if (columnName == "UnitPrice")
        {
            HandleUnitPriceEdit(line, detailId);
        }
    }

    private void HandleUnitPriceEdit(LineRow line, long detailId)
    {
        var partyRow = _partyGrid.CurrentRow?.DataBoundItem as PartyRow;
        if (partyRow == null || partyRow.IsManual || string.IsNullOrEmpty(line.CskuCode))
        {
            RefreshBoard();
            return;
        }
        var channelCode = partyRow.PartyKey["CH:".Length..];
        // 그리드에 표시/편집된 값은 체크박스 기준(VAT별도면 공급가 기준)이므로, 저장 전 항상
        // VAT포함 원본 기준으로 환산한다 — CSKU/발주 데이터는 항상 VAT포함으로 저장하는 관례.
        var vatExcluded = _vatExcludedCheck.Checked;
        var newPrice = vatExcluded
            ? Math.Round(line.UnitPrice * VatCalculator.VatDivisor, 0, MidpointRounding.AwayFromZero)
            : line.UnitPrice;
        var lineDate = (line.Source.LineDate ?? DateTime.Today).Date;

        var priceLabel = vatExcluded ? $"{newPrice:N0}원(VAT포함, 입력한 VAT별도 값 {line.UnitPrice:N0}원 환산)" : $"{newPrice:N0}원";
        var choice = MessageBox.Show(
            $"단가를 {priceLabel}으로 변경합니다.\n\n" +
            "[예] 이 건에만 적용\n" +
            $"[아니오] {lineDate:yyyy-MM-dd} 이후 이 CSKU({line.CskuCode}) 전체에 적용(등록 단가도 함께 갱신)\n" +
            "[취소] 변경 취소",
            "단가 변경 범위 선택", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

        if (choice == DialogResult.Cancel)
        {
            RefreshBoard();
            SelectPartyByKey(partyRow.PartyKey);
            return;
        }

        if (choice == DialogResult.Yes)
        {
            _outboundRepo.CorrectSupplyPrice(detailId, newPrice);
            _statusLabel.Text = $"이 건의 단가를 {newPrice:N0}원으로 수정했습니다. ({DateTime.Now:HH:mm:ss})";
        }
        else
        {
            var updatedCount = _outboundRepo.CorrectSupplyPriceForCskuFromDate(channelCode, line.CskuCode, lineDate, newPrice);

            var registered = _channelSkuRepo.GetByChannelAndCskuCode(channelCode, line.CskuCode);
            var registeredNote = "";
            if (registered != null)
            {
                registered.SupplyPrice = newPrice;
                _channelSkuRepo.Upsert(registered);
                registeredNote = " + 등록 단가";
            }

            _statusLabel.Text = $"{lineDate:yyyy-MM-dd} 이후 {line.CskuCode} {updatedCount}건{registeredNote}을 {newPrice:N0}원으로 일괄 수정했습니다. ({DateTime.Now:HH:mm:ss})";
        }

        RefreshBoard();
        SelectPartyByKey(partyRow.PartyKey);
    }

    /// <summary>
    /// 이 라인을 발주/출고 이력 관리창에서 바로 찾아 연다("역으로 추적" — §2). 마감보드 라인
    /// 상세에는 없는 필드(운송장번호·수령인·상태 등)까지 이어서 확인·수정할 수 있다.
    /// </summary>
    private void OnOpenLineInHistoryClick(object? sender, EventArgs e)
    {
        var line = _lineGrid.CurrentRow?.DataBoundItem as LineRow;
        var partyRow = _partyGrid.CurrentRow?.DataBoundItem as PartyRow;
        if (line?.Source.OutboundDetailId == null || partyRow == null || partyRow.IsManual)
        {
            FormManager.Show<OutboundHistoryForm>();
            return;
        }

        var channelCode = partyRow.PartyKey["CH:".Length..];
        var form = new OutboundHistoryForm(line.Source.OutboundDetailId, channelCode, line.Source.LineDate);
        FormManager.ApplyBoundsTracking(form);
        form.Show();
    }

    private sealed class PartyRow(PartnerClosingSummary source, bool isFavorite)
    {
        public PartnerClosingSummary Source { get; } = source;
        public string PartyKey { get; } = source.PartyKey;
        public bool IsManual { get; } = source.IsManual;
        public long? ClosingId { get; } = source.ClosingId;
        public bool IsFavorite { get; } = isFavorite;
        public string FavoriteMark { get; } = isFavorite ? "★" : "";
        public string PartyName { get; } = source.PartyName;
        public int OutboundCount { get; } = source.Lines.Count;
        public decimal TotalQty { get; } = source.TotalQty;
        public decimal TotalSupply { get; } = source.TotalSupply;
        public decimal TotalProfit { get; } = source.TotalProfit;
        public string Status { get; } = source.Status;
        public int UnshippedCount { get; } = source.UnshippedCount;
        public string ReconcileNote { get; } = source.ReconcileNote;
        public string Warning { get; } = BuildWarning(source);

        private static string BuildWarning(PartnerClosingSummary s)
        {
            var parts = new List<string>();
            if (s.HasUnstableKeyLines) parts.Add("미확정묶음");
            if (s.FreightFallbackByCount) parts.Add("운임균등배부");
            return parts.Count == 0 ? "" : "⚠" + string.Join(",", parts);
        }
    }

    private sealed class LineRow(PartnerClosingLine source, bool isUnshipped, bool vatExcluded)
    {
        public PartnerClosingLine Source { get; } = source;
        public bool IsUnshipped { get; } = isUnshipped;
        public string StatusText { get; } = isUnshipped ? "미출고" : "확정";
        public string LineDateText { get; } = source.LineDate?.ToString("yyyy-MM-dd") ?? "";
        public string CskuCode { get; } = source.CskuCode;
        public string ItemName { get; set; } = source.ItemName;
        public decimal Qty { get; } = source.Qty;

        /// <summary>화면 표시/편집 값 — "VAT 별도" 체크 시 CSKU 원본(VAT포함) 값을 공급가 기준으로
        /// 나눠 보여준다. 이 칸은 편집 가능하므로(HandleUnitPriceEdit), 저장할 때는 반드시 다시
        /// VAT포함 원본 기준으로 환산해야 한다 — CSKU/발주 데이터는 항상 VAT포함으로 저장하는 관례.</summary>
        public decimal UnitPrice { get; set; } = VatCalculator.ToDisplay(source.UnitPrice, vatExcluded);
        public decimal CostPrice { get; } = VatCalculator.ToDisplay(source.CostPrice, vatExcluded);
        public decimal Profit { get; } = VatCalculator.ToDisplay(source.Profit, vatExcluded);
    }
}

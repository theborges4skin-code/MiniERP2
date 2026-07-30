using System.ComponentModel;
using System.Text.RegularExpressions;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;

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

    private ComboBox _periodCombo = new();
    private CheckBox _includeAllCheck = new();
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
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
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
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

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

        var btnConfirm = new Button { Text = "마감확정", Size = new Size(80, 28) };
        btnConfirm.Click += OnConfirmClick;

        var btnCancelClosing = new Button { Text = "확정취소", Size = new Size(80, 28) };
        btnCancelClosing.Click += OnCancelClosingClick;

        _statusSummaryLabel = new Label { AutoSize = true, Padding = new Padding(10, 6, 0, 0), Text = "상태요약: -" };

        toolbar.Controls.Add(new Label { Text = "기간:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        toolbar.Controls.Add(_periodCombo);
        toolbar.Controls.Add(btnRefresh);
        toolbar.Controls.Add(_includeAllCheck);
        toolbar.Controls.Add(btnAddManual);
        toolbar.Controls.Add(btnManualEntry);
        toolbar.Controls.Add(btnConfirm);
        toolbar.Controls.Add(btnCancelClosing);
        toolbar.Controls.Add(_statusSummaryLabel);
        return toolbar;
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
        openHistoryItem.Click += (s, e) => FormManager.Show<OutboundHistoryForm>();
        menu.Items.Add(openHistoryItem);
        grid.ContextMenuStrip = menu;

        return grid;
    }

    private ExcelLikeDataGridView BuildLineGrid()
    {
        var grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "PartnerClosingForm.LineGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
        };
        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "일자", Name = "LineDateText", DataPropertyName = "LineDateText", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "CskuCode", DataPropertyName = "CskuCode", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "품목", Name = "ItemName", DataPropertyName = "ItemName", Width = 150 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 55, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "단가", Name = "UnitPrice", DataPropertyName = "UnitPrice", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "원가", Name = "CostPrice", DataPropertyName = "CostPrice", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { HeaderText = "이익", Name = "Profit", DataPropertyName = "Profit", Width = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight } }
        );

        var menu = new ContextMenuStrip();
        var reassignItem = new ToolStripMenuItem("귀속월 변경");
        reassignItem.Click += OnReassignPeriodClick;
        menu.Items.Add(reassignItem);
        var openHistoryItem = new ToolStripMenuItem("출고이력 관리창에서 열기");
        openHistoryItem.Click += (s, e) => FormManager.Show<OutboundHistoryForm>();
        menu.Items.Add(openHistoryItem);
        grid.ContextMenuStrip = menu;

        return grid;
    }

    private void OnPartyGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _partyGrid.Rows[e.RowIndex].DataBoundItem is not PartyRow row) return;
        _partyGrid.Rows[e.RowIndex].DefaultCellStyle.BackColor = row.Status switch
        {
            "대조중" => Color.LightYellow,
            "확정" => Color.FromArgb(220, 245, 220),
            "발행완료" => Color.Gainsboro,
            _ => Color.White,
        };
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
        _lineGrid.DataSource = new BindingList<LineRow>(row.Source.Lines.Select(l => new LineRow(l)).ToList());
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
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
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
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        if (row.IsManual)
            _closingRepo.SaveManualDraft(CurrentPeriod, row.PartyKey, row.PartyName, dlg.Qty, dlg.Supply, dlg.Profit, "대조중", dlg.Note);
        else
            _closingRepo.SetReconcileNoteForParty(CurrentPeriod, row.PartyKey, row.PartyName, dlg.Note);

        RefreshBoard();
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
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _closingRepo.ReassignPeriod(ids, dlg.Value);
        RefreshBoard();
        _statusLabel.Text = $"{ids.Count}건의 귀속월을 {dlg.Value}(으)로 변경했습니다. ({DateTime.Now:HH:mm:ss})";
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

    private sealed class LineRow(PartnerClosingLine source)
    {
        public PartnerClosingLine Source { get; } = source;
        public string LineDateText { get; } = source.LineDate?.ToString("yyyy-MM-dd") ?? "";
        public string CskuCode { get; } = source.CskuCode;
        public string ItemName { get; } = source.ItemName;
        public decimal Qty { get; } = source.Qty;
        public decimal UnitPrice { get; } = source.UnitPrice;
        public decimal CostPrice { get; } = source.CostPrice;
        public decimal Profit { get; } = source.Profit;
    }
}

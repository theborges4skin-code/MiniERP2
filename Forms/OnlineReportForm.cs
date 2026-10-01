using System.Globalization;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 온라인 매출 종합보고서 — 수동 엑셀 "온라인 매출(YY년MM월).xlsx"를 대체한다.
/// 이익분석·광고비 DB, 거래처 마감보드, CSKU별 통계에서 숫자를 불러오고, 월별 수동값(환율·비용·수출·운임표·
/// 풀필먼트 건수·추가 광고비·거래처 수동값)을 입력받아 요약(A4 가로) + 채널별(A4 세로) 엑셀을 만든다.
/// </summary>
public class OnlineReportForm : Form
{
    private readonly OnlineReportConfigService _configService = new();
    private readonly OnlineReportMonthRepository _monthRepo = new();
    private readonly OnlineReportDataSource _dataSource = new();
    private readonly ProfitFactRepository _factRepo = new();

    private OnlineReportConfig _config;
    private OnlineReportMonthInput _month = new();
    private OnlineReportBuilder.Result? _report;
    private string _period = string.Empty;
    private bool _dirty;
    private bool _populating;

    private readonly ComboBox _periodCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly Label _statusLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

    // 환율·비용·수출
    private readonly TextBox _rateBox = new() { Width = 110, TextAlign = HorizontalAlignment.Right };
    private readonly Label _rateNote = new() { AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(0, 6, 0, 0) };
    private readonly DataGridView _costGrid = NewGrid();
    private readonly DataGridView _exportGrid = NewGrid();

    // 택배비
    private readonly DataGridView _freightGrid = NewGrid(allowAdd: true);
    private readonly NumericUpDown _fulfillmentCount = new() { Maximum = 100000, Width = 90, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right };
    private readonly CheckBox _overrideCheck = new() { Text = "조정액 직접 입력", AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
    private readonly TextBox _overrideBox = new() { Width = 120, TextAlign = HorizontalAlignment.Right, Enabled = false };
    private readonly Label _freightNote = new() { Dock = DockStyle.Fill, ForeColor = Color.DimGray };

    // 거래처·추가 광고비
    private readonly DataGridView _partnerGrid = NewGrid();
    private readonly DataGridView _extraAdGrid = NewGrid(allowAdd: true);

    // 미리보기
    private readonly DataGridView _previewGrid = NewGrid();
    private readonly ListBox _warningList = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };

    public OnlineReportForm()
    {
        _config = _configService.Load();
        Text = "온라인 매출 보고서";
        Size = new Size(1320, 840);
        StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();

        var now = DateTime.Today;
        for (int i = 1; i <= 24; i++) _periodCombo.Items.Add(now.AddMonths(-i).ToString("yyyy-MM", CultureInfo.InvariantCulture));
        _periodCombo.Items.Insert(0, now.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        _periodCombo.SelectedIndexChanged += (_, _) => OnPeriodChanged();
        _periodCombo.SelectedIndex = 1; // 지난달(익월초에 마감하므로)
        FormClosing += (_, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
    }

    // ───────────────────────────── 화면 구성 ─────────────────────────────

    private void BuildLayout()
    {
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6, 6, 6, 0), WrapContents = false };
        toolbar.Controls.Add(new Label { Text = "기간", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        toolbar.Controls.Add(_periodCombo);
        toolbar.Controls.Add(ToolButton("새로고침", (_, _) => Recompute()));
        toolbar.Controls.Add(ToolButton("입력 저장", (_, _) => SaveMonth()));
        toolbar.Controls.Add(ToolButton("이익분석 결과파일 불러오기…", OnImportResultFiles, 190));
        toolbar.Controls.Add(ToolButton("설정…", OnSettings));
        toolbar.Controls.Add(ToolButton("엑셀 만들기", (_, _) => OnExport(pdf: false), 100));
        toolbar.Controls.Add(ToolButton("PDF 만들기", (_, _) => OnExport(pdf: true), 100));

        var inputTabs = new TabControl { Dock = DockStyle.Fill };
        inputTabs.TabPages.Add(BuildRateTab());
        inputTabs.TabPages.Add(BuildFreightTab());
        inputTabs.TabPages.Add(BuildPartnerTab());
        inputTabs.TabPages.Add(BuildExtraAdTab());

        var previewSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 470 };
        previewSplit.Panel1.Controls.Add(_previewGrid);
        previewSplit.Panel1.Controls.Add(new Label { Dock = DockStyle.Top, Height = 22, Text = "미리보기 — 블록별 합계와 요약(엑셀 1·2페이지와 같은 숫자)", Font = new Font(Font, FontStyle.Bold) });
        previewSplit.Panel2.Controls.Add(_warningList);
        previewSplit.Panel2.Controls.Add(new Label { Dock = DockStyle.Top, Height = 22, Text = "점검 메시지", Font = new Font(Font, FontStyle.Bold) });
        _previewGrid.ReadOnly = true;
        _previewGrid.Columns.Add("Label", "구분");
        _previewGrid.Columns.Add("Revenue", "매출액");
        _previewGrid.Columns.Add("Profit", "판매분 이익");
        _previewGrid.Columns.Add("Ad", "광고비");
        _previewGrid.Columns.Add("Net", "순이익");
        _previewGrid.Columns.Add("Margin", "마진율");
        _previewGrid.Columns.Add("Ship", "택배 건수");
        _previewGrid.Columns[0].Width = 150;
        foreach (DataGridViewColumn c in _previewGrid.Columns)
            if (c.Index > 0) { c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; c.Width = 110; }

        var main = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 560 };
        main.Panel1.Controls.Add(inputTabs);
        main.Panel2.Controls.Add(previewSplit);

        var status = new Panel { Dock = DockStyle.Bottom, Height = 26, Padding = new Padding(6, 0, 6, 0) };
        status.Controls.Add(_statusLabel);

        Controls.Add(main);
        Controls.Add(status);
        Controls.Add(toolbar);
    }

    private TabPage BuildRateTab()
    {
        var page = new TabPage("환율·비용·수출") { Padding = new Padding(8) };
        var rateRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36 };
        rateRow.Controls.Add(new Label { Text = "환율 USD", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        rateRow.Controls.Add(_rateBox);
        rateRow.Controls.Add(ToolButton("하나은행 월평균 조회", OnFetchRate, 140));
        rateRow.Controls.Add(_rateNote);
        _rateBox.TextChanged += (_, _) => { if (!_populating) { _rateNote.Text = "수동 입력"; MarkDirty(); } };

        _costGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Item", HeaderText = "비용 항목", ReadOnly = true, Width = 160 });
        _costGrid.Columns.Add(AmountColumn("Amount", "금액(원)"));
        _exportGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Market", HeaderText = "수출 시장", ReadOnly = true, Width = 120 });
        _exportGrid.Columns.Add(AmountColumn("Revenue", "매출(USD)", "N2"));
        _exportGrid.Columns.Add(AmountColumn("Profit", "순이익(USD)", "N2"));
        _costGrid.CellEndEdit += (_, _) => OnInputEdited();
        _exportGrid.CellEndEdit += (_, _) => OnInputEdited();

        var copyCosts = ToolButton("전월 비용 가져오기", OnCopyPreviousCosts, 140);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 230 };
        split.Panel1.Controls.Add(_costGrid);
        var costBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        costBar.Controls.Add(new Label { Text = "비용(요약 B) — 최종 결과에서 뺍니다", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        costBar.Controls.Add(copyCosts);
        split.Panel1.Controls.Add(costBar);
        split.Panel2.Controls.Add(_exportGrid);
        split.Panel2.Controls.Add(new Label { Dock = DockStyle.Top, Height = 22, Text = "수출(요약 C) — 달러로 입력하면 환율로 원화 환산" });

        page.Controls.Add(split);
        page.Controls.Add(rateRow);
        return page;
    }

    private TabPage BuildFreightTab()
    {
        var page = new TabPage("택배비") { Padding = new Padding(8) };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36 };
        top.Controls.Add(ToolButton("운임 파일 불러오기…", OnLoadFreightFiles, 140));
        top.Controls.Add(new Label { Text = "풀필먼트 건수", AutoSize = true, Padding = new Padding(10, 6, 0, 0) });
        top.Controls.Add(_fulfillmentCount);
        top.Controls.Add(_overrideCheck);
        top.Controls.Add(_overrideBox);
        _fulfillmentCount.ValueChanged += (_, _) => OnInputEdited();
        _overrideCheck.CheckedChanged += (_, _) => { _overrideBox.Enabled = _overrideCheck.Checked; OnInputEdited(); };
        _overrideBox.Leave += (_, _) => OnInputEdited();

        _freightGrid.Columns.Add(AmountColumn("Rate", "운임(원)"));
        _freightGrid.Columns.Add(AmountColumn("Count", "출고 건수"));
        _freightGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Subtotal", HeaderText = "조정 소계", ReadOnly = true, Width = 110, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, ForeColor = Color.DimGray } });
        _freightGrid.CellEndEdit += (_, _) => OnInputEdited();
        _freightGrid.UserDeletedRow += (_, _) => OnInputEdited();

        var notePanel = new Panel { Dock = DockStyle.Bottom, Height = 70 };
        notePanel.Controls.Add(_freightNote);
        page.Controls.Add(_freightGrid);
        page.Controls.Add(notePanel);
        page.Controls.Add(top);
        return page;
    }

    private TabPage BuildPartnerTab()
    {
        var page = new TabPage("거래처") { Padding = new Padding(8) };
        _partnerGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "표시명", ReadOnly = true, Width = 100 });
        _partnerGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Board", HeaderText = "마감보드 값(매출 / 이익)", ReadOnly = true, Width = 190, DefaultCellStyle = { ForeColor = Color.DimGray } });
        _partnerGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Manual", HeaderText = "수동", Width = 50 });
        _partnerGrid.Columns.Add(AmountColumn("Revenue", "수동 매출"));
        _partnerGrid.Columns.Add(AmountColumn("Profit", "수동 이익"));
        _partnerGrid.CurrentCellDirtyStateChanged += (_, _) => { if (_partnerGrid.IsCurrentCellDirty) _partnerGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _partnerGrid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) OnInputEdited(); };
        page.Controls.Add(_partnerGrid);
        page.Controls.Add(new Label { Dock = DockStyle.Top, Height = 40, Text = "거래처 마감보드 값을 씁니다. 마감보드에 없는 거래처(툴스엠알오 등)는 [수동]을 체크하고 금액을 넣으세요.", ForeColor = Color.DimGray });
        return page;
    }

    private TabPage BuildExtraAdTab()
    {
        var page = new TabPage("추가 광고비") { Padding = new Padding(8) };
        _extraAdGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Block", HeaderText = "블록", Width = 120, FlatStyle = FlatStyle.Flat });
        _extraAdGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Column", HeaderText = "품목 열 번호(0=기타)", Width = 120 });
        _extraAdGrid.Columns.Add(AmountColumn("Amount", "금액(원)"));
        _extraAdGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Memo", HeaderText = "메모", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _extraAdGrid.CellEndEdit += (_, _) => OnInputEdited();
        _extraAdGrid.UserDeletedRow += (_, _) => OnInputEdited();
        _extraAdGrid.DataError += (_, e) => e.ThrowException = false;
        page.Controls.Add(_extraAdGrid);
        page.Controls.Add(new Label { Dock = DockStyle.Top, Height = 40, Text = "광고비 행에 더할 비용입니다(예: 쿠팡로켓 밀크런·CJ택배입고). 양수로 입력하면 비용으로 빠집니다.", ForeColor = Color.DimGray });
        return page;
    }

    // ───────────────────────────── 기간·입력 ─────────────────────────────

    private void OnPeriodChanged()
    {
        var next = _periodCombo.SelectedItem as string ?? string.Empty;
        if (next == _period) return;
        if (!ConfirmDiscard())
        {
            _populating = true;
            _periodCombo.SelectedItem = _period;
            _populating = false;
            return;
        }
        _period = next;
        _month = _monthRepo.Get(_period) ?? new OnlineReportMonthInput();
        PopulateInputs();
        _dirty = false;
        Recompute();
    }

    private void PopulateInputs()
    {
        _populating = true;
        try
        {
            _rateBox.Text = _month.ExchangeRate == 0 ? "" : _month.ExchangeRate.ToString("0.##", CultureInfo.InvariantCulture);
            _rateNote.Text = _month.ExchangeRateNote;

            _costGrid.Rows.Clear();
            foreach (var item in _config.CostItems)
                _costGrid.Rows.Add(item, _month.Costs.TryGetValue(item, out var amount) ? amount : 0m);
            _exportGrid.Rows.Clear();
            foreach (var market in _config.ExportMarkets)
            {
                var entry = _month.Exports.FirstOrDefault(e => e.Market == market);
                _exportGrid.Rows.Add(market, entry?.RevenueUsd ?? 0m, entry?.ProfitUsd ?? 0m);
            }

            _freightGrid.Rows.Clear();
            foreach (var tier in _month.FreightTiers.OrderBy(t => t.Rate)) _freightGrid.Rows.Add(tier.Rate, tier.Count, "");
            _fulfillmentCount.Value = Math.Clamp(_month.FulfillmentCount, 0, (int)_fulfillmentCount.Maximum);
            _overrideCheck.Checked = _month.FreightAdjustmentOverride is not null;
            _overrideBox.Text = _month.FreightAdjustmentOverride?.ToString("0", CultureInfo.InvariantCulture) ?? "";

            _partnerGrid.Rows.Clear();
            foreach (var partner in _config.Partners)
            {
                var manual = _month.PartnerOverrides.FirstOrDefault(o => o.DisplayName == partner.DisplayName);
                _partnerGrid.Rows.Add(partner.DisplayName, "", manual is not null, manual?.Revenue ?? 0m, manual?.Profit ?? 0m);
            }

            var blockColumn = (DataGridViewComboBoxColumn)_extraAdGrid.Columns["Block"]!;
            blockColumn.Items.Clear();
            foreach (var block in _config.Blocks) blockColumn.Items.Add(block.Name);
            _extraAdGrid.Rows.Clear();
            foreach (var extra in _month.ExtraAdCosts)
            {
                if (!blockColumn.Items.Contains(extra.BlockName)) blockColumn.Items.Add(extra.BlockName);
                _extraAdGrid.Rows.Add(extra.BlockName, extra.ColumnNumber, extra.Amount, extra.Memo);
            }
        }
        finally
        {
            _populating = false;
        }
    }

    /// <summary>화면 입력값을 _month에 옮긴다.</summary>
    private void CollectInputs()
    {
        _month.ExchangeRate = ParseDecimal(_rateBox.Text);
        _month.ExchangeRateNote = _rateNote.Text;

        _month.Costs = [];
        foreach (DataGridViewRow row in _costGrid.Rows)
            if (row.Cells["Item"].Value is string item) _month.Costs[item] = ParseDecimal(row.Cells["Amount"].Value);
        _month.Exports = _exportGrid.Rows.Cast<DataGridViewRow>()
            .Where(r => r.Cells["Market"].Value is string)
            .Select(r => new OnlineReportExportEntry { Market = (string)r.Cells["Market"].Value!, RevenueUsd = ParseDecimal(r.Cells["Revenue"].Value), ProfitUsd = ParseDecimal(r.Cells["Profit"].Value) })
            .ToList();

        _month.FreightTiers = _freightGrid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => new OnlineReportFreightTier { Rate = ParseDecimal(r.Cells["Rate"].Value), Count = (int)ParseDecimal(r.Cells["Count"].Value) })
            .Where(t => t.Rate > 0 && t.Count != 0)
            .GroupBy(t => t.Rate)
            .Select(g => new OnlineReportFreightTier { Rate = g.Key, Count = g.Sum(t => t.Count) })
            .ToList();
        _month.FulfillmentCount = (int)_fulfillmentCount.Value;
        _month.FreightAdjustmentOverride = _overrideCheck.Checked ? ParseDecimal(_overrideBox.Text) : null;

        _month.PartnerOverrides = _partnerGrid.Rows.Cast<DataGridViewRow>()
            .Where(r => r.Cells["Manual"].Value is true)
            .Select(r => new OnlineReportPartnerOverride { DisplayName = (string)r.Cells["Name"].Value!, Revenue = ParseDecimal(r.Cells["Revenue"].Value), Profit = ParseDecimal(r.Cells["Profit"].Value) })
            .ToList();

        _month.ExtraAdCosts = _extraAdGrid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow && r.Cells["Block"].Value is string)
            .Select(r => new OnlineReportExtraAdCost
            {
                BlockName = (string)r.Cells["Block"].Value!,
                ColumnNumber = (int)ParseDecimal(r.Cells["Column"].Value),
                Amount = ParseDecimal(r.Cells["Amount"].Value),
                Memo = Convert.ToString(r.Cells["Memo"].Value) ?? "",
            })
            .Where(x => x.Amount != 0)
            .ToList();
    }

    private void OnInputEdited()
    {
        if (_populating) return;
        MarkDirty();
        Recompute();
    }

    private void MarkDirty()
    {
        if (_populating) return;
        _dirty = true;
        Text = "온라인 매출 보고서 *";
    }

    private void SaveMonth()
    {
        if (string.IsNullOrEmpty(_period)) return;
        CollectInputs();
        _monthRepo.Save(_period, _month);
        _dirty = false;
        Text = "온라인 매출 보고서";
        SetStatus($"{_period} 입력값을 저장했습니다.");
    }

    private bool ConfirmDiscard()
    {
        if (!_dirty || string.IsNullOrEmpty(_period)) return true;
        var answer = MessageBox.Show(this, $"{_period} 입력값이 저장되지 않았습니다. 저장할까요?", "저장 확인", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes) SaveMonth();
        _dirty = false;
        return true;
    }

    // ───────────────────────────── 계산·미리보기 ─────────────────────────────

    private void Recompute()
    {
        if (string.IsNullOrEmpty(_period)) return;
        try
        {
            CollectInputs();
            _report = _dataSource.Build(_period, _config, _month);
            RenderPreview(_report);
        }
        catch (Exception ex)
        {
            SetStatus("계산 오류: " + ex.Message, error: true);
        }
    }

    private void RenderPreview(OnlineReportBuilder.Result report)
    {
        _previewGrid.Rows.Clear();
        void Row(string label, decimal? revenue, decimal? profit, decimal? ad, decimal? net, decimal? ship = null, bool bold = false)
        {
            var margin = revenue is { } r && r != 0 && net is { } n ? (n / r).ToString("0.0%", CultureInfo.InvariantCulture) : "";
            var index = _previewGrid.Rows.Add(label, Fmt(revenue), Fmt(profit), Fmt(ad is null ? null : -ad), Fmt(net), margin, ship is null ? "" : ship.Value.ToString("N1"));
            if (bold) _previewGrid.Rows[index].DefaultCellStyle.Font = new Font(_previewGrid.Font, FontStyle.Bold);
        }

        foreach (var block in report.Blocks)
            Row(block.Block.Name, block.Revenue, block.Profit, block.AdCost, block.Net, block.Block.CountShipping ? block.ShippingCount : null);
        Row("실택배비 조정" + (report.FreightOverridden ? "(수동)" : ""), null, null, null, report.FreightAdjustment);
        Row("온라인 합계", report.Total.Revenue, report.Total.Profit, report.Total.AdCost, report.Total.Net, report.Total.ShippingCount, bold: true);
        _previewGrid.Rows.Add();
        Row("온라인(로켓 제외)", report.OnlineRevenue, null, null, report.OnlineNet);
        Row("로켓+그로스", report.RocketRevenue, null, null, report.RocketNet);
        Row("거래처", report.PartnerRevenue, null, null, report.PartnerProfit);
        Row("수출", report.ExportRevenue, null, null, report.ExportProfit);
        Row("소계", report.SubtotalRevenue, null, null, report.SubtotalProfit, bold: true);
        Row("비용", null, null, null, -report.CostTotal);
        Row("최종", report.SubtotalRevenue, null, null, report.FinalProfit, bold: true);

        // 거래처 마감보드 값 표시.
        _populating = true;
        foreach (DataGridViewRow row in _partnerGrid.Rows)
        {
            var partner = report.Partners.FirstOrDefault(p => p.DisplayName == row.Cells["Name"].Value as string);
            if (partner is null) continue;
            row.Cells["Board"].Value = BoardText(partner);
        }
        _populating = false;

        // 운임 소계·설명.
        _populating = true;
        foreach (DataGridViewRow row in _freightGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var line = report.FreightLines.FirstOrDefault(l => l.Rate == ParseDecimal(row.Cells["Rate"].Value));
            row.Cells["Subtotal"].Value = line is null ? "" : line.Subtotal.ToString("N0");
        }
        _populating = false;
        var f = _config.Freight;
        _freightNote.Text =
            $"출고 {report.FreightLines.Sum(l => l.Count):N0}건 · 운임 합계 {report.FreightLines.Sum(l => l.Rate * l.Count):N0}원 · 풀필 {report.FulfillmentCount:N0}건\r\n" +
            $"계산값 {report.FreightCalculated:N0}원 = Σ(기준 {f.BaseFee:N0} − 운임 − 부자재 {f.PackingSmall:N0}/{f.PackingLarge:N0}) + 풀필 × ({f.BaseFee:N0} − {f.FulfillmentUnitCost:N0})" +
            (report.FreightOverridden ? $"\r\n보고서에는 직접 입력한 {report.FreightAdjustment:N0}원을 씁니다." : "") +
            (string.IsNullOrWhiteSpace(_month.FreightSourceNote) ? "" : $"\r\n원본: {_month.FreightSourceNote}");

        _warningList.Items.Clear();
        foreach (var warning in report.Warnings) _warningList.Items.Add("• " + warning);
        if (report.Warnings.Count == 0) _warningList.Items.Add("점검 메시지 없음");
        SetStatus($"{_period} — 온라인 매출 {report.Total.Revenue:N0}원, 최종 순이익 {report.FinalProfit:N0}원, 점검 메시지 {report.Warnings.Count}건");
    }

    private static string BoardText(OnlineReportBuilder.PartnerResult partner) =>
        partner.IsManual
            ? $"(수동) 마감보드: {string.Join(", ", partner.MatchedParties)}"
            : partner.MatchedParties.Count == 0 ? "마감보드에 없음" : $"{partner.Revenue:N0} / {partner.Profit:N0}";

    // ───────────────────────────── 버튼 동작 ─────────────────────────────

    private async void OnFetchRate(object? sender, EventArgs e)
    {
        if (!DateTime.TryParseExact(_period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) return;
        try
        {
            UseWaitCursor = true;
            SetStatus("하나은행 월평균 환율 조회 중…");
            var rate = await HanaExchangeRateClient.GetMonthlyAverageBaseRateAsync(month.Year, month.Month);
            _populating = true;
            _rateBox.Text = rate.ToString("0.00", CultureInfo.InvariantCulture);
            _populating = false;
            _rateNote.Text = $"하나은행 {month:yyyy년 M월} 평균 매매기준율(최종)";
            MarkDirty();
            Recompute();
            SetStatus($"하나은행 {month:yyyy-MM} USD 월평균 {rate:N2}원을 넣었습니다.");
        }
        catch (Exception ex)
        {
            SetStatus("환율 조회 실패 — 수동으로 입력하세요: " + ex.Message, error: true);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void OnCopyPreviousCosts(object? sender, EventArgs e)
    {
        var previous = _monthRepo.GetLatestPeriodBefore(_period);
        var source = previous is null ? null : _monthRepo.Get(previous);
        if (source is null)
        {
            SetStatus("이전에 저장한 달이 없습니다.");
            return;
        }
        _populating = true;
        foreach (DataGridViewRow row in _costGrid.Rows)
            if (row.Cells["Item"].Value is string item && source.Costs.TryGetValue(item, out var amount)) row.Cells["Amount"].Value = amount;
        _populating = false;
        OnInputEdited();
        SetStatus($"{previous} 비용을 가져왔습니다.");
    }

    private void OnLoadFreightFiles(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "택배사 운송장이력 조회 파일 선택(여러 개 가능)",
            Filter = "Excel 파일|*.xlsx",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var result = FreightFileAggregator.Aggregate(dialog.FileNames);
            _populating = true;
            _freightGrid.Rows.Clear();
            foreach (var tier in result.Tiers) _freightGrid.Rows.Add(tier.Rate, tier.Count, "");
            _populating = false;
            _month.FreightSourceNote = string.Join(", ", result.Files.Select(f =>
                $"{f.FileName} {f.Counted:N0}건" + (f.SkippedNoTrackingNo > 0 ? $"(송장번호 없음 {f.SkippedNoTrackingNo}건 제외)" : "")));
            OnInputEdited();
            SetStatus($"운임 파일 {result.Files.Count}개 — 출고 {result.TotalCount:N0}건, 운임 합계 {result.TotalAmount:N0}원");
        }
        catch (Exception ex)
        {
            _populating = false;
            SetStatus("운임 파일 오류: " + ex.Message, error: true);
        }
    }

    private void OnImportResultFiles(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = $"{_period} 이익분석 결과파일 선택(여러 개 가능)",
            Filter = "이익분석 결과파일|*이익분석*.xlsx|Excel 파일|*.xlsx",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var results = new List<ProfitResultFileImporter.Result>();
        var errors = new List<string>();
        foreach (var file in dialog.FileNames)
        {
            try { results.Add(ProfitResultFileImporter.Read(file)); }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        if (results.Count == 0)
        {
            MessageBox.Show(this, string.Join("\n", errors), "불러오기 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var existing = results.Where(r => _factRepo.HasData(_period, r.ChannelCode)).Select(r => r.ChannelName).ToList();
        var message = $"{_period} 이익 데이터로 저장합니다:\n" +
                      string.Join("\n", results.Select(r => $"  • {r.ChannelName}({r.ChannelCode}) — {r.Facts.Count}개 그룹, 매출 {r.Facts.Sum(f => f.Revenue):N0}원, 배송비 {r.Facts.Sum(f => f.ShippingFee):N0}원")) +
                      (existing.Count > 0 ? $"\n\n이미 있는 채널은 덮어씁니다: {string.Join(", ", existing)}" : "") +
                      (errors.Count > 0 ? $"\n\n읽지 못한 파일:\n{string.Join("\n", errors)}" : "");
        if (MessageBox.Show(this, message, "이익분석 결과파일 불러오기", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        foreach (var r in results) _factRepo.SaveProfitFacts(_period, r.ChannelCode, r.ChannelName, r.Facts);
        Recompute();
        SetStatus($"결과파일 {results.Count}개를 {_period} 이익 데이터로 저장했습니다.");
    }

    private void OnSettings(object? sender, EventArgs e)
    {
        var channels = new ChannelConfigService().Load();
        using var dialog = new OnlineReportSettingsDialog(_configService.Load(), channels, LoadProductGroups());
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) return;
        CollectInputs();
        _configService.Save(dialog.Result);
        _config = dialog.Result;
        PopulateInputs();
        Recompute();
        SetStatus("설정을 저장했습니다.");
    }

    private void OnExport(bool pdf)
    {
        if (_report is null) Recompute();
        if (_report is null) return;
        if (_dirty) SaveMonth();

        var yymm = _period.Length == 7 ? $"{_period[2..4]}년{_period[5..]}월" : _period;
        var defaultName = $"온라인 매출({yymm})_MiniERP2.xlsx";
        var path = ExportHelper.ShowSaveFileDialog(this, "Excel 파일|*.xlsx", defaultName, Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "온라인 매출 보고서 저장");
        if (path is null) return;

        try
        {
            UseWaitCursor = true;
            var layout = OnlineReportExcelWriter.Write(path, _report, _config);
            var output = path;
            if (pdf)
            {
                output = Path.ChangeExtension(path, ".pdf");
                ExcelPdfConverter.Convert(path, output);
            }
            SetStatus($"저장했습니다 — 채널별 페이지 {layout.PageCount}쪽, 인쇄 배율 {layout.ScalePercent}%");
            ExportHelper.ShowPostExportDialog(this, output);
        }
        catch (Exception ex)
        {
            SetStatus("저장 오류: " + ExportHelper.DescribeSaveError(ex), error: true);
            MessageBox.Show(this, ExportHelper.DescribeSaveError(ex), "저장 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    // ───────────────────────────── 도우미 ─────────────────────────────

    private static List<string> LoadProductGroups()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT ProductGroup FROM ItemTable WHERE ProductGroup IS NOT NULL AND TRIM(ProductGroup) <> '' ORDER BY ProductGroup";
        using var reader = cmd.ExecuteReader();
        var list = new List<string>();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    private void SetStatus(string text, bool error = false)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = error ? Color.Firebrick : SystemColors.ControlText;
    }

    private static string Fmt(decimal? value) => value is null ? "" : value.Value.ToString("#,##0;(#,##0);0", CultureInfo.InvariantCulture);

    private static decimal ParseDecimal(object? value) => value switch
    {
        null => 0m,
        decimal d => d,
        int i => i,
        double db => (decimal)db,
        _ => decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture)?.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m,
    };

    private static Button ToolButton(string text, EventHandler onClick, int width = 90)
    {
        var button = new Button { Text = text, Size = new Size(width, 28) };
        button.Click += onClick;
        return button;
    }

    private static DataGridView NewGrid(bool allowAdd = false) => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = allowAdd,
        AllowUserToDeleteRows = allowAdd,
        RowHeadersWidth = 26,
        BackgroundColor = SystemColors.Window,
    };

    private static DataGridViewTextBoxColumn AmountColumn(string name, string header, string format = "N0") => new()
    {
        Name = name,
        HeaderText = header,
        Width = 110,
        ValueType = typeof(decimal),
        DefaultCellStyle = { Format = format, Alignment = DataGridViewContentAlignment.MiddleRight },
    };
}

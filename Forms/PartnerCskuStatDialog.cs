using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감보드 [CSKU별 통계] — 보드에서 고른 거래처들의 마감 라인을 CSKU별(또는 마스터SKU별)로
/// 합산해 품목별 수량·공급가·원가·이익을 보여준다. 매출장 파일 대신 DB 라인(CSKU·원가가 이미 붙어
/// 있음)을 쓰므로 품목명→CSKU 재해석이 필요 없고, 같은 거래처 파일이 두 번 잡히는 일도 없다.
/// 읽기 전용이며, 엑셀 저장 시 "분석결과상세" 시트도 함께 넣어 [CSKU별 통계] 화면에 그대로 불러올 수 있다.
/// </summary>
public class PartnerCskuStatDialog : Form
{
    private readonly SettingsService _settingsService = new();
    private readonly string _period;
    private readonly List<PartnerClosingSummary> _summaries;

    private ComboBox _groupByCombo = new();
    private CheckBox _includeNonItemCheck = new();
    private CheckBox _includeUnshippedCheck = new();
    private CheckBox _vatExcludedCheck = new();
    private ExcelLikeDataGridView _grid = new();
    private Label _totalsLabel = new();
    private List<PartnerCskuStatRow> _rows = [];

    public PartnerCskuStatDialog(string period, List<PartnerClosingSummary> summaries, bool vatExcluded)
    {
        _period = period;
        _summaries = summaries;
        InitializeComponent(vatExcluded);
        FormManager.ApplyBoundsTracking(this);
        Load += (s, e) => RunAggregate();
    }

    private void InitializeComponent(bool vatExcluded)
    {
        Text = $"CSKU별 통계 — {_period} 거래처 {_summaries.Count}곳";
        Size = new Size(1150, 680);
        StartPosition = FormStartPosition.CenterParent;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5), WrapContents = false };
        _groupByCombo = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
        _groupByCombo.Items.AddRange(["CSKU별", "마스터SKU별"]);
        _groupByCombo.SelectedIndex = 0;
        _groupByCombo.SelectedIndexChanged += (s, e) => RunAggregate();

        _includeNonItemCheck = new CheckBox { Text = "택배비·할인 라인 포함", AutoSize = true, Checked = true, Padding = new Padding(10, 5, 0, 0) };
        _includeNonItemCheck.CheckedChanged += (s, e) => RunAggregate();
        _includeUnshippedCheck = new CheckBox { Text = "미출고 포함", AutoSize = true, Checked = false, Padding = new Padding(10, 5, 0, 0) };
        _includeUnshippedCheck.CheckedChanged += (s, e) => RunAggregate();
        _vatExcludedCheck = new CheckBox { Text = "VAT 별도", AutoSize = true, Checked = vatExcluded, Padding = new Padding(10, 5, 0, 0) };
        _vatExcludedCheck.CheckedChanged += (s, e) => RunAggregate();

        var btnExport = new Button { Text = "엑셀 저장", Size = new Size(90, 28), Margin = new Padding(16, 3, 3, 3) };
        btnExport.Click += OnExportClick;

        toolbar.Controls.Add(new Label { Text = "묶음:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        toolbar.Controls.Add(_groupByCombo);
        toolbar.Controls.Add(_includeNonItemCheck);
        toolbar.Controls.Add(_includeUnshippedCheck);
        toolbar.Controls.Add(_vatExcludedCheck);
        toolbar.Controls.Add(btnExport);

        _grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "PartnerCskuStatDialog.Grid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
        };
        var money = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight };
        var right = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight };
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "CSKU", Name = "CskuCode", DataPropertyName = "CskuCode", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "마스터SKU", Name = "MasterSku", DataPropertyName = "MasterSku", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "품목", Name = "ItemName", DataPropertyName = "ItemName", Width = 230 },
            new DataGridViewTextBoxColumn { HeaderText = "수량", Name = "Qty", DataPropertyName = "Qty", Width = 60, DefaultCellStyle = money },
            new DataGridViewTextBoxColumn { HeaderText = "공급가합", Name = "Supply", DataPropertyName = "Supply", Width = 95, DefaultCellStyle = money },
            new DataGridViewTextBoxColumn { HeaderText = "원가합", Name = "Cost", DataPropertyName = "Cost", Width = 95, DefaultCellStyle = money },
            new DataGridViewTextBoxColumn { HeaderText = "이익액", Name = "Profit", DataPropertyName = "Profit", Width = 95, DefaultCellStyle = money },
            new DataGridViewTextBoxColumn { HeaderText = "마진율", Name = "MarginRateText", DataPropertyName = "MarginRateText", Width = 60, DefaultCellStyle = right },
            new DataGridViewTextBoxColumn { HeaderText = "이익비중", Name = "ProfitShareText", DataPropertyName = "ProfitShareText", Width = 65, DefaultCellStyle = right },
            new DataGridViewTextBoxColumn { HeaderText = "거래처수", Name = "PartyCount", DataPropertyName = "PartyCount", Width = 60, DefaultCellStyle = right },
            new DataGridViewTextBoxColumn { HeaderText = "거래처", Name = "Parties", DataPropertyName = "Parties", Width = 200, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _grid.CellFormatting += (s, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is DisplayRow { Source.IsNonItem: true } && e.CellStyle != null)
                e.CellStyle.ForeColor = Color.Gray;
        };

        _totalsLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        mainLayout.Controls.Add(toolbar, 0, 0);
        mainLayout.Controls.Add(_grid, 0, 1);
        mainLayout.Controls.Add(_totalsLabel, 0, 2);
        Controls.Add(mainLayout);
    }

    private PartnerCskuGroupBy GroupBy => _groupByCombo.SelectedIndex == 1 ? PartnerCskuGroupBy.MasterSku : PartnerCskuGroupBy.Csku;

    private List<PartnerCskuStatInput> CollectInputs()
    {
        var includeNonItem = _includeNonItemCheck.Checked;
        var includeUnshipped = _includeUnshippedCheck.Checked;
        return _summaries
            .SelectMany(s => s.Lines.Select(l => new PartnerCskuStatInput(s.PartyName, l, false))
                .Concat(includeUnshipped ? s.UnshippedLines.Select(l => new PartnerCskuStatInput(s.PartyName, l, true)) : []))
            .Where(i => includeNonItem || !PartnerCskuStatAggregator.IsNonItemLine(i.Line))
            .ToList();
    }

    private void RunAggregate()
    {
        _rows = PartnerCskuStatAggregator.Aggregate(CollectInputs(), GroupBy);
        var vatExcluded = _vatExcludedCheck.Checked;
        var totalProfit = _rows.Sum(r => r.Profit);
        _grid.DataSource = new BindingList<DisplayRow>(_rows.Select(r => new DisplayRow(r, vatExcluded, totalProfit)).ToList());

        var supply = _rows.Sum(r => r.Supply);
        var cost = _rows.Sum(r => r.Cost);
        var freight = _summaries.Sum(s => s.FreightAllocated);
        string M(decimal v) => VatCalculator.ToDisplay(v, vatExcluded).ToString("N0");
        var rate = supply == 0 ? "-" : $"{totalProfit / supply * 100:0.0}%";

        // 금액만 입력된 수동 거래처(라인 없음)는 품목을 알 수 없어 통계에서 빠진다 — 조용히 빼지 않고 알린다.
        var amountOnly = _summaries.Where(s => s.Lines.Count == 0 && s.TotalSupply != 0).Select(s => s.PartyName).ToList();
        var line1 = $"{_rows.Count}개 품목 | 수량 {_rows.Sum(r => r.Qty):N0} | 공급가 {M(supply)} | 원가 {M(cost)} | 이익 {M(totalProfit)} ({rate})"
            + (freight != 0 ? $" | 배부운임 {M(freight)} → 운임차감 이익 {M(totalProfit - freight)}" : "")
            + $"   [{(vatExcluded ? "VAT별도" : "VAT포함")} 기준]";
        var line2 = amountOnly.Count == 0
            ? "이익액 = 라인 이익(운임 미반영). 회색 = 택배비·할인 라인."
            : $"⚠ 라인 없이 금액만 입력된 거래처 {amountOnly.Count}곳은 품목을 알 수 없어 제외: {string.Join(", ", amountOnly)}";
        _totalsLabel.Text = line1 + Environment.NewLine + line2;
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show("내보낼 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"거래처_CSKU별통계_{_period}_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("PartnerCskuStatExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        if (filePath == null) return;
        _settingsService.SetLastFolder("PartnerCskuStatExport", Path.GetDirectoryName(filePath)!);

        try
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var vatExcluded = _vatExcludedCheck.Checked;
            decimal V(decimal v) => Math.Round(VatCalculator.ToDisplay(v, vatExcluded), 0, MidpointRounding.AwayFromZero);

            // 1) 품목별 합산 — 화면 그대로.
            var ws = package.Workbook.Worksheets.Add(GroupBy == PartnerCskuGroupBy.MasterSku ? "마스터SKU별통계" : "CSKU별통계");
            var headers = new[] { "CSKU", "마스터SKU", "품목", "수량", "공급가합", "원가합", "이익액", "마진율", "거래처수", "거래처" };
            for (var i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var r = i + 2;
                ws.Cells[r, 1].Value = row.CskuCode;
                ws.Cells[r, 2].Value = row.MasterSku;
                ws.Cells[r, 3].Value = row.ItemName;
                ws.Cells[r, 4].Value = row.Qty;
                ws.Cells[r, 5].Value = V(row.Supply);
                ws.Cells[r, 6].Value = V(row.Cost);
                ws.Cells[r, 7].Value = V(row.Profit);
                if (row.MarginRate is { } rate) ws.Cells[r, 8].Value = rate;
                ws.Cells[r, 9].Value = row.PartyCount;
                ws.Cells[r, 10].Value = row.Parties;
            }
            var totalRow = _rows.Count + 2;
            ws.Cells[totalRow, 3].Value = "합계";
            ws.Cells[totalRow, 4].Formula = $"SUM(D2:D{totalRow - 1})";
            ws.Cells[totalRow, 5].Formula = $"SUM(E2:E{totalRow - 1})";
            ws.Cells[totalRow, 6].Formula = $"SUM(F2:F{totalRow - 1})";
            ws.Cells[totalRow, 7].Formula = $"SUM(G2:G{totalRow - 1})";
            ws.Cells[totalRow, 8].Formula = $"IF(E{totalRow}=0,\"\",G{totalRow}/E{totalRow})";
            ws.Cells[totalRow, 1, totalRow, headers.Length].Style.Font.Bold = true;
            ws.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
            ws.Cells[2, 4, totalRow, 7].Style.Numberformat.Format = "#,##0";
            ws.Cells[2, 8, totalRow, 8].Style.Numberformat.Format = "0.0%";
            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            ws.View.FreezePanes(2, 1);

            // 2) 거래처×품목 상세 — 어느 거래처에서 얼마 남겼는지 추적용.
            var inputs = CollectInputs();
            var detail = package.Workbook.Worksheets.Add("거래처별상세");
            var detailHeaders = new[] { "거래처", "CSKU", "마스터SKU", "품목", "수량", "공급가합", "원가합", "이익액", "마진율" };
            for (var i = 0; i < detailHeaders.Length; i++) detail.Cells[1, i + 1].Value = detailHeaders[i];
            var detailRows = inputs
                .GroupBy(i => (i.PartyName, Key: PartnerCskuStatAggregator.KeyOf(i.Line, GroupBy)))
                .Select(g => new
                {
                    g.Key.PartyName,
                    Rows = PartnerCskuStatAggregator.Aggregate(g, GroupBy).Single(),
                })
                .OrderBy(x => x.PartyName, StringComparer.Ordinal)
                .ThenByDescending(x => x.Rows.Profit)
                .ToList();
            for (var i = 0; i < detailRows.Count; i++)
            {
                var d = detailRows[i];
                var r = i + 2;
                detail.Cells[r, 1].Value = d.PartyName;
                detail.Cells[r, 2].Value = d.Rows.CskuCode;
                detail.Cells[r, 3].Value = d.Rows.MasterSku;
                detail.Cells[r, 4].Value = d.Rows.ItemName;
                detail.Cells[r, 5].Value = d.Rows.Qty;
                detail.Cells[r, 6].Value = V(d.Rows.Supply);
                detail.Cells[r, 7].Value = V(d.Rows.Cost);
                detail.Cells[r, 8].Value = V(d.Rows.Profit);
                if (d.Rows.MarginRate is { } rate) detail.Cells[r, 9].Value = rate;
            }
            detail.Cells[1, 1, 1, detailHeaders.Length].Style.Font.Bold = true;
            if (detailRows.Count > 0)
            {
                detail.Cells[2, 5, detailRows.Count + 1, 8].Style.Numberformat.Format = "#,##0";
                detail.Cells[2, 9, detailRows.Count + 1, 9].Style.Numberformat.Format = "0.0%";
            }
            detail.Cells[detail.Dimension.Address].AutoFitColumns();
            detail.View.FreezePanes(2, 1);

            // 3) [CSKU별 통계] 화면이 읽는 "분석결과상세" 형식(CskuStatFileParser 필수 헤더) — 온라인 채널
            //    통계와 한 배치로 합쳐 볼 수 있게 한다. 거래처 매출은 정산=매출, 배송비·입출고비 0으로 둔다.
            var analysis = package.Workbook.Worksheets.Add("분석결과상세");
            var analysisHeaders = new[] { "채널", "상품그룹", "상품명", "옵션명", "매핑SKU", "수량", "매출액", "정산액", "배송비", "입출고비", "이익액", "상태" };
            for (var i = 0; i < analysisHeaders.Length; i++) analysis.Cells[1, i + 1].Value = analysisHeaders[i];
            var channelByParty = _summaries
                .GroupBy(s => s.PartyName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => ChannelLabel(g.First()), StringComparer.Ordinal);
            var aRow = 2;
            foreach (var g in inputs.GroupBy(i => (i.PartyName, Csku: PartnerCskuStatAggregator.KeyOf(i.Line, PartnerCskuGroupBy.Csku))))
            {
                var agg = PartnerCskuStatAggregator.Aggregate(g, PartnerCskuGroupBy.Csku).Single();
                analysis.Cells[aRow, 1].Value = channelByParty.GetValueOrDefault(g.Key.PartyName, g.Key.PartyName);
                analysis.Cells[aRow, 2].Value = "거래처";
                analysis.Cells[aRow, 3].Value = agg.ItemName;
                analysis.Cells[aRow, 4].Value = g.Key.PartyName;
                analysis.Cells[aRow, 5].Value = agg.IsNonItem ? "" : agg.CskuCode;
                analysis.Cells[aRow, 6].Value = agg.Qty;
                analysis.Cells[aRow, 7].Value = V(agg.Supply);
                analysis.Cells[aRow, 8].Value = V(agg.Supply);
                analysis.Cells[aRow, 9].Value = 0;
                analysis.Cells[aRow, 10].Value = 0;
                analysis.Cells[aRow, 11].Value = V(agg.Profit);
                analysis.Cells[aRow, 12].Value = agg.IsNonItem ? "제외(배송비 등)" : "매핑(1:1)";
                aRow++;
            }
            analysis.Cells[1, 1, 1, analysisHeaders.Length].Style.Font.Bold = true;
            analysis.Cells[analysis.Dimension.Address].AutoFitColumns();

            ExportHelper.SaveExcel(package, filePath);
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀 저장 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>채널 경유 거래처는 채널코드, 수동 거래처는 거래처명을 "채널"로 쓴다.</summary>
    private static string ChannelLabel(PartnerClosingSummary s) =>
        s.PartyKey.StartsWith("CH:", StringComparison.Ordinal) ? s.PartyKey["CH:".Length..] : s.PartyName;

    private sealed class DisplayRow(PartnerCskuStatRow source, bool vatExcluded, decimal totalProfit)
    {
        public PartnerCskuStatRow Source { get; } = source;
        public string CskuCode { get; } = source.CskuCode;
        public string MasterSku { get; } = source.MasterSku;
        public string ItemName { get; } = source.ItemName;
        public decimal Qty { get; } = source.Qty;
        public decimal Supply { get; } = VatCalculator.ToDisplay(source.Supply, vatExcluded);
        public decimal Cost { get; } = VatCalculator.ToDisplay(source.Cost, vatExcluded);
        public decimal Profit { get; } = VatCalculator.ToDisplay(source.Profit, vatExcluded);
        public string MarginRateText { get; } = source.MarginRate is { } rate ? $"{rate * 100:0.0}%" : "-";
        public string ProfitShareText { get; } = totalProfit == 0 ? "-" : $"{source.Profit / totalProfit * 100:0.0}%";
        public int PartyCount { get; } = source.PartyCount;
        public string Parties { get; } = source.Parties;
    }
}

using System.Drawing;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.Utils;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace MiniERP2.Exporters;

/// <summary>
/// 온라인 매출 종합보고서 엑셀을 만든다.
/// - 1p "요약": A4 가로 한 장(최종 결과·비용·수출 / 거래처 / 상품그룹·CSKU 매출 변동).
/// - 2p~ "채널별": A4 세로. 세로는 항상 한 장 — 기준 배율(기본 72%)로 넘치면 그만큼 자동 축소.
///   가로는 품목 열 경계에서 페이지를 나누고(판매처·구분 열 반복), 택배비·합계·비고는 마지막 쪽에 함께 둔다.
///   엑셀의 "페이지에 맞추기"는 수동 페이지 나눔을 무시하므로 쓰지 않고, 배율을 직접 계산해 고정한다.
/// - "택배비": 실택배비 조정의 운임별 계산 근거(참고용).
/// 합계·순이익·마진율·요약 숫자는 수식으로 넣어, 출력 후 엑셀에서 값을 고쳐도 다시 계산되게 한다.
/// </summary>
public static class OnlineReportExcelWriter
{
    private const string AmountFormat = "#,##0;[Red](#,##0);0";
    private const string PercentFormat = "0.0%;[Red]-0.0%;0.0%";
    private const string RatioFormat = "0%";
    private const string UsdFormat = "\"US$\"#,##0.00;[Red]-\"US$\"#,##0.00;\"US$\"0";

    private static readonly Color HeaderFill = Color.FromArgb(217, 217, 217);
    private static readonly Color TotalFill = Color.FromArgb(242, 242, 242);
    private static readonly Color HighlightFill = Color.FromArgb(255, 243, 107);
    private static readonly Color ManualFill = Color.FromArgb(255, 248, 214);
    private static readonly Color UpFill = Color.FromArgb(238, 247, 241);
    private static readonly Color DownFill = Color.FromArgb(251, 239, 239);
    private static readonly Color UpText = Color.FromArgb(31, 111, 58);
    private static readonly Color DownText = Color.FromArgb(192, 0, 0);
    private static readonly Color GridLine = Color.FromArgb(150, 150, 150);
    private static readonly Color RuleLine = Color.FromArgb(64, 64, 64);

    // 채널별 시트 치수(기존 수동 엑셀 2609 시트 값).
    private const double TitleColumnAWidth = 7.2; // 기존 6.5에서 10% 넓힘(2026-10-02 요청)
    private const double TitleColumnBWidth = 5.6;
    private const double DataColumnWidth = 12.5;
    private const double TotalColumnWidth = 13.5;
    private const double NoteColumnWidth = 13;
    private const double BlockRowHeight = 21.95;
    private const double Row1Height = 18;
    private const double Row2Height = 20.25;
    private const double Row3Height = 22.5;

    // 엑셀이 실제로 찍는 크기는 배율×행 높이보다 약 10% 크다(2026-10-02 9월 시험 출력에서 72% 지정 → 실측 79%).
    // 배율·쪽 나눔 계산에 이만큼 여유를 둔다.
    private const double PrintGrowthFactor = 1.1;

    // A4(pt)와 여백(inch).
    private const double A4ShortPt = 595.3;
    private const double A4LongPt = 841.9;
    private const double SideMarginInch = 0.25;
    private const double TopMarginInch = 0.35;
    private const double BottomMarginInch = 0.45;

    public record Layout(int ScalePercent, IReadOnlyList<int> PageBreakAfterColumns, int PageCount);

    public static Layout Write(string filePath, OnlineReportBuilder.Result report, OnlineReportConfig config)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var normal = package.Workbook.Styles.NamedStyles.First().Style.Font;
        normal.Name = config.FontName;
        normal.Size = 10;

        var summary = package.Workbook.Worksheets.Add("요약");
        var channel = package.Workbook.Worksheets.Add("채널별");
        var freight = package.Workbook.Worksheets.Add("택배비");

        var channelCells = WriteChannelSheet(channel, report, config);
        WriteSummarySheet(summary, report, config, channelCells);
        WriteFreightSheet(freight, report, config);

        package.Workbook.Calculate();
        ExportHelper.SaveExcel(package, filePath);
        return channelCells.Layout;
    }

    // ───────────────────────────── 채널별 시트 ─────────────────────────────

    /// <summary>요약 시트가 수식으로 참조할 채널별 시트의 셀 주소.</summary>
    private record ChannelCells(string TotalRevenue, string TotalNet, IReadOnlyList<string> RocketRevenue, IReadOnlyList<string> RocketNet, Layout Layout);

    private static ChannelCells WriteChannelSheet(ExcelWorksheet ws, OnlineReportBuilder.Result report, OnlineReportConfig config)
    {
        var groupCount = report.Columns.Count;
        const int firstDataCol = 3;
        var shipCol = firstDataCol + groupCount;
        var totalCol = shipCol + 1;
        var noteCol = totalCol + 1;
        var lastGroupCol = shipCol - 1;
        string Col(int c) => ExcelCellBase.GetAddressCol(c);

        // 머리글 1~3행.
        ws.Cells[1, 1].Value = "온라인 판매  " + OnlineReportBuilder.FormatPeriodLabel(report.Period);
        ws.Cells[1, 1].Style.Font.Size = 11;
        ws.Cells[1, 1].Style.Font.Bold = true;
        for (int i = 0; i < groupCount; i++)
        {
            var numberCell = ws.Cells[2, firstDataCol + i];
            numberCell.Value = report.Columns[i].Number;
            numberCell.Style.Font.Size = 8;
            numberCell.Style.Font.Color.SetColor(Color.Gray);
            numberCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            numberCell.Style.VerticalAlignment = ExcelVerticalAlignment.Bottom;
        }
        ws.Cells[2, totalCol].Value = "VAT포함";
        ws.Cells[2, totalCol].Style.Font.Size = 9;
        ws.Cells[2, totalCol].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        ws.Cells[2, totalCol].Style.VerticalAlignment = ExcelVerticalAlignment.Bottom;

        var headers = new List<string> { "판매처", "구분" };
        headers.AddRange(report.Columns.Select(c => c.Header));
        headers.AddRange([OnlineReportBuilder.ShippingColumnHeader, "합계", "비고"]);
        for (int c = 1; c <= headers.Count; c++)
        {
            var cell = ws.Cells[3, c];
            cell.Value = headers[c - 1];
            StyleHeader(cell);
            cell.Style.Border.Left.Style = cell.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            cell.Style.Border.Left.Color.SetColor(RuleLine);
            cell.Style.Border.Right.Color.SetColor(RuleLine);
        }
        ws.Cells[3, 1, 3, headers.Count].Style.Border.BorderAround(ExcelBorderStyle.Medium);

        // 블록.
        var row = 4;
        var blockStarts = new List<int>();
        foreach (var block in report.Blocks)
        {
            blockStarts.Add(row);
            WriteBlock(ws, row, block.Block.Name, block, firstDataCol, groupCount, shipCol, totalCol, noteCol, isTotal: false, blockStarts: null, report);
            row += 6;
        }
        var totalStart = row;
        WriteBlock(ws, row, "합계", report.Total, firstDataCol, groupCount, shipCol, totalCol, noteCol, isTotal: true, blockStarts, report);
        var lastRow = totalStart + 6;

        // 열 너비·행 높이.
        ws.Column(1).Width = TitleColumnAWidth;
        ws.Column(2).Width = TitleColumnBWidth;
        for (int c = firstDataCol; c <= shipCol; c++) ws.Column(c).Width = DataColumnWidth;
        ws.Column(totalCol).Width = TotalColumnWidth;
        ws.Column(noteCol).Width = NoteColumnWidth;
        ws.Row(1).Height = Row1Height;
        ws.Row(2).Height = Row2Height;
        ws.Row(3).Height = Row3Height;
        for (int r = 4; r <= lastRow; r++) ws.Row(r).Height = BlockRowHeight;

        ws.View.FreezePanes(4, 3);
        ws.View.ShowGridLines = false;

        // 인쇄 설정 — 세로 한 장 맞춤 배율 + 품목 경계 페이지 나눔.
        var rowHeights = Row1Height + Row2Height + Row3Height + (lastRow - 3) * BlockRowHeight;
        var layout = ComputeChannelLayout(config, rowHeights, groupCount);
        var printer = ws.PrinterSettings;
        printer.PaperSize = ePaperSize.A4;
        printer.Orientation = eOrientation.Portrait;
        printer.FitToPage = false;
        printer.Scale = layout.ScalePercent;
        printer.LeftMargin = SideMarginInch;
        printer.RightMargin = SideMarginInch;
        printer.TopMargin = TopMarginInch;
        printer.BottomMargin = BottomMarginInch;
        printer.HeaderMargin = 0.1;
        printer.FooterMargin = 0.2;
        printer.PrintArea = ws.Cells[1, 1, lastRow, noteCol];
        printer.RepeatColumns = new ExcelAddress("$A:$B");
        foreach (var breakAfter in layout.PageBreakAfterColumns) ws.Column(breakAfter).PageBreak = true;
        ws.HeaderFooter.OddFooter.RightAlignedText = "&8MiniERP2 온라인 매출 &P / &N";

        var totalRevenueRow = totalStart + 1;
        var totalNetRow = totalStart + 4;
        var rocketBlocks = report.Blocks.Select((b, i) => (b, start: blockStarts[i])).Where(x => x.b.Block.IsRocketGrowth).ToList();
        return new ChannelCells(
            $"'{ws.Name}'!{Col(totalCol)}{totalRevenueRow}",
            $"'{ws.Name}'!{Col(totalCol)}{totalNetRow}",
            rocketBlocks.Select(x => $"'{ws.Name}'!{Col(totalCol)}{x.start + 1}").ToList(),
            rocketBlocks.Select(x => $"'{ws.Name}'!{Col(totalCol)}{x.start + 4}").ToList(),
            layout);
    }

    private static void WriteBlock(ExcelWorksheet ws, int top, string name, OnlineReportBuilder.BlockResult block,
        int firstDataCol, int groupCount, int shipCol, int totalCol, int noteCol, bool isTotal, IReadOnlyList<int>? blockStarts,
        OnlineReportBuilder.Result report)
    {
        string Col(int c) => ExcelCellBase.GetAddressCol(c);
        string[] labels = ["수량", "매출액", "판매분\n이익액", "광고비", "순이익", "마진율"];
        var rowCount = isTotal ? 7 : 6;
        var qtyRow = top; var revRow = top + 1; var profitRow = top + 2; var adRow = top + 3; var netRow = top + 4; var marginRow = top + 5;

        // A열: 판매처 이름(1~4행 병합), "광고", 광고비율.
        var nameRange = ws.Cells[top, 1, top + 3, 1];
        nameRange.Merge = true;
        nameRange.Value = name;
        nameRange.Style.WrapText = true;
        nameRange.Style.Font.Bold = true;
        nameRange.Style.Font.Size = 10;
        nameRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        nameRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        ws.Cells[netRow, 1].Value = "광고";
        ws.Cells[netRow, 1].Style.Font.Bold = true;
        ws.Cells[netRow, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        var ratioCell = ws.Cells[marginRow, 1];
        ratioCell.Formula = $"IFERROR(-{Col(totalCol)}{adRow}/{Col(totalCol)}{revRow},\"\")";
        ratioCell.Style.Numberformat.Format = RatioFormat;
        ratioCell.Style.Font.Bold = true;
        ratioCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        if (isTotal) ws.Cells[marginRow + 1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        for (int i = 0; i < rowCount; i++)
        {
            var label = ws.Cells[top + i, 2];
            label.Value = i < labels.Length ? labels[i] : "광고비율";
            label.Style.Font.Size = 7;
            label.Style.WrapText = true;
            label.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            label.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        }

        // 품목 열 + 택배비 열.
        for (int i = 0; i <= groupCount; i++)
        {
            var c = firstDataCol + i;
            var isShip = c == shipCol;
            if (isTotal && blockStarts is not null)
            {
                string SumOf(int offset) => string.Join("+", blockStarts.Select(s => $"{Col(c)}{s + offset}"));
                ws.Cells[qtyRow, c].Formula = SumOf(0);
                ws.Cells[revRow, c].Formula = SumOf(1);
                ws.Cells[profitRow, c].Formula = SumOf(2);
                ws.Cells[adRow, c].Formula = SumOf(3);
                if (isShip) ws.Cells[netRow, c].Value = (double)block.ShippingNet;
                else ws.Cells[netRow, c].Formula = $"{Col(c)}{profitRow}+{Col(c)}{adRow}";
            }
            else if (isShip)
            {
                ws.Cells[qtyRow, c].Value = (double)block.ShippingCount;
                ws.Cells[revRow, c].Value = (double)block.ShippingRevenue;
                ws.Cells[profitRow, c].Value = 0d;
                ws.Cells[adRow, c].Value = 0d;
                ws.Cells[netRow, c].Formula = $"{Col(c)}{profitRow}+{Col(c)}{adRow}";
            }
            else
            {
                var cell = block.Cells[i];
                ws.Cells[qtyRow, c].Value = (double)cell.Qty;
                ws.Cells[revRow, c].Value = (double)cell.Revenue;
                ws.Cells[profitRow, c].Value = (double)cell.Profit;
                ws.Cells[adRow, c].Value = (double)-cell.AdCost;
                ws.Cells[netRow, c].Formula = $"{Col(c)}{profitRow}+{Col(c)}{adRow}";
            }
            ws.Cells[marginRow, c].Formula = $"IFERROR({Col(c)}{netRow}/{Col(c)}{revRow},\"\")";
            if (isTotal) ws.Cells[marginRow + 1, c].Formula = $"IFERROR(-{Col(c)}{adRow}/{Col(c)}{revRow},\"\")";
        }

        // 합계 열: 수량은 품목 열만(택배 건수 제외), 금액은 택배비 열 포함.
        var firstGroup = Col(firstDataCol);
        var lastGroup = Col(shipCol - 1);
        var ship = Col(shipCol);
        ws.Cells[qtyRow, totalCol].Formula = $"SUM({firstGroup}{qtyRow}:{lastGroup}{qtyRow})";
        foreach (var r in new[] { revRow, profitRow, adRow, netRow })
            ws.Cells[r, totalCol].Formula = $"SUM({firstGroup}{r}:{ship}{r})";
        ws.Cells[marginRow, totalCol].Formula = $"IFERROR({Col(totalCol)}{netRow}/{Col(totalCol)}{revRow},\"\")";
        if (isTotal) ws.Cells[marginRow + 1, totalCol].Formula = $"IFERROR(-{Col(totalCol)}{adRow}/{Col(totalCol)}{revRow},\"\")";

        // 비고.
        if (block.ExtraAdNotes.Count > 0) ws.Cells[adRow, noteCol].Value = "추가: " + string.Join(", ", block.ExtraAdNotes);
        if (isTotal)
        {
            ws.Cells[adRow, noteCol].Value = "부자재비 포함";
            ws.Cells[netRow, noteCol].Value = report.FreightOverridden ? "실택배비 조정(수동)" : "실택배비 조정";
        }

        // 서식.
        var lastRowOfBlock = top + rowCount - 1;
        var body = ws.Cells[top, firstDataCol, lastRowOfBlock, totalCol];
        body.Style.Font.Size = 10;
        body.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        body.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        ws.Cells[qtyRow, firstDataCol, netRow, totalCol].Style.Numberformat.Format = AmountFormat;
        ws.Cells[marginRow, firstDataCol, lastRowOfBlock, totalCol].Style.Numberformat.Format = PercentFormat;
        ws.Cells[netRow, firstDataCol, netRow, totalCol].Style.Font.Bold = true;
        ws.Cells[top, totalCol, lastRowOfBlock, totalCol].Style.Font.Bold = true;
        ws.Cells[top, noteCol, lastRowOfBlock, noteCol].Style.Font.Size = 8;
        ws.Cells[top, noteCol, lastRowOfBlock, noteCol].Style.WrapText = true;
        if (isTotal)
        {
            ws.Cells[top, totalCol, lastRowOfBlock, totalCol].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[top, totalCol, lastRowOfBlock, totalCol].Style.Fill.BackgroundColor.SetColor(TotalFill);
        }

        // 선: 블록 안 행 구분(수량·매출액…)은 점선, 품목 열 구분은 실선, 채널 블록 경계는 굵은 선.
        // 판매처 이름 칸(A열 1~4행 병합)과 "광고"/광고비율 칸 사이도 점선으로 둔다.
        var whole = ws.Cells[top, 1, lastRowOfBlock, noteCol];
        whole.Style.Border.Left.Style = ExcelBorderStyle.Thin;
        whole.Style.Border.Right.Style = ExcelBorderStyle.Thin;
        whole.Style.Border.Top.Style = ExcelBorderStyle.Dotted;
        whole.Style.Border.Bottom.Style = ExcelBorderStyle.Dotted;
        whole.Style.Border.Left.Color.SetColor(RuleLine);
        whole.Style.Border.Right.Color.SetColor(RuleLine);
        whole.Style.Border.Top.Color.SetColor(RuleLine);
        whole.Style.Border.Bottom.Color.SetColor(RuleLine);
        whole.Style.Border.BorderAround(ExcelBorderStyle.Medium, Color.Black);
    }

    /// <summary>
    /// 채널별 시트 배율과 열 페이지 나눔을 계산한다. 세로 한 장에 들어가도록 기준 배율에서 필요하면 줄이고,
    /// 그 배율에서 한 쪽에 들어가는 품목 열 수만큼씩 나눈다. 택배비·합계·비고 3열은 마지막 쪽에 같이 둔다.
    /// </summary>
    public static Layout ComputeChannelLayout(OnlineReportConfig config, double totalRowHeightPt, int groupCount)
    {
        var usableHeight = A4LongPt - (TopMarginInch + BottomMarginInch) * 72;
        var heightScale = (int)Math.Floor(usableHeight / (totalRowHeightPt * PrintGrowthFactor) * 100);
        var scale = Math.Clamp(Math.Min(config.BaseScalePercent, heightScale), 10, 400);

        var mdw = MaxDigitWidthPx(config.FontName);
        double WidthPt(double chars) => Math.Truncate(chars * mdw + 5) * 0.75;
        var usableWidth = (A4ShortPt - SideMarginInch * 2 * 72) / (scale / 100.0 * PrintGrowthFactor);
        var available = usableWidth - WidthPt(TitleColumnAWidth) - WidthPt(TitleColumnBWidth);
        var perPage = Math.Max(1, (int)Math.Floor(available / WidthPt(DataColumnWidth)));

        // 마지막 쪽에 들어갈 꼬리 3열(택배비·합계·비고)의 폭을 품목 열 개수로 환산.
        var tailWidth = WidthPt(DataColumnWidth) + WidthPt(TotalColumnWidth) + WidthPt(NoteColumnWidth);
        var tailSlots = (int)Math.Ceiling(tailWidth / WidthPt(DataColumnWidth));

        var breaks = PlanColumnBreaks(groupCount, perPage, tailSlots);
        return new Layout(scale, breaks, breaks.Count + 1);
    }

    /// <summary>
    /// 품목 열을 쪽마다 perPage개씩 채우되, 마지막 쪽에는 꼬리(택배비·합계·비고, tailSlots칸)가 품목 1개 이상과
    /// 함께 들어가도록 끊는다. 반환값은 "이 열 다음에서 쪽을 나눈다"의 열 번호(C열 = 3부터).
    /// </summary>
    public static List<int> PlanColumnBreaks(int groupCount, int perPage, int tailSlots)
    {
        const int firstDataCol = 3;
        var breaks = new List<int>();
        var remaining = groupCount;
        var col = firstDataCol;
        while (remaining > 0 && remaining + tailSlots > perPage)
        {
            // 남은 품목이 한 쪽에 들어가도 꼬리까지는 안 들어가면, 마지막 쪽에 품목 1개를 남기고 끊는다.
            var take = remaining > perPage ? perPage : Math.Max(1, remaining - 1);
            if (take >= remaining) break;
            col += take;
            remaining -= take;
            breaks.Add(col - 1);
        }
        return breaks;
    }

    /// <summary>엑셀 열 너비(문자 수)를 픽셀로 바꿀 때 쓰는 기본 글꼴의 숫자 최대 폭(96dpi, 10pt 기준 근사).</summary>
    private static double MaxDigitWidthPx(string fontName) => fontName switch
    {
        "맑은 고딕" or "Malgun Gothic" => 7,
        "Pretendard" => 7,
        "나눔고딕" or "NanumGothic" or "나눔고딕OTF" => 7,
        _ => 7,
    };

    // ───────────────────────────── 요약 시트 ─────────────────────────────

    private static void WriteSummarySheet(ExcelWorksheet ws, OnlineReportBuilder.Result report, OnlineReportConfig config, ChannelCells channel)
    {
        const int lastCol = 14;
        for (int c = 1; c <= lastCol; c++) ws.Column(c).Width = 11.5;
        ws.Column(3).Width = 13.5; // 최종 결과 매출액·순이익(억 단위)
        ws.Column(4).Width = 13.5;
        ws.View.ShowGridLines = false;

        // 머리글.
        ws.Cells[1, 1].Value = "종합보고 요약";
        ws.Cells[1, 1].Style.Font.Size = 18;
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 4].Value = "온라인 판매 · " + OnlineReportBuilder.FormatPeriodLabel(report.Period);
        ws.Cells[1, 4].Style.Font.Size = 10;
        ws.Cells[1, 4].Style.VerticalAlignment = ExcelVerticalAlignment.Bottom;
        ws.Cells[1, 10].Value = "VAT포함 · 단위: 원 · 환율 USD";
        ws.Cells[1, 10].Style.Font.Size = 9;
        ws.Cells[1, 10].Style.VerticalAlignment = ExcelVerticalAlignment.Bottom;
        var rateHeader = ws.Cells[1, 13];
        rateHeader.Value = (double)report.ExchangeRate;
        rateHeader.Style.Numberformat.Format = "#,##0.00";
        rateHeader.Style.Fill.PatternType = ExcelFillStyle.Solid;
        rateHeader.Style.Fill.BackgroundColor.SetColor(HighlightFill);
        rateHeader.Style.VerticalAlignment = ExcelVerticalAlignment.Bottom;
        ws.Row(1).Height = 30;
        var titleLine = ws.Cells[1, 1, 1, lastCol];
        titleLine.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;

        // ── 1행 섹션: A 최종 결과(A:E) · B 비용(G:H) · C 수출(J:M) ──
        const int sectionTop = 3;
        SectionTitle(ws, sectionTop, 1, "A  최종 결과");
        SectionTitle(ws, sectionTop, 7, "B  비용");
        SectionTitle(ws, sectionTop, 10, "C  수출");

        // C 수출 — 환율 셀을 먼저 정해 두고 원화 수식이 참조한다.
        var exportHead = sectionTop + 1;
        var exportRows = new[] { "매출(USD)", "순이익(USD)", "매출(원)", "순이익(원)", "마진율", "환율" };
        HeaderCells(ws, exportHead, 10, ["구분"], mergeTo: 11);
        var marketCols = new List<int>();
        for (int m = 0; m < report.Exports.Count && 12 + m <= lastCol; m++)
        {
            marketCols.Add(12 + m);
            HeaderCells(ws, exportHead, 12 + m, [report.Exports[m].Market]);
        }
        var exportLastCol = marketCols.Count > 0 ? marketCols[^1] : 12;
        for (int i = 0; i < exportRows.Length; i++)
        {
            var r = exportHead + 1 + i;
            LabelCell(ws, r, 10, exportRows[i], mergeTo: 11);
        }
        var rateRow = exportHead + 6;
        var rateCell = ws.Cells[rateRow, 12, rateRow, exportLastCol];
        rateCell.Merge = true;
        rateCell.Value = (double)report.ExchangeRate;
        rateCell.Style.Numberformat.Format = "#,##0.00";
        rateCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        Manual(rateCell);
        var rateAddress = ExcelCellBase.GetAddress(rateRow, 12, true);
        var exportKrwRevenue = new List<string>();
        var exportKrwProfit = new List<string>();
        for (int m = 0; m < marketCols.Count; m++)
        {
            var c = marketCols[m];
            var e = report.Exports[m];
            ws.Cells[exportHead + 1, c].Value = (double)e.RevenueUsd;
            ws.Cells[exportHead + 2, c].Value = (double)e.ProfitUsd;
            Manual(ws.Cells[exportHead + 1, c, exportHead + 2, c]);
            ws.Cells[exportHead + 1, c, exportHead + 2, c].Style.Numberformat.Format = UsdFormat;
            ws.Cells[exportHead + 3, c].Formula = $"ROUND({ExcelCellBase.GetAddress(exportHead + 1, c)}*{rateAddress},0)";
            ws.Cells[exportHead + 4, c].Formula = $"ROUND({ExcelCellBase.GetAddress(exportHead + 2, c)}*{rateAddress},0)";
            ws.Cells[exportHead + 3, c, exportHead + 4, c].Style.Numberformat.Format = AmountFormat;
            ws.Cells[exportHead + 5, c].Formula = $"IFERROR({ExcelCellBase.GetAddress(exportHead + 4, c)}/{ExcelCellBase.GetAddress(exportHead + 3, c)},\"–\")";
            ws.Cells[exportHead + 5, c].Style.Numberformat.Format = PercentFormat;
            exportKrwRevenue.Add(ExcelCellBase.GetAddress(exportHead + 3, c));
            exportKrwProfit.Add(ExcelCellBase.GetAddress(exportHead + 4, c));
        }
        TableBorders(ws.Cells[exportHead, 10, rateRow, exportLastCol]);
        ws.Cells[exportHead + 5, 12, exportHead + 5, exportLastCol].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        if (!string.IsNullOrWhiteSpace(report.ExchangeRateNote))
        {
            ws.Cells[rateRow + 1, 10].Value = "환율: " + report.ExchangeRateNote;
            ws.Cells[rateRow + 1, 10].Style.Font.Size = 8;
            ws.Cells[rateRow + 1, 10].Style.Font.Color.SetColor(Color.Gray);
        }

        // B 비용.
        var costHead = sectionTop + 1;
        HeaderCells(ws, costHead, 7, ["항목", "금액"]);
        var costSlots = Math.Max(5, report.Costs.Count);
        for (int i = 0; i < costSlots; i++)
        {
            var r = costHead + 1 + i;
            if (i < report.Costs.Count)
            {
                LabelCell(ws, r, 7, report.Costs[i].Item);
                ws.Cells[r, 8].Value = (double)report.Costs[i].Amount;
            }
            else LabelCell(ws, r, 7, "");
            Manual(ws.Cells[r, 8]);
            ws.Cells[r, 8].Style.Numberformat.Format = AmountFormat;
        }
        var costTotalRow = costHead + 1 + costSlots;
        LabelCell(ws, costTotalRow, 7, "소계");
        ws.Cells[costTotalRow, 8].Formula = $"SUM(H{costHead + 1}:H{costTotalRow - 1})";
        ws.Cells[costTotalRow, 8].Style.Numberformat.Format = AmountFormat;
        TotalRow(ws.Cells[costTotalRow, 7, costTotalRow, 8]);
        TableBorders(ws.Cells[costHead, 7, costTotalRow, 8]);

        // D 거래처(아래에서 그리지만 A가 참조하므로 위치를 먼저 정한다).
        var partnerTop = Math.Max(Math.Max(rateRow, costTotalRow), sectionTop + 8) + 2;
        var partnerCells = WritePartnerSection(ws, partnerTop, report, lastCol);

        // A 최종 결과.
        var aHead = sectionTop + 1;
        HeaderCells(ws, aHead, 1, ["구분"], mergeTo: 2);
        HeaderCells(ws, aHead, 3, ["매출액", "순이익", "마진율"]);
        var rocketRevenue = channel.RocketRevenue.Count > 0 ? string.Join("+", channel.RocketRevenue) : "0";
        var rocketNet = channel.RocketNet.Count > 0 ? string.Join("+", channel.RocketNet) : "0";
        var lines = new (string Label, string Revenue, string Profit)[]
        {
            ("온라인(로켓 제외)", $"{channel.TotalRevenue}-({rocketRevenue})", $"{channel.TotalNet}-({rocketNet})"),
            ("로켓+그로스", rocketRevenue, rocketNet),
            ("거래처", partnerCells.TotalRevenue, partnerCells.TotalProfit),
            ("수출", exportKrwRevenue.Count > 0 ? string.Join("+", exportKrwRevenue) : "0", exportKrwProfit.Count > 0 ? string.Join("+", exportKrwProfit) : "0"),
        };
        var r0 = aHead + 1;
        for (int i = 0; i < lines.Length; i++)
        {
            var r = r0 + i;
            LabelCell(ws, r, 1, lines[i].Label, mergeTo: 2);
            ws.Cells[r, 3].Formula = lines[i].Revenue;
            ws.Cells[r, 4].Formula = lines[i].Profit;
            ws.Cells[r, 5].Formula = $"IFERROR(D{r}/C{r},\"–\")";
        }
        var subtotalRow = r0 + lines.Length;
        LabelCell(ws, subtotalRow, 1, "소계", mergeTo: 2);
        ws.Cells[subtotalRow, 3].Formula = $"SUM(C{r0}:C{subtotalRow - 1})";
        ws.Cells[subtotalRow, 4].Formula = $"SUM(D{r0}:D{subtotalRow - 1})";
        ws.Cells[subtotalRow, 5].Formula = $"IFERROR(D{subtotalRow}/C{subtotalRow},\"–\")";
        TotalRow(ws.Cells[subtotalRow, 1, subtotalRow, 5]);
        var costRow = subtotalRow + 1;
        LabelCell(ws, costRow, 1, "비용", mergeTo: 2);
        ws.Cells[costRow, 4].Formula = $"-H{costTotalRow}";
        var finalRow = costRow + 1;
        LabelCell(ws, finalRow, 1, "최종", mergeTo: 2);
        ws.Cells[finalRow, 3].Formula = $"C{subtotalRow}";
        ws.Cells[finalRow, 4].Formula = $"D{subtotalRow}+D{costRow}";
        ws.Cells[finalRow, 5].Formula = $"IFERROR(D{finalRow}/C{finalRow},\"–\")";
        var finalRange = ws.Cells[finalRow, 1, finalRow, 5];
        finalRange.Style.Font.Bold = true;
        finalRange.Style.Font.Size = 11;
        finalRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
        finalRange.Style.Fill.BackgroundColor.SetColor(HighlightFill);
        ws.Cells[r0, 3, finalRow, 4].Style.Numberformat.Format = AmountFormat;
        ws.Cells[r0, 5, finalRow, 5].Style.Numberformat.Format = PercentFormat;
        ws.Cells[r0, 5, finalRow, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        TableBorders(ws.Cells[aHead, 1, finalRow, 5]);
        ws.Row(finalRow).Height = 22;

        // ── 3행 섹션: E 상품그룹 변동(A:F) · F CSKU 변동(H:N) ──
        var changeTop = partnerCells.BottomRow + 2;
        WriteGroupChanges(ws, changeTop, report);
        WriteCskuChanges(ws, changeTop, report);
        var memoBottom = WriteMemoSection(ws, changeTop + 15, report, lastCol);

        var printer = ws.PrinterSettings;
        printer.PaperSize = ePaperSize.A4;
        printer.Orientation = eOrientation.Landscape;
        printer.FitToPage = true;
        printer.FitToWidth = 1;
        printer.FitToHeight = 1;
        printer.HorizontalCentered = true;
        printer.LeftMargin = 0.4;
        printer.RightMargin = 0.4;
        printer.TopMargin = 0.4;
        printer.BottomMargin = 0.45;
        printer.PrintArea = ws.Cells[1, 1, memoBottom, lastCol];
        ws.HeaderFooter.OddFooter.RightAlignedText = "&8MiniERP2 온라인 매출 &P / &N";
    }

    /// <summary>G 비고 — 마감할 때 보고서 화면 [메모] 탭에 적은 내용(단가 인상 등)을 그대로 찍는다. 비어 있어도 칸은 남긴다.</summary>
    private static int WriteMemoSection(ExcelWorksheet ws, int top, OnlineReportBuilder.Result report, int lastCol)
    {
        SectionTitle(ws, top, 1, "G  비고");
        var memo = report.Memo.ReplaceLineEndings(((char)10).ToString()); // 엑셀 셀 줄바꿈은 LF
        var lines = memo.Split((char)10);
        var rows = Math.Clamp(lines.Length, 3, 8);
        var box = ws.Cells[top + 1, 1, top + rows, lastCol];
        box.Merge = true;
        box.Value = memo;
        box.Style.WrapText = true;
        box.Style.Font.Size = 10;
        box.Style.VerticalAlignment = ExcelVerticalAlignment.Top;
        box.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        box.Style.Indent = 1;
        box.Style.Border.BorderAround(ExcelBorderStyle.Medium, Color.Black);
        for (int r = top + 1; r <= top + rows; r++) ws.Row(r).Height = 18;
        return top + rows;
    }

    private record PartnerCells(string TotalRevenue, string TotalProfit, int BottomRow);

    /// <summary>D 거래처 — 한 섹션으로 두고, 거래처가 한 줄(최대 11곳)을 넘으면 같은 섹션 안에서 다음 줄로 이어 쓴다.</summary>
    private static PartnerCells WritePartnerSection(ExcelWorksheet ws, int top, OnlineReportBuilder.Result report, int lastCol)
    {
        SectionTitle(ws, top, 1, "D  거래처");
        const int firstCol = 3;
        var perBand = lastCol - firstCol; // 마지막 열은 첫 줄의 합계 자리
        var partners = report.Partners;
        var bands = Math.Max(1, (int)Math.Ceiling(partners.Count / (double)perBand));
        var revenueCells = new List<string>();
        var profitCells = new List<string>();
        var row = top + 1;
        int sumCol = 0, sumHeadRow = 0;
        for (int b = 0; b < bands; b++)
        {
            var slice = partners.Skip(b * perBand).Take(perBand).ToList();
            var head = row;
            HeaderCells(ws, head, 1, ["구분"], mergeTo: 2);
            for (int i = 0; i < slice.Count; i++)
            {
                var c = firstCol + i;
                HeaderCells(ws, head, c, [slice[i].DisplayName]);
                ws.Cells[head + 1, c].Value = (double)slice[i].Revenue;
                ws.Cells[head + 2, c].Value = (double)slice[i].Profit;
                ws.Cells[head + 3, c].Formula = $"IFERROR({ExcelCellBase.GetAddress(head + 2, c)}/{ExcelCellBase.GetAddress(head + 1, c)},\"–\")";
                if (slice[i].IsManual) Manual(ws.Cells[head + 1, c, head + 2, c]);
                revenueCells.Add(ExcelCellBase.GetAddress(head + 1, c));
                profitCells.Add(ExcelCellBase.GetAddress(head + 2, c));
            }
            LabelCell(ws, head + 1, 1, "매출액", mergeTo: 2);
            LabelCell(ws, head + 2, 1, "예상순이익", mergeTo: 2);
            LabelCell(ws, head + 3, 1, "마진율", mergeTo: 2);
            var bandLast = firstCol + Math.Max(slice.Count, 1) - 1;
            if (b == bands - 1)
            {
                sumCol = bandLast + 1;
                sumHeadRow = head;
                bandLast = sumCol;
            }
            ws.Cells[head + 1, firstCol, head + 2, bandLast].Style.Numberformat.Format = AmountFormat;
            ws.Cells[head + 3, firstCol, head + 3, bandLast].Style.Numberformat.Format = PercentFormat;
            ws.Cells[head + 3, firstCol, head + 3, bandLast].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            TableBorders(ws.Cells[head, 1, head + 3, bandLast]);
            row = head + 4;
        }

        HeaderCells(ws, sumHeadRow, sumCol, ["합계"]);
        ws.Cells[sumHeadRow + 1, sumCol].Formula = revenueCells.Count > 0 ? string.Join("+", revenueCells) : "0";
        ws.Cells[sumHeadRow + 2, sumCol].Formula = profitCells.Count > 0 ? string.Join("+", profitCells) : "0";
        ws.Cells[sumHeadRow + 3, sumCol].Formula = $"IFERROR({ExcelCellBase.GetAddress(sumHeadRow + 2, sumCol)}/{ExcelCellBase.GetAddress(sumHeadRow + 1, sumCol)},\"–\")";
        ws.Cells[sumHeadRow + 1, sumCol, sumHeadRow + 3, sumCol].Style.Font.Bold = true;
        ws.Cells[sumHeadRow + 1, sumCol, sumHeadRow + 3, sumCol].Style.Fill.PatternType = ExcelFillStyle.Solid;
        ws.Cells[sumHeadRow + 1, sumCol, sumHeadRow + 3, sumCol].Style.Fill.BackgroundColor.SetColor(TotalFill);

        return new PartnerCells(ExcelCellBase.GetAddress(sumHeadRow + 1, sumCol), ExcelCellBase.GetAddress(sumHeadRow + 2, sumCol), row - 1);
    }

    private static void WriteGroupChanges(ExcelWorksheet ws, int top, OnlineReportBuilder.Result report)
    {
        var prevLabel = ShortMonth(OnlineReportBuilder.PreviousPeriod(report.Period));
        var curLabel = ShortMonth(report.Period);
        SectionTitle(ws, top, 1, $"E  상품그룹별 매출 변동 (전월 대비)");
        var head = top + 1;
        HeaderCells(ws, head, 1, ["상품그룹"], mergeTo: 2);
        HeaderCells(ws, head, 3, [prevLabel, curLabel, "증감", "증감률"]);
        var row = head + 1;
        row = ChangeBand(ws, row, 1, 6, "▲ 상위 3", up: true, report.GroupTop, 3, report.HasPreviousProfit ? null : "전월 데이터 없음", groupColumn: false);
        row = ChangeBand(ws, row, 1, 6, "▼ 하위 3", up: false, report.GroupBottom, 3, report.HasPreviousProfit ? null : "전월 데이터 없음", groupColumn: false);
        TableBorders(ws.Cells[head, 1, row - 1, 6]);
    }

    private static void WriteCskuChanges(ExcelWorksheet ws, int top, OnlineReportBuilder.Result report)
    {
        var prevLabel = ShortMonth(OnlineReportBuilder.PreviousPeriod(report.Period));
        var curLabel = ShortMonth(report.Period);
        SectionTitle(ws, top, 8, "F  CSKU별 매출 변동 (전월 대비)");
        var head = top + 1;
        HeaderCells(ws, head, 8, ["CSKU"], mergeTo: 9);
        HeaderCells(ws, head, 10, ["상품그룹", prevLabel, curLabel, "증감", "증감률"]);
        string? emptyNote = !report.HasCurrentCsku ? "이 달 CSKU별 통계 없음" : null;
        string? bottomNote = emptyNote ?? (report.HasPreviousCsku ? null : "전월 CSKU별 통계 생성 후 표시");
        var row = head + 1;
        row = ChangeBand(ws, row, 8, 14, "▲ 상위 5", up: true, report.CskuTop, 5, emptyNote, groupColumn: true, previousKnown: report.HasPreviousCsku);
        row = ChangeBand(ws, row, 8, 14, "▼ 하위 5", up: false, report.CskuBottom, 5, bottomNote, groupColumn: true, previousKnown: report.HasPreviousCsku);
        TableBorders(ws.Cells[head, 8, row - 1, 14]);
    }

    /// <summary>변동 표의 한 구간(띠 1행 + 고정 n행). 행 수를 고정해 위아래 구간 위치가 달마다 같게 한다.</summary>
    private static int ChangeBand(ExcelWorksheet ws, int row, int firstCol, int lastCol, string title, bool up,
        IReadOnlyList<OnlineReportBuilder.ChangeRow> items, int slots, string? emptyNote, bool groupColumn, bool previousKnown = true)
    {
        var band = ws.Cells[row, firstCol, row, lastCol];
        band.Merge = true;
        band.Value = title;
        band.Style.Font.Bold = true;
        band.Style.Font.Color.SetColor(up ? UpText : DownText);
        band.Style.Fill.PatternType = ExcelFillStyle.Solid;
        band.Style.Fill.BackgroundColor.SetColor(up ? UpFill : DownFill);
        row++;

        var keyEnd = firstCol + 1;
        var valueStart = groupColumn ? firstCol + 3 : firstCol + 2;
        for (int i = 0; i < slots; i++, row++)
        {
            if (i >= items.Count)
            {
                // 빈 칸도 행 전체를 하나로 묶어 둔다(안내 문구가 열 경계에서 잘리지 않게).
                var empty = ws.Cells[row, firstCol, row, lastCol];
                empty.Merge = true;
                if (i == 0 && emptyNote is not null)
                {
                    empty.Value = emptyNote;
                    empty.Style.Font.Size = 9;
                    empty.Style.Font.Color.SetColor(Color.Gray);
                    empty.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }
                continue;
            }

            var key = ws.Cells[row, firstCol, row, keyEnd];
            key.Merge = true;
            key.Style.Font.Size = 9;

            var item = items[i];
            key.Value = item.Key;
            if (groupColumn)
            {
                ws.Cells[row, firstCol + 2].Value = item.Group;
                ws.Cells[row, firstCol + 2].Style.Font.Size = 9;
            }
            if (previousKnown) ws.Cells[row, valueStart].Value = (double)item.Previous;
            else ws.Cells[row, valueStart].Value = "–";
            ws.Cells[row, valueStart + 1].Value = (double)item.Current;
            if (previousKnown)
            {
                ws.Cells[row, valueStart + 2].Value = (double)item.Delta;
                if (item.Rate is { } rate) ws.Cells[row, valueStart + 3].Value = (double)rate;
                else ws.Cells[row, valueStart + 3].Value = "신규";
                var color = item.Delta >= 0 ? UpText : DownText;
                ws.Cells[row, valueStart + 2, row, valueStart + 3].Style.Font.Color.SetColor(color);
            }
            else
            {
                ws.Cells[row, valueStart + 2].Value = "–";
                ws.Cells[row, valueStart + 3].Value = "–";
            }
            ws.Cells[row, valueStart, row, valueStart + 2].Style.Numberformat.Format = "#,##0;-#,##0;0";
            ws.Cells[row, valueStart + 2].Style.Numberformat.Format = "+#,##0;-#,##0;0";
            ws.Cells[row, valueStart + 3].Style.Numberformat.Format = "+0.0%;-0.0%;0.0%";
            ws.Cells[row, valueStart, row, valueStart + 3].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        }
        return row;
    }

    // ───────────────────────────── 택배비 시트 ─────────────────────────────

    private static void WriteFreightSheet(ExcelWorksheet ws, OnlineReportBuilder.Result report, OnlineReportConfig config)
    {
        var f = config.Freight;
        ws.Cells[1, 1].Value = "실택배비 조정 계산 근거";
        ws.Cells[1, 1].Style.Font.Size = 14;
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[2, 1].Value = $"{OnlineReportBuilder.FormatPeriodLabel(report.Period)} · 출고 1건 = 기준 {f.BaseFee:N0}원 − 실운임 − 부자재비(운임 {f.PackingThreshold:N0}원 이하 {f.PackingSmall:N0}원, 초과 {f.PackingLarge:N0}원) · 풀필먼트 건당 {f.FulfillmentUnitCost:N0}원";
        ws.Cells[2, 1, 2, 6].Merge = true;
        ws.Cells[2, 1].Style.Font.Size = 9;
        ws.Cells[2, 1].Style.WrapText = true;
        ws.Row(2).Height = 30;

        HeaderCells(ws, 4, 1, ["운임", "건수", "운임 합계", $"{f.BaseFee:N0} − 운임", "부자재비", "소계"]);
        var row = 5;
        foreach (var line in report.FreightLines)
        {
            ws.Cells[row, 1].Value = (double)line.Rate;
            ws.Cells[row, 2].Value = line.Count;
            ws.Cells[row, 3].Value = (double)(line.Rate * line.Count);
            ws.Cells[row, 4].Value = (double)line.BaseDiff;
            ws.Cells[row, 5].Value = (double)line.Packing;
            ws.Cells[row, 6].Value = (double)line.Subtotal;
            row++;
        }
        ws.Cells[row, 1].Value = "풀필먼트";
        ws.Cells[row, 2].Value = report.FulfillmentCount;
        ws.Cells[row, 3].Value = (double)(f.FulfillmentUnitCost * report.FulfillmentCount);
        ws.Cells[row, 4].Value = (double)report.FulfillmentSubtotal;
        ws.Cells[row, 6].Value = (double)report.FulfillmentSubtotal;
        row++;
        ws.Cells[row, 1].Value = "합계";
        ws.Cells[row, 2].Formula = $"SUM(B5:B{row - 1})";
        ws.Cells[row, 3].Formula = $"SUM(C5:C{row - 1})";
        ws.Cells[row, 4].Formula = $"SUM(D5:D{row - 1})";
        ws.Cells[row, 5].Formula = $"SUM(E5:E{row - 1})";
        ws.Cells[row, 6].Formula = $"SUM(F5:F{row - 1})";
        TotalRow(ws.Cells[row, 1, row, 6]);
        ws.Cells[5, 1, row, 6].Style.Numberformat.Format = AmountFormat;
        TableBorders(ws.Cells[4, 1, row, 6]);
        row += 2;
        ws.Cells[row, 1].Value = "보고서 반영액";
        ws.Cells[row, 3].Value = (double)report.FreightAdjustment;
        ws.Cells[row, 3].Style.Numberformat.Format = AmountFormat;
        ws.Cells[row, 1, row, 3].Style.Font.Bold = true;
        if (report.FreightOverridden)
        {
            ws.Cells[row, 4].Value = $"수동 입력값(계산값 {report.FreightCalculated:N0})";
            ws.Cells[row, 4].Style.Font.Color.SetColor(DownText);
        }
        for (int c = 1; c <= 6; c++) ws.Column(c).Width = 14;
        ws.PrinterSettings.PaperSize = ePaperSize.A4;
        ws.PrinterSettings.Orientation = eOrientation.Portrait;
        ws.PrinterSettings.FitToPage = true;
        ws.PrinterSettings.FitToWidth = 1;
        ws.PrinterSettings.FitToHeight = 0;
        ws.PrinterSettings.PrintArea = ws.Cells[1, 1, row, 6];
    }

    // ───────────────────────────── 서식 도우미 ─────────────────────────────

    private static string ShortMonth(string period) =>
        period.Length == 7 ? $"{int.Parse(period[5..])}월" : period;

    private static void SectionTitle(ExcelWorksheet ws, int row, int col, string text)
    {
        var cell = ws.Cells[row, col];
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Font.Size = 11;
        cell.Style.Font.Color.SetColor(Color.FromArgb(47, 93, 138));
    }

    private static void HeaderCells(ExcelWorksheet ws, int row, int col, IReadOnlyList<string> texts, int? mergeTo = null)
    {
        if (mergeTo is { } end && texts.Count == 1)
        {
            var range = ws.Cells[row, col, row, end];
            range.Merge = true;
            range.Value = texts[0];
            StyleHeader(range);
            return;
        }
        for (int i = 0; i < texts.Count; i++)
        {
            var cell = ws.Cells[row, col + i];
            cell.Value = texts[i];
            StyleHeader(cell);
        }
    }

    private static void StyleHeader(ExcelRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.Size = 10;
        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        range.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        range.Style.WrapText = true;
        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
        range.Style.Fill.BackgroundColor.SetColor(HeaderFill);
        range.Style.Border.BorderAround(ExcelBorderStyle.Thin, GridLine);
    }

    private static void LabelCell(ExcelWorksheet ws, int row, int col, string text, int? mergeTo = null)
    {
        var range = mergeTo is { } end ? ws.Cells[row, col, row, end] : ws.Cells[row, col];
        if (mergeTo is not null) range.Merge = true;
        range.Value = text;
        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        range.Style.Indent = 1;
    }

    private static void Manual(ExcelRange range)
    {
        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
        range.Style.Fill.BackgroundColor.SetColor(ManualFill);
    }

    private static void TotalRow(ExcelRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
        range.Style.Fill.BackgroundColor.SetColor(TotalFill);
    }

    private static void TableBorders(ExcelRange range)
    {
        var b = range.Style.Border;
        b.Top.Style = b.Bottom.Style = b.Left.Style = b.Right.Style = ExcelBorderStyle.Thin;
        b.Top.Color.SetColor(GridLine);
        b.Bottom.Color.SetColor(GridLine);
        b.Left.Color.SetColor(GridLine);
        b.Right.Color.SetColor(GridLine);
        b.BorderAround(ExcelBorderStyle.Medium, Color.Black); // 섹션 경계는 굵은 선
        for (int r = range.Start.Row; r <= range.End.Row; r++)
            if (range.Worksheet.Row(r).Height < 18) range.Worksheet.Row(r).Height = 18;
    }
}

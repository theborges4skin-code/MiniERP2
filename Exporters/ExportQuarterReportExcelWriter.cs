using System.Drawing;
using MiniERP2.Services;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using static MiniERP2.Services.ExportQuarterReportBuilder;

namespace MiniERP2.Exporters;

/// <summary>
/// 분기 수출요약보고서 엑셀(26년2분기 보고서와 같은 시트 구성).
///   전체 요약(마켓×통화 / 월×지표 표) · 전체상세(tidy) · 수출신고실적목록조회 · 페이오니어 내역 상세 · 자사몰판매
///   + 아마존 매출(계산 근거) · 수출신고 대사 시트 · 확인사항
/// 2분기 피벗은 서로 다른 지표·통화를 한 칸에 더한 합계가 섞여 있어, 요약 표는 지표·통화별 합계만 둔다.
/// </summary>
public static class ExportQuarterReportExcelWriter
{
    private const string Num = "#,##0.00;-#,##0.00;";
    private static readonly Color HeaderFill = Color.FromArgb(221, 235, 247);
    private static readonly Color MarketFill = Color.FromArgb(242, 242, 242);
    private static readonly string[] Indicators = [Sales, Declared, Settled];

    public static void Write(ExcelPackage package, Report report, IReadOnlyDictionary<string, string> marketNames)
    {
        var quarter = $"{report.PeriodStart:yy}q{(report.PeriodStart.Month - 1) / 3 + 1}";
        WriteSummary(package.Workbook.Worksheets.Add($"전체 요약 {quarter}"), report);
        WriteTidy(package.Workbook.Worksheets.Add($"전체상세 {quarter}"), report);
        WriteDeclarations(package.Workbook.Worksheets.Add("수출신고실적목록조회"), report);
        WritePayoneer(package.Workbook.Worksheets.Add("페이오니어 내역 상세"), report, marketNames);
        WritePaypal(package.Workbook.Worksheets.Add("자사몰판매"), report);
        if (report.WixTables.Count > 0) WriteWix(package.Workbook.Worksheets.Add("자사몰주문(Wix)"), report);
        WriteAmazon(package.Workbook.Worksheets.Add("아마존 매출"), report, marketNames);
        ExportDeclarationReconcileExcelWriter.Write(package, report.Reconcile, marketNames, "신고대사_");
        WriteChecks(package.Workbook.Worksheets.Add("확인사항"), report);
        package.Workbook.Worksheets[$"전체 요약 {quarter}"].Select();
    }

    private static void WriteSummary(ExcelWorksheet ws, Report report)
    {
        var months = Enumerable.Range(0, 12).Select(i => report.PeriodStart.AddMonths(i))
            .TakeWhile(m => m < report.PeriodEndExclusive).Select(m => m.ToString("yyyy-MM")).ToList();

        ws.Cells[1, 1].Value = $"수출요약보고서 {report.PeriodStart:yyyy-MM} ~ {report.PeriodEndExclusive.AddDays(-1):yyyy-MM}";
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 1].Style.Font.Size = 13;
        ws.Cells[2, 1].Value = "매출액: 쇼피=구매확정일 기준 소비자 실결제액, 아마존=거래내역 Income 합계, 자사몰=PayPal 결제액 / 신고액: 관세청 수출신고 결제금액 / 정산액: 페이오니어·PayPal 입금액. 금액은 각 통화 단위.";

        const int h1 = 4, h2 = 5;
        ws.Cells[h1, 1].Value = "마켓";
        ws.Cells[h1, 2].Value = "통화";
        ws.Cells[h1, 1, h2, 1].Merge = true;
        ws.Cells[h1, 2, h2, 2].Merge = true;
        var col = 3;
        foreach (var label in months.Append("분기 합계"))
        {
            ws.Cells[h1, col].Value = label;
            ws.Cells[h1, col, h1, col + Indicators.Length - 1].Merge = true;
            for (var i = 0; i < Indicators.Length; i++) ws.Cells[h2, col + i].Value = Indicators[i];
            col += Indicators.Length;
        }
        var lastCol = col - 1;
        var header = ws.Cells[h1, 1, h2, lastCol];
        header.Style.Font.Bold = true;
        header.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        header.Style.Fill.PatternType = ExcelFillStyle.Solid;
        header.Style.Fill.BackgroundColor.SetColor(HeaderFill);

        var row = h2 + 1;
        foreach (var market in report.Tidy.GroupBy(t => t.MarketName).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var first = true;
            foreach (var currency in market.Select(t => t.Currency).Distinct().OrderBy(c => c == "USD" ? 1 : 0).ThenBy(c => c, StringComparer.Ordinal))
            {
                ws.Cells[row, 1].Value = first ? market.Key : "";
                ws.Cells[row, 2].Value = currency;
                col = 3;
                foreach (var ym in months.Append(null))
                {
                    foreach (var ind in Indicators)
                    {
                        var amount = market.Where(t => t.Currency == currency && t.Indicator == ind && (ym is null || t.YearMonth == ym)).Sum(t => t.Amount);
                        if (amount != 0)
                        {
                            ws.Cells[row, col].Value = (double)amount;
                            ws.Cells[row, col].Style.Numberformat.Format = Num;
                        }
                        col++;
                    }
                }
                if (first)
                {
                    ws.Cells[row, 1].Style.Font.Bold = true;
                    ws.Cells[row, 1, row, lastCol].Style.Border.Top.Style = ExcelBorderStyle.Thin;
                }
                first = false;
                row++;
            }
        }
        // 분기 합계 열 강조
        ws.Cells[h2 + 1, lastCol - Indicators.Length + 1, Math.Max(row - 1, h2 + 1), lastCol].Style.Fill.PatternType = ExcelFillStyle.Solid;
        ws.Cells[h2 + 1, lastCol - Indicators.Length + 1, Math.Max(row - 1, h2 + 1), lastCol].Style.Fill.BackgroundColor.SetColor(MarketFill);

        row += 1;
        ws.Cells[row++, 1].Value = "비고";
        ws.Cells[row - 1, 1].Style.Font.Bold = true;
        ws.Cells[row++, 1].Value = "자사몰 정산 -> 페이팔, 그외 전부 페이오니어. 쇼피 정산은 USD(페이오니어)로 들어와 판매·신고 통화와 다름.";
        ws.Cells[row++, 1].Value = "신고액 USD = 아마존 미국 FBA 입고분 수출신고(인천공항세관). 쇼피 신고액과 판매액의 차이는 '신고대사_요약' 시트 참고.";
        if (report.Warnings.Count > 0)
        {
            ws.Cells[row, 1].Value = $"확인사항 {report.Warnings.Count}건 — '확인사항' 시트 참고";
            ws.Cells[row, 1].Style.Font.Color.SetColor(Color.DarkOrange);
            ws.Cells[row, 1].Style.Font.Bold = true;
        }

        ws.Column(1).Width = 16;
        ws.Column(2).Width = 7;
        for (var c = 3; c <= lastCol; c++) ws.Column(c).Width = 13;
        ws.View.FreezePanes(h2 + 1, 3);
    }

    private static void WriteTidy(ExcelWorksheet ws, Report report)
    {
        string[] headers = ["마켓", "연월", "지표", "통화", "금액", "비고"];
        WriteHeader(ws, headers);
        var row = 2;
        foreach (var t in report.Tidy)
        {
            ws.Cells[row, 1].Value = t.MarketName;
            ws.Cells[row, 2].Value = t.YearMonth;
            ws.Cells[row, 3].Value = t.Indicator;
            ws.Cells[row, 4].Value = t.Currency;
            ws.Cells[row, 5].Value = (double)t.Amount;
            ws.Cells[row, 5].Style.Numberformat.Format = Num;
            ws.Cells[row, 6].Value = t.Note;
            row++;
        }
        Finish(ws, headers.Length, row);
    }

    private static void WriteDeclarations(ExcelWorksheet ws, Report report)
    {
        var headers = report.DeclarationTables.FirstOrDefault()?.Headers ?? new List<string>();
        WriteHeader(ws, headers.Append("파일").ToArray());
        var row = 2;
        foreach (var table in report.DeclarationTables)
        {
            foreach (var r in table.Rows)
            {
                foreach (var h in headers.Select((name, i) => (name, i)))
                    SetCell(ws.Cells[row, h.i + 1], table.Get(r, table.Col(h.name)), h.name == "결제금액");
                ws.Cells[row, headers.Count + 1].Value = table.FileName;
                row++;
            }
        }
        Finish(ws, headers.Count + 1, row);
    }

    private static void WritePayoneer(ExcelWorksheet ws, Report report, IReadOnlyDictionary<string, string> marketNames)
    {
        var headers = report.PayoneerTables.FirstOrDefault().Table?.Headers ?? new List<string>();
        WriteHeader(ws, new[] { "연월", "분류" }.Concat(headers).Append("파일").ToArray());
        var row = 2;
        foreach (var (table, rows) in report.PayoneerTables)
        {
            foreach (var r in rows)
            {
                ws.Cells[row, 1].Value = r.Date?.ToString("yyyy-MM");
                ws.Cells[row, 2].Value = r.Excluded ? "제외(이체)" : r.MarketCode is null ? "미분류" : marketNames.GetValueOrDefault(r.MarketCode, r.MarketCode);
                if (r.MarketCode is null && !r.Excluded) ws.Cells[row, 2].Style.Font.Color.SetColor(Color.Red);
                for (var i = 0; i < headers.Count; i++)
                    SetCell(ws.Cells[row, i + 3], table.Get(r.Raw, table.Col(headers[i])), headers[i] == "Amount");
                ws.Cells[row, headers.Count + 3].Value = table.FileName;
                row++;
            }
        }
        Finish(ws, headers.Count + 3, row);
    }

    private static void WritePaypal(ExcelWorksheet ws, Report report)
    {
        var headers = report.PaypalTables.FirstOrDefault().Table?.Headers ?? new List<string>();
        WriteHeader(ws, new[] { "연월" }.Concat(headers).Append("파일").ToArray());
        var row = 2;
        foreach (var (table, rows) in report.PaypalTables)
        {
            foreach (var r in rows)
            {
                ws.Cells[row, 1].Value = r.Date?.ToString("yyyy-MM");
                for (var i = 0; i < headers.Count; i++)
                    SetCell(ws.Cells[row, i + 2], table.Get(r.Raw, table.Col(headers[i])), headers[i] is "Gross" or "Fee" or "Net");
                ws.Cells[row, headers.Count + 2].Value = table.FileName;
                row++;
            }
        }
        Finish(ws, headers.Count + 2, row);
    }

    private static void WriteWix(ExcelWorksheet ws, Report report)
    {
        var headers = report.WixTables[0].Table.Headers;
        WriteHeader(ws, new[] { "연월" }.Concat(headers).Append("파일").ToArray());
        var row = 2;
        foreach (var (table, orders) in report.WixTables)
        {
            foreach (var o in orders)
            {
                ws.Cells[row, 1].Value = o.Date?.ToString("yyyy-MM");
                for (var i = 0; i < headers.Count; i++)
                    SetCell(ws.Cells[row, i + 2], table.Get(o.Raw, table.Col(headers[i])), headers[i] is "Total" or "Price" or "Net amount");
                ws.Cells[row, headers.Count + 2].Value = table.FileName;
                row++;
            }
        }
        Finish(ws, headers.Count + 2, row);
    }

    private static void WriteAmazon(ExcelWorksheet ws, Report report, IReadOnlyDictionary<string, string> marketNames)
    {
        var components = report.AmazonIncome.SelectMany(a => a.Components.Keys).Distinct().ToList();
        var headers = new[] { "마켓", "연월", "통화" }.Concat(components).Concat(new[] { "재고 보상(양수 조정)", "매출액(Income 합계)", "파일" }).ToArray();
        WriteHeader(ws, headers);
        var row = 2;
        foreach (var a in report.AmazonIncome.OrderBy(a => a.MarketCode).ThenBy(a => a.YearMonth))
        {
            ws.Cells[row, 1].Value = marketNames.GetValueOrDefault(a.MarketCode, a.MarketCode);
            ws.Cells[row, 2].Value = a.YearMonth;
            ws.Cells[row, 3].Value = a.Currency;
            var c = 4;
            foreach (var comp in components) SetNum(ws.Cells[row, c++], a.Components.GetValueOrDefault(comp));
            SetNum(ws.Cells[row, c++], a.Reimbursement);
            SetNum(ws.Cells[row, c], a.Income);
            ws.Cells[row, c++].Style.Font.Bold = true;
            ws.Cells[row, c].Value = a.FileName;
            row++;
        }
        Finish(ws, headers.Length, row);
        ws.Cells[row + 1, 1].Value = "매출액 = 아마존 요약 PDF의 Income 합계(Net sales, credits, and refunds)와 같은 계산: 주문·환불·청산 행의 상품판매+배송비+선물포장+프로모션 할인 + 양수 재고 보상(FBA Inventory Reimbursement).";
    }

    private static void WriteChecks(ExcelWorksheet ws, Report report)
    {
        ws.Cells[1, 1].Value = "확인사항";
        ws.Cells[1, 1].Style.Font.Bold = true;
        var row = 2;
        if (report.Warnings.Count == 0) ws.Cells[row++, 1].Value = "없음";
        foreach (var w in report.Warnings) ws.Cells[row++, 1].Value = "• " + w;
        row++;
        ws.Cells[row++, 1].Value = "사용한 파일";
        ws.Cells[row - 1, 1].Style.Font.Bold = true;
        foreach (var f in report.UsedFiles) ws.Cells[row++, 1].Value = f;
        ws.Column(1).Width = 140;
    }

    /// <summary>원본 문자열을 그대로 넣되, 금액 열은 숫자로 바꿔 합계를 낼 수 있게 한다.</summary>
    private static void SetCell(ExcelRange cell, string text, bool numeric)
    {
        if (numeric && decimal.TryParse(text.Replace(",", ""), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            cell.Value = (double)d;
            cell.Style.Numberformat.Format = Num;
        }
        else cell.Value = text;
    }

    private static void SetNum(ExcelRange cell, decimal value)
    {
        cell.Value = (double)value;
        cell.Style.Numberformat.Format = Num;
    }

    private static void WriteHeader(ExcelWorksheet ws, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cells[1, i + 1];
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(HeaderFill);
        }
        ws.View.FreezePanes(2, 1);
    }

    private static void Finish(ExcelWorksheet ws, int columnCount, int nextRow)
    {
        if (columnCount == 0) return;
        if (nextRow > 2) ws.Cells[1, 1, nextRow - 1, columnCount].AutoFilter = true;
        ws.Cells[1, 1, Math.Max(nextRow - 1, 1), columnCount].AutoFitColumns(8, 45);
    }
}

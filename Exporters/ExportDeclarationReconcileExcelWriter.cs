using MiniERP2.Services;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using static MiniERP2.Services.ExportDeclarationReconcileEngine;

namespace MiniERP2.Exporters;

/// <summary>
/// 수출신고 대사 결과를 엑셀로 쓴다. 분기 부가세 신고 때 세무사에게 "판매 매출과 수출신고액이 왜 다른지"를
/// 설명하는 자료로 쓰는 것이 목적이라, 요약 시트에 항목별 브리지와 각 항목 설명을 함께 둔다.
/// </summary>
public static class ExportDeclarationReconcileExcelWriter
{
    private const string Num = "#,##0.00;-#,##0.00;-";

    /// <param name="sheetPrefix">다른 보고서에 대사 시트를 덧붙일 때 시트 이름 앞에 붙일 문자열(예: "대사_").</param>
    public static void Write(ExcelPackage package, Result result, IReadOnlyDictionary<string, string> marketNames, string sheetPrefix = "")
    {
        WriteSummary(package.Workbook.Worksheets.Add(sheetPrefix + "요약"), result, marketNames);
        WriteMatches(package.Workbook.Worksheets.Add(sheetPrefix + "대사상세"), result, marketNames);
        WriteUnmatchedDeclarations(package.Workbook.Worksheets.Add(sheetPrefix + "짝없는신고"), result);
        WriteUnmatchedOrders(package.Workbook.Worksheets.Add(sheetPrefix + "짝없는주문"), result, marketNames);
        WriteRates(package.Workbook.Worksheets.Add(sheetPrefix + "신고환산율"), result);
    }

    private static void WriteSummary(ExcelWorksheet ws, Result result, IReadOnlyDictionary<string, string> marketNames)
    {
        ws.Cells[1, 1].Value = "수출신고 대사 요약 (판매 매출 → 관세청 수출신고액)";
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 1].Style.Font.Size = 13;
        ws.Cells[2, 1].Value = $"기간: {result.PeriodStart:yyyy-MM-dd} ~ {result.PeriodEndExclusive.AddDays(-1):yyyy-MM-dd}  (판매=구매확정일 기준, 신고=신고일자 기준, 금액은 각 통화 원화폐 단위)";

        string[] headers =
        [
            "통화", "마켓", "판매 매출\n(실결제, 완료일 기준)", "① 완료는 기간 내,\n기간 내 신고 없음(−)", "① 기간 내 신고,\n완료는 기간 밖(+)",
            "② 쇼피 부담 할인\n되돌림(+)", "③ 신고환산율\n차이", "④ 건별 차이", "⑤ 짝 없는 신고(+)",
            "= 수출신고액", "검산(0이어야 함)", "짝지은 신고 건수", "짝없는 신고 건수", "짝없는 주문 건수", "주문파일 최초 출고일",
        ];
        const int headerRow = 4;
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cells[headerRow, i + 1];
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.WrapText = true;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(221, 235, 247));
        }
        ws.Row(headerRow).Height = 45;

        var row = headerRow + 1;
        foreach (var s in result.Summaries)
        {
            ws.Cells[row, 1].Value = s.Currency;
            ws.Cells[row, 2].Value = string.Join(", ", s.Markets.Split(", ", StringSplitOptions.RemoveEmptyEntries)
                .Select(m => marketNames.TryGetValue(m, out var n) ? n : m));
            SetNum(ws.Cells[row, 3], s.SalesCompleted);
            SetNum(ws.Cells[row, 4], -s.CompletedButNoDeclaration);
            SetNum(ws.Cells[row, 5], s.DeclaredButCompletedOutside);
            SetNum(ws.Cells[row, 6], s.ShopeeFundedDiscount);
            SetNum(ws.Cells[row, 7], s.RateEffect);
            SetNum(ws.Cells[row, 8], s.Residual);
            SetNum(ws.Cells[row, 9], s.UnmatchedDeclarations);
            SetNum(ws.Cells[row, 10], s.Declared);
            ws.Cells[row, 10].Style.Font.Bold = true;
            SetNum(ws.Cells[row, 11], s.BridgeCheck);
            ws.Cells[row, 12].Value = s.MatchedCount;
            ws.Cells[row, 13].Value = s.UnmatchedDeclarationCount;
            ws.Cells[row, 14].Value = s.UnmatchedOrderCount;
            ws.Cells[row, 15].Value = s.FirstShippedOrder?.ToString("yyyy-MM-dd") ?? "";
            row++;
        }

        row += 2;
        string[] notes =
        [
            "항목 설명",
            "판매 매출: 쇼피 구매확정(completed) 주문의 소비자 실결제 상품금액(모든 할인 반영, 배송비 제외), 구매확정일이 기간 안인 것. 수출요약보고서 B트랙 매출액과 같은 값.",
            "수출신고액: 관세청 수출신고실적의 결제금액 합계(신고일자가 기간 안인 것). 신고일자 = 쇼피 출고처리일(한국시간).",
            "① 시점 차이: 매출은 구매확정일, 신고는 출고일 기준이라 생기는 차이. 예) 9월 출고·10월 구매확정 주문은 3분기 신고에는 있고 3분기 매출에는 없음.",
            "② 쇼피 부담 할인: 쇼피가 부담한 쿠폰·코인 등. 소비자 실결제액에서는 빠지지만 신고 기준가에는 포함된다.",
            "③ 신고환산율 차이: 수출신고액은 (할인가×수량 − 판매자 부담 쿠폰) × 마켓별 고정비율(k)로 잡힌다. 그 비율만큼의 차이. k는 '신고환산율' 시트 참고.",
            "④ 건별 차이: 짝지은 건의 실제 신고액 − 예상 신고액(k×기준가). 환율 변동·가격 변경 등으로 생기는 작은 차이.",
            "⑤ 짝 없는 신고: 대응 주문을 찾지 못한 신고. 대부분 주문파일 기간 이전(예: 6월 주문, 7월 출고) 주문이며 '짝없는신고' 시트에서 사유 확인.",
            "짝 없는 주문: '짝없는주문' 시트. 기간 이후 출고(다음 분기 신고분)이거나 신고 누락·매칭 실패 의심 건.",
            "※ 짝짓기는 통화·출고일(신고일 1일 전~4일 후)·예상신고액(±15%, 남은 건은 ±35%)으로 하며, 주문번호가 신고서에 없어 추정 매칭이다. '대사상세' 매칭방식이 '금액 일치'가 아닌 건은 금액 확인 권장.",
        ];
        foreach (var note in notes)
        {
            ws.Cells[row, 1].Value = note;
            if (note == notes[0]) ws.Cells[row, 1].Style.Font.Bold = true;
            row++;
        }

        ws.Column(1).Width = 8;
        ws.Column(2).Width = 22;
        for (var c = 3; c <= 11; c++) ws.Column(c).Width = 17;
        for (var c = 12; c <= 15; c++) ws.Column(c).Width = 12;
        ws.View.FreezePanes(headerRow + 1, 3);
    }

    private static void WriteMatches(ExcelWorksheet ws, Result result, IReadOnlyDictionary<string, string> marketNames)
    {
        string[] headers =
        [
            "신고번호", "신고일자", "통화", "신고금액", "주문번호", "마켓", "출고일(한국)", "구매확정일", "상품(SKU×수량)",
            "소비자 실결제액", "신고 기준가\n(할인가−판매자쿠폰)", "k", "예상 신고액", "차이\n(신고−실결제)",
            "② 쇼피부담 할인", "③ 환산율 차이", "④ 건별 차이", "매칭방식", "기간 밖 구매확정",
        ];
        WriteHeader(ws, headers);

        var inPeriod = (DateTime? d) => d is { } v && v >= result.PeriodStart && v < result.PeriodEndExclusive;
        var row = 2;
        foreach (var m in result.Matches.OrderBy(m => m.Declaration.Currency).ThenBy(m => m.Declaration.Date).ThenBy(m => m.Declaration.DeclarationNo))
        {
            ws.Cells[row, 1].Value = m.Declaration.DeclarationNo;
            ws.Cells[row, 2].Value = m.Declaration.Date.ToString("yyyy-MM-dd");
            ws.Cells[row, 3].Value = m.Declaration.Currency;
            SetNum(ws.Cells[row, 4], m.Declaration.Amount);
            ws.Cells[row, 5].Value = m.Order.OrderId;
            ws.Cells[row, 6].Value = marketNames.TryGetValue(m.Order.MarketCode, out var n) ? n : m.Order.MarketCode;
            ws.Cells[row, 7].Value = m.Order.ShippedAt?.ToString("yyyy-MM-dd HH:mm");
            ws.Cells[row, 8].Value = m.Order.CompletedAt?.ToString("yyyy-MM-dd HH:mm");
            ws.Cells[row, 9].Value = m.Order.Skus;
            SetNum(ws.Cells[row, 10], m.Order.PaidAmount);
            SetNum(ws.Cells[row, 11], m.Order.SellerBaseAmount);
            ws.Cells[row, 12].Value = (double)m.Rate;
            ws.Cells[row, 12].Style.Numberformat.Format = "0.000";
            SetNum(ws.Cells[row, 13], m.ExpectedDeclared);
            SetNum(ws.Cells[row, 14], m.TotalDiff);
            SetNum(ws.Cells[row, 15], m.ShopeeFundedDiscount);
            SetNum(ws.Cells[row, 16], m.RateEffect);
            SetNum(ws.Cells[row, 17], m.Residual);
            ws.Cells[row, 18].Value = m.MatchType;
            if (m.MatchType != "금액 일치") ws.Cells[row, 18].Style.Font.Color.SetColor(System.Drawing.Color.DarkOrange);
            ws.Cells[row, 19].Value = inPeriod(m.Order.CompletedAt) ? "" : "예";
            row++;
        }
        Finish(ws, headers.Length, row);
        ws.Column(9).Width = 40;
    }

    private static void WriteUnmatchedDeclarations(ExcelWorksheet ws, Result result)
    {
        string[] headers = ["신고번호", "신고일자", "통화", "신고금액", "추정 사유", "파일"];
        WriteHeader(ws, headers);
        var row = 2;
        foreach (var u in result.UnmatchedDeclarations.OrderBy(u => u.Declaration.Currency).ThenBy(u => u.Declaration.Date))
        {
            ws.Cells[row, 1].Value = u.Declaration.DeclarationNo;
            ws.Cells[row, 2].Value = u.Declaration.Date.ToString("yyyy-MM-dd");
            ws.Cells[row, 3].Value = u.Declaration.Currency;
            SetNum(ws.Cells[row, 4], u.Declaration.Amount);
            ws.Cells[row, 5].Value = u.Reason;
            ws.Cells[row, 6].Value = u.Declaration.FileName;
            row++;
        }
        Finish(ws, headers.Length, row);
        ws.Column(5).Width = 60;
    }

    private static void WriteUnmatchedOrders(ExcelWorksheet ws, Result result, IReadOnlyDictionary<string, string> marketNames)
    {
        string[] headers = ["주문번호", "마켓", "통화", "출고일(한국)", "구매확정일", "소비자 실결제액", "신고 기준가", "상품(SKU×수량)", "추정 사유", "파일"];
        WriteHeader(ws, headers);
        var row = 2;
        foreach (var u in result.UnmatchedOrders.OrderBy(u => u.Order.Currency).ThenBy(u => u.Order.ShippedAt))
        {
            ws.Cells[row, 1].Value = u.Order.OrderId;
            ws.Cells[row, 2].Value = marketNames.TryGetValue(u.Order.MarketCode, out var n) ? n : u.Order.MarketCode;
            ws.Cells[row, 3].Value = u.Order.Currency;
            ws.Cells[row, 4].Value = u.Order.ShippedAt?.ToString("yyyy-MM-dd HH:mm");
            ws.Cells[row, 5].Value = u.Order.CompletedAt?.ToString("yyyy-MM-dd HH:mm");
            SetNum(ws.Cells[row, 6], u.Order.PaidAmount);
            SetNum(ws.Cells[row, 7], u.Order.SellerBaseAmount);
            ws.Cells[row, 8].Value = u.Order.Skus;
            ws.Cells[row, 9].Value = u.Reason;
            ws.Cells[row, 10].Value = u.Order.FileName;
            row++;
        }
        Finish(ws, headers.Length, row);
        ws.Column(8).Width = 40;
        ws.Column(9).Width = 50;
    }

    private static void WriteRates(ExcelWorksheet ws, Result result)
    {
        string[] headers = ["통화", "k (신고액 ÷ 신고 기준가)", "표본 수(그날 신고 1건·주문 1건)", "출처"];
        WriteHeader(ws, headers);
        var row = 2;
        foreach (var r in result.Rates)
        {
            ws.Cells[row, 1].Value = r.Currency;
            ws.Cells[row, 2].Value = (double)r.Rate;
            ws.Cells[row, 2].Style.Numberformat.Format = "0.0000";
            ws.Cells[row, 3].Value = r.SampleCount;
            ws.Cells[row, 4].Value = r.Source;
            row++;
        }
        Finish(ws, headers.Length, row);
    }

    private static void WriteHeader(ExcelWorksheet ws, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cells[1, i + 1];
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.WrapText = true;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(221, 235, 247));
        }
        ws.Row(1).Height = 32;
        ws.View.FreezePanes(2, 1);
    }

    private static void Finish(ExcelWorksheet ws, int columnCount, int nextRow)
    {
        if (nextRow > 2) ws.Cells[1, 1, nextRow - 1, columnCount].AutoFilter = true;
        ws.Cells[1, 1, Math.Max(nextRow - 1, 1), columnCount].AutoFitColumns(9, 24);
    }

    private static void SetNum(ExcelRange cell, decimal value)
    {
        cell.Value = (double)value;
        cell.Style.Numberformat.Format = Num;
    }
}

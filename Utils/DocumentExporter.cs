using MiniERP2.Models;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace MiniERP2.Utils;

/// <summary>
/// 거래명세표·견적서 등 사업 문서를 XLSX 파일로 생성합니다.
/// 템플릿 없이 EPPlus로 직접 셀을 구성하며, A4 1페이지 출력 설정을 적용합니다.
/// </summary>
public static class DocumentExporter
{
    // 거래명세표 데이터 열 인덱스 (1-based)
    private const int ColYear = 1;
    private const int ColMonth = 2;
    private const int ColDay = 3;
    private const int ColItem = 4;
    private const int ColSpec = 5;
    private const int ColQty = 6;
    private const int ColUnitPrice = 7;
    private const int ColSupply = 8;
    private const int ColTax = 9;   // VAT별도: 세액 / VAT포함: 금액(VAT포함)
    private const int ColNote = 10;
    private const int TotalCols = 10;

    public static void ExportTradeStatement(TradeStatementDoc doc, string filePath)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("거래명세표");

        // VAT포함은 '공급가액'·'세액' 분리 없이 9열, VAT별도는 기존 10열
        int totalCols = doc.IsVatExcluded ? 10 : 9;
        int colNote   = totalCols; // 비고는 항상 마지막 열

        SetColumnWidths(ws, doc.IsVatExcluded);
        SetPrintSettings(ws);

        int row = 1;
        row = WriteTitle(ws, row, doc.IsVatExcluded, totalCols);
        row = WritePartyBlock(ws, row, doc.Supplier, doc.Buyer, totalCols);
        row = WriteDataHeader(ws, row, doc.IsVatExcluded, totalCols);
        int dataStart = row;
        row = WriteDataRows(ws, row, doc, totalCols, colNote);
        row = WriteTotalRow(ws, row, doc, dataStart, totalCols);
        row = WriteSummaryRow(ws, row, doc, totalCols);
        if (!string.IsNullOrWhiteSpace(doc.TaxInvoiceEmail))
            row = WriteTaxInvoiceLine(ws, row, doc.TaxInvoiceEmail, totalCols);
        if (!string.IsNullOrWhiteSpace(doc.FooterNote))
            row = WriteFooterNote(ws, row, doc.FooterNote, totalCols);
        WriteIssueDate(ws, row, doc.IssueDate, totalCols);

        if (doc.StampImagePath != null && File.Exists(doc.StampImagePath))
            InsertStamp(ws, doc.StampImagePath);

        ExportHelper.SaveExcel(package, filePath);
    }

    // ── 열 너비 ────────────────────────────────────────────────────────────
    private static void SetColumnWidths(ExcelWorksheet ws, bool vatExcl)
    {
        ws.Column(ColYear).Width = 5;
        ws.Column(ColMonth).Width = 4;
        ws.Column(ColDay).Width = 4;
        ws.Column(ColItem).Width = vatExcl ? 22 : 26;  // VAT포함은 열이 1개 줄어 품목 열을 더 넓게
        ws.Column(ColSpec).Width = 11;
        ws.Column(ColQty).Width = 7;
        ws.Column(ColUnitPrice).Width = vatExcl ? 11 : 13; // 단가(VAT포함) 라벨이 길어 약간 넓게
        ws.Column(ColSupply).Width = vatExcl ? 11 : 15;    // 합계금액(VAT포함) 라벨 대응
        if (vatExcl) ws.Column(ColTax).Width = 11;          // VAT별도에만 세액 열 존재
        ws.Column(vatExcl ? ColNote : ColSupply + 1).Width = 13; // 비고: VAT별도=col10, VAT포함=col9
    }

    // ── 인쇄 설정 (A4 세로, 1페이지에 맞춤) ─────────────────────────────
    private static void SetPrintSettings(ExcelWorksheet ws)
    {
        ws.PrinterSettings.PaperSize = ePaperSize.A4;
        ws.PrinterSettings.Orientation = eOrientation.Portrait;
        ws.PrinterSettings.FitToPage = true;
        ws.PrinterSettings.FitToWidth = 1;
        ws.PrinterSettings.FitToHeight = 0;
        ws.PrinterSettings.TopMargin = 0.7;
        ws.PrinterSettings.BottomMargin = 0.7;
        ws.PrinterSettings.LeftMargin = 0.7;
        ws.PrinterSettings.RightMargin = 0.7;
    }

    // ── 제목 행 ──────────────────────────────────────────────────────────
    private static int WriteTitle(ExcelWorksheet ws, int row, bool vatExcl, int totalCols)
    {
        ws.Cells[row, 1, row, totalCols].Merge = true;
        var cell = ws.Cells[row, 1];
        cell.Value = "거 래 명 세 표";
        cell.Style.Font.Size = 18;
        cell.Style.Font.Bold = true;
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        ws.Row(row).Height = 36;

        // VAT 구분 라벨 (우측 끝에 작게)
        ws.Cells[row, totalCols].Value = vatExcl ? "(VAT 별도)" : "(VAT 포함)";
        ws.Cells[row, totalCols].Style.Font.Size = 8;
        ws.Cells[row, totalCols].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;

        ApplyOuterBorder(ws.Cells[row, 1, row, totalCols]);
        return row + 1;
    }

    // ── 공급자/공급받는자 블록 ───────────────────────────────────────────
    private static int WritePartyBlock(ExcelWorksheet ws, int startRow, DocParty supplier, DocParty buyer, int totalCols)
    {
        // VAT별도=10열: 공급자=1-5, 공급받는자=6-10 / VAT포함=9열: 공급자=1-4, 공급받는자=5-9
        int leftEnd    = totalCols / 2;   // 10→5, 9→4
        int rightStart = leftEnd + 1;
        int rightEnd   = totalCols;
        int r = startRow;

        // 공급자/공급받는자 라벨 행
        Merge(ws, r, 1, r, leftEnd, "공   급   자");
        StyleLabel(ws.Cells[r, 1], bold: true, center: true, size: 10);
        Merge(ws, r, rightStart, r, rightEnd, "공 급 받 는 자");
        StyleLabel(ws.Cells[r, rightStart], bold: true, center: true, size: 10);
        ApplyOuterBorder(ws.Cells[r, 1, r, totalCols]);
        ws.Row(r).Height = 18;
        r++;

        // 등록번호
        WritePartyRow(ws, r, leftEnd, rightStart, rightEnd, "등록번호", supplier.RegNo, "등록번호", buyer.RegNo, formatRegNo: true);
        r++;

        // 상호 + 대표자
        WritePartyTwoFieldRow(ws, r, leftEnd, rightStart, rightEnd,
            "상호(법인명)", supplier.CompanyName, "대표자", supplier.CeoName,
            "상호(법인명)", buyer.CompanyName, "대표자", buyer.CeoName);
        r++;

        // 사업장 주소
        WritePartyRow(ws, r, leftEnd, rightStart, rightEnd, "사업장 주소", supplier.Address, "사업장 주소", buyer.Address);
        r++;

        // 업태 + 종목
        WritePartyTwoFieldRow(ws, r, leftEnd, rightStart, rightEnd,
            "업태", supplier.BizType, "종목", supplier.BizItem,
            "업태", buyer.BizType, "종목", buyer.BizItem);
        r++;

        // 전화/이메일 (간소화: 하나의 행에)
        WritePartyRow(ws, r, leftEnd, rightStart, rightEnd, "전화/이메일",
            Join(supplier.Tel, supplier.Email), "전화/이메일", Join(buyer.Tel, buyer.Email));
        r++;

        return r;
    }

    private static void WritePartyRow(ExcelWorksheet ws, int r, int leftEnd, int rightStart, int rightEnd,
        string leftLabel, string leftVal, string rightLabel, string rightVal, bool formatRegNo = false)
    {
        ws.Cells[r, 1].Value = leftLabel;
        StyleLabel(ws.Cells[r, 1]);
        ws.Cells[r, 1].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        Merge(ws, r, 2, r, leftEnd, formatRegNo ? FormatRegNo(leftVal) : leftVal);
        ws.Cells[r, rightStart].Value = rightLabel;
        StyleLabel(ws.Cells[r, rightStart]);
        ws.Cells[r, rightStart].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        Merge(ws, r, rightStart + 1, r, rightEnd, formatRegNo ? FormatRegNo(rightVal) : rightVal);
        ApplyOuterBorder(ws.Cells[r, 1, r, rightEnd]);  // rightEnd = totalCols
        DrawMidBorder(ws, r, leftEnd, rightEnd);
        ws.Row(r).Height = 16;
    }

    private static void WritePartyTwoFieldRow(ExcelWorksheet ws, int r, int leftEnd, int rightStart, int rightEnd,
        string lLabel1, string lVal1, string lLabel2, string lVal2,
        string rLabel1, string rVal1, string rLabel2, string rVal2)
    {
        // Left: [lLabel1][lVal1][lLabel2][lVal2]
        ws.Cells[r, 1].Value = lLabel1;
        StyleLabel(ws.Cells[r, 1]);
        ws.Cells[r, 1].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        ws.Cells[r, 2].Value = lVal1;
        ws.Cells[r, 2, r, leftEnd - 1].Merge = true;
        ws.Cells[r, leftEnd - 1].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        ws.Cells[r, leftEnd].Value = lLabel2 + ": " + lVal2;
        StyleLabel(ws.Cells[r, leftEnd]);

        // Right: [rLabel1][rVal1][rLabel2][rVal2]
        ws.Cells[r, rightStart].Value = rLabel1;
        StyleLabel(ws.Cells[r, rightStart]);
        ws.Cells[r, rightStart].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        ws.Cells[r, rightStart + 1].Value = rVal1;
        ws.Cells[r, rightStart + 1, r, rightEnd - 1].Merge = true;
        ws.Cells[r, rightEnd - 1].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        ws.Cells[r, rightEnd].Value = rLabel2 + ": " + rVal2;
        StyleLabel(ws.Cells[r, rightEnd]);

        ApplyOuterBorder(ws.Cells[r, 1, r, rightEnd]);  // rightEnd = totalCols
        DrawMidBorder(ws, r, leftEnd, rightEnd);
        ws.Row(r).Height = 16;
    }

    // ── 데이터 헤더 ──────────────────────────────────────────────────────
    private static int WriteDataHeader(ExcelWorksheet ws, int row, bool vatExcl, int totalCols)
    {
        // VAT별도 10열: 연/월/일/품목/규격/수량/단가/공급가액/세액/비고
        // VAT포함  9열: 연/월/일/품목/규격/수량/단가(VAT포함)/합계금액(VAT포함)/비고
        string[] headers = vatExcl
            ? new[] { "연", "월", "일", "품  목", "규  격", "수량", "단  가", "공급가액", "세  액", "비  고" }
            : new[] { "연", "월", "일", "품  목", "규  격", "수량", "단가(VAT포함)", "합계금액(VAT포함)", "비  고" };

        for (int c = 1; c <= totalCols; c++)
        {
            var cell = ws.Cells[row, c];
            cell.Value = headers[c - 1];
            cell.Style.Font.Bold = true;
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(230, 230, 230));
            ApplyCellBorder(cell);
        }
        ws.Row(row).Height = 18;
        return row + 1;
    }

    // ── 데이터 행 ────────────────────────────────────────────────────────
    private static int WriteDataRows(ExcelWorksheet ws, int row, TradeStatementDoc doc, int totalCols, int colNote)
    {
        int maxLines = doc.MaxLines;
        var lines = doc.Lines.Where(l => !IsLineEmpty(l)).ToList();

        for (int i = 0; i < maxLines; i++)
        {
            var line = i < lines.Count ? lines[i] : null;
            WriteDataRow(ws, row + i, line, doc.IsVatExcluded, totalCols, colNote);
        }
        return row + maxLines;
    }

    private static void WriteDataRow(ExcelWorksheet ws, int row, DocLineItem? line, bool vatExcl, int totalCols, int colNote)
    {
        ws.Row(row).Height = 18; // 줄바꿈 대비 기본 높이를 15→18로 넓힘

        void SetNum(int col, decimal? v) {
            if (v.HasValue && v.Value != 0) {
                ws.Cells[row, col].Value = (double)v.Value;
                ws.Cells[row, col].Style.Numberformat.Format = "#,##0";
                ws.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            }
        }

        if (line != null)
        {
            if (line.Year > 0) ws.Cells[row, ColYear].Value = line.Year;
            if (line.Month > 0) ws.Cells[row, ColMonth].Value = line.Month;
            if (line.Day > 0) ws.Cells[row, ColDay].Value = line.Day;
            ws.Cells[row, ColItem].Value = line.ItemName;
            ws.Cells[row, ColItem].Style.WrapText = true; // 품목이 길면 셀 안에서 줄바꿈
            ws.Cells[row, ColSpec].Value = line.Spec;
            SetNum(ColQty, line.Qty == 0 ? (decimal?)null : line.Qty);
            SetNum(ColUnitPrice, line.UnitPrice == 0 ? (decimal?)null : line.UnitPrice);
            SetNum(ColSupply, line.SupplyAmount == 0 ? (decimal?)null : line.SupplyAmount);
            // VAT별도: 세액은 합계행에서만 표시 — 라인별 세액칸 비워둠
            // VAT포함: ColTax 열 자체가 없으므로 비고(colNote)는 9열에 쓴다
            ws.Cells[row, colNote].Value = line.Note;
        }

        for (int c = 1; c <= totalCols; c++)
        {
            ApplyCellBorder(ws.Cells[row, c]);
            if (c == ColYear || c == ColMonth || c == ColDay)
                ws.Cells[row, c].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }
    }

    // ── 합계 행 ──────────────────────────────────────────────────────────
    private static int WriteTotalRow(ExcelWorksheet ws, int row, TradeStatementDoc doc, int dataStart, int totalCols)
    {
        Merge(ws, row, 1, row, 3, "합   계");
        StyleLabel(ws.Cells[row, 1], bold: true, center: true);

        ws.Cells[row, ColQty].Value = (double)doc.TotalQty;
        ws.Cells[row, ColQty].Style.Numberformat.Format = "#,##0";
        ws.Cells[row, ColQty].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        ws.Cells[row, ColQty].Style.Font.Bold = true;

        ws.Cells[row, ColSupply].Value = (double)doc.TotalSupply;
        ws.Cells[row, ColSupply].Style.Numberformat.Format = "#,##0";
        ws.Cells[row, ColSupply].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        ws.Cells[row, ColSupply].Style.Font.Bold = true;

        // VAT별도에만 세액 합계(col 9)를 표시; VAT포함은 col 9 = 비고이므로 기재 안 함
        if (doc.IsVatExcluded)
        {
            ws.Cells[row, ColTax].Value = (double)doc.TotalTax;
            ws.Cells[row, ColTax].Style.Numberformat.Format = "#,##0";
            ws.Cells[row, ColTax].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            ws.Cells[row, ColTax].Style.Font.Bold = true;
        }

        for (int c = 1; c <= totalCols; c++)
            ApplyCellBorder(ws.Cells[row, c]);

        ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
        ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(230, 230, 230));
        ws.Row(row).Height = 16;
        return row + 1;
    }

    // ── 요약 행 ──────────────────────────────────────────────────────────
    private static int WriteSummaryRow(ExcelWorksheet ws, int row, TradeStatementDoc doc, int totalCols)
    {
        ws.Row(row).Height = 18;

        if (doc.IsVatExcluded)
        {
            // 10열: [공급가액][value][세액][value][합계금액][value][미수금][value][인수자][value]
            var labels = new[] { "공급가액", "", "세  액", "", "합계금액", "", "미수금", "", "인수자", "" };
            var values = new object?[]
            {
                null, (double)doc.TotalSupply,
                null, (double)doc.TotalTax,
                null, (double)doc.GrandTotal,
                null, doc.Unpaid,
                null, doc.Receiver
            };

            for (int c = 1; c <= totalCols; c++)
            {
                var cell = ws.Cells[row, c];
                if (labels[c - 1] != "")
                {
                    cell.Value = labels[c - 1];
                    StyleLabel(cell, bold: true, center: true, size: 8);
                }
                else if (values[c - 1] != null)
                {
                    cell.Value = values[c - 1];
                    if (values[c - 1] is double)
                    {
                        cell.Style.Numberformat.Format = "#,##0";
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    }
                }
                ApplyCellBorder(cell);
            }
        }
        else
        {
            // VAT포함 9열: [잔  액][value][비  고][merged→col9]
            ws.Cells[row, 1].Value = "잔  액";
            StyleLabel(ws.Cells[row, 1], bold: true, center: true);
            ws.Cells[row, 2].Value = doc.Unpaid;
            ws.Cells[row, 3].Value = "비  고";
            StyleLabel(ws.Cells[row, 3], bold: true, center: true);
            Merge(ws, row, 4, row, totalCols, "");
            for (int c = 1; c <= totalCols; c++) ApplyCellBorder(ws.Cells[row, c]);
        }

        return row + 1;
    }

    // ── 전자세금계산서 이메일 행 ─────────────────────────────────────────
    private static int WriteTaxInvoiceLine(ExcelWorksheet ws, int row, string email, int totalCols)
    {
        Merge(ws, row, 1, row, totalCols, $"전자세금계산서 발행 이메일: {email}");
        ws.Cells[row, 1].Style.Font.Size = 9;
        ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        ApplyOuterBorder(ws.Cells[row, 1, row, totalCols]);
        ws.Row(row).Height = 15;
        return row + 1;
    }

    // ── 하단 비고 ────────────────────────────────────────────────────────
    private static int WriteFooterNote(ExcelWorksheet ws, int row, string note, int totalCols)
    {
        Merge(ws, row, 1, row, totalCols, note);
        var cell = ws.Cells[row, 1];
        cell.Style.Font.Size = 9;
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        cell.Style.WrapText = true;
        ApplyOuterBorder(ws.Cells[row, 1, row, totalCols]);
        ws.Row(row).Height = 30;
        return row + 1;
    }

    // ── 발행일 ───────────────────────────────────────────────────────────
    private static void WriteIssueDate(ExcelWorksheet ws, int row, DateTime date)
        => WriteIssueDate(ws, row, date, TotalCols);

    private static void WriteIssueDate(ExcelWorksheet ws, int row, DateTime date, int totalCols)
    {
        Merge(ws, row, 1, row, totalCols, $"{date.Year}년 {date.Month}월 {date.Day}일");
        ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        ws.Cells[row, 1].Style.Font.Size = 10;
        ws.Row(row).Height = 18;
    }

    // ── 도장 이미지 삽입 ─────────────────────────────────────────────────
    private static void InsertStamp(ExcelWorksheet ws, string imagePath)
    {
        try
        {
            using var img = System.Drawing.Image.FromFile(imagePath);
            var pic = ws.Drawings.AddPicture("stamp", new FileInfo(imagePath));
            // 공급자 블록 우측 상단 (행 2, 열 4 근처)에 고정 앵커
            pic.SetPosition(1, 0, 3, 0);
            pic.SetSize(80, 80);
        }
        catch
        {
            // 도장 삽입 실패는 무시(출력은 계속)
        }
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────
    private static void Merge(ExcelWorksheet ws, int r1, int c1, int r2, int c2, string value)
    {
        var range = ws.Cells[r1, c1, r2, c2];
        range.Merge = true;
        range.Value = value;
        range.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
    }

    private static void StyleLabel(ExcelRangeBase cell, bool bold = false, bool center = false, float size = 9)
    {
        cell.Style.Font.Size = size;
        cell.Style.Font.Bold = bold;
        if (center)
        {
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
        cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(242, 242, 242));
    }

    private static void ApplyOuterBorder(ExcelRangeBase range)
    {
        range.Style.Border.Top.Style = ExcelBorderStyle.Medium;
        range.Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
        range.Style.Border.Left.Style = ExcelBorderStyle.Medium;
        range.Style.Border.Right.Style = ExcelBorderStyle.Medium;
    }

    private static void ApplyCellBorder(ExcelRangeBase cell)
    {
        cell.Style.Border.Top.Style = ExcelBorderStyle.Thin;
        cell.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        cell.Style.Border.Left.Style = ExcelBorderStyle.Thin;
        cell.Style.Border.Right.Style = ExcelBorderStyle.Thin;
    }

    private static void DrawMidBorder(ExcelWorksheet ws, int row, int leftEnd, int rightEnd)
    {
        ws.Cells[row, leftEnd].Style.Border.Right.Style = ExcelBorderStyle.Medium;
    }

    private static string Join(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b;
        if (string.IsNullOrWhiteSpace(b)) return a;
        return $"{a}  /  {b}";
    }

    private static string FormatRegNo(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 10) return $"{digits[..3]}-{digits[3..5]}-{digits[5..]}";
        return raw;
    }

    private static bool IsLineEmpty(DocLineItem l) =>
        string.IsNullOrWhiteSpace(l.ItemName) && l.Qty == 0 && l.UnitPrice == 0;

    // ════════════════════════════════════════════════════════════════════
    //  견적서 (기본 / 수량포함)
    // ════════════════════════════════════════════════════════════════════

    public static void ExportQuote(QuoteDoc doc, string filePath)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("견적서");

        bool withQty = !doc.IsBasic;
        int totalCols = withQty ? 9 : 5;

        SetQuoteColumnWidths(ws, withQty);
        SetPrintSettings(ws);

        int row = 1;
        row = WriteQuoteTitle(ws, row, doc, totalCols);
        row = WriteQuoteSupplierHeader(ws, row, doc, totalCols);
        row = WriteQuoteDataHeader(ws, row, withQty);
        int dataStart = row;
        row = WriteQuoteDataRows(ws, row, doc, withQty);
        if (withQty) row = WriteQuoteTotalRow(ws, row, doc, totalCols);
        if (!string.IsNullOrWhiteSpace(doc.FooterNote)) row = WriteFooterNote(ws, row, doc.FooterNote, totalCols);
        row = WriteQuoteSupplierFooter(ws, row, doc, totalCols);

        if (doc.StampImagePath != null && File.Exists(doc.StampImagePath))
            InsertStamp(ws, doc.StampImagePath);

        ExportHelper.SaveExcel(package, filePath);
    }

    private static void SetQuoteColumnWidths(ExcelWorksheet ws, bool withQty)
    {
        // 기본: 품목(1) / 단위(2) / 패킹(3) / 단가(4) / 비고(5)
        ws.Column(1).Width = 24;
        ws.Column(2).Width = 8;
        ws.Column(3).Width = 12;
        ws.Column(4).Width = 12;
        ws.Column(5).Width = withQty ? 8 : 16;
        if (withQty)
        {
            ws.Column(6).Width = 11;   // 금액
            ws.Column(7).Width = 11;   // 세액 (합계 행에서만 값)
            ws.Column(8).Width = 11;   // 합계금액
            ws.Column(9).Width = 14;   // 비고
        }
    }

    private static int WriteQuoteTitle(ExcelWorksheet ws, int row, QuoteDoc doc, int totalCols)
    {
        // 제목 행
        Merge(ws, row, 1, row, totalCols, doc.DocTitle);
        var titleCell = ws.Cells[row, 1];
        titleCell.Style.Font.Size = 18;
        titleCell.Style.Font.Bold = true;
        titleCell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        titleCell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        ws.Row(row).Height = 36;
        ApplyOuterBorder(titleCell);
        row++;

        // 발행일 + 수량포함용 단위 라벨
        if (!doc.IsBasic)
        {
            ws.Cells[row, 1].Value = $"발행일: {doc.IssueDate:yyyy년 M월 d일}";
            ws.Cells[row, 1].Style.Font.Size = 10;
            Merge(ws, row, totalCols - 1, row, totalCols, "(단위: 원)");
            ws.Cells[row, totalCols - 1].Style.Font.Size = 9;
            ws.Cells[row, totalCols - 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        }
        else
        {
            ws.Cells[row, 1].Value = $"발행일: {doc.IssueDate:yyyy년 M월 d일}";
            ws.Cells[row, 1].Style.Font.Size = 10;
        }
        ws.Row(row).Height = 16;
        row++;

        // 수신 / 제목 / 인사문 / 가격기준
        WriteQuoteInfoRow(ws, row++, totalCols, "수     신", doc.RecipientName);
        WriteQuoteInfoRow(ws, row++, totalCols, "제     목", doc.DocTitle == "견 적 서" ? "견 적 의 건" : doc.DocTitle);

        if (!string.IsNullOrWhiteSpace(doc.HeaderText))
        {
            Merge(ws, row, 1, row, totalCols, doc.HeaderText);
            ws.Cells[row, 1].Style.WrapText = true;
            ws.Cells[row, 1].Style.Font.Size = 9;
            ws.Row(row).Height = 40;
            row++;
        }

        WriteQuoteInfoRow(ws, row++, totalCols, "가격기준", $"({doc.PriceBasis}) 단가 기준");
        return row;
    }

    private static void WriteQuoteInfoRow(ExcelWorksheet ws, int row, int totalCols, string label, string value)
    {
        ws.Cells[row, 1].Value = label;
        StyleLabel(ws.Cells[row, 1]);
        ws.Cells[row, 1].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        Merge(ws, row, 2, row, totalCols, value);
        ApplyOuterBorder(ws.Cells[row, 1, row, totalCols]);
        ws.Row(row).Height = 16;
    }

    private static int WriteQuoteSupplierHeader(ExcelWorksheet ws, int row, QuoteDoc doc, int totalCols)
    {
        // 공급자 라벨 행
        Merge(ws, row, 1, row, totalCols, "■ 공  급  자");
        StyleLabel(ws.Cells[row, 1], bold: true);
        ws.Row(row).Height = 15;
        row++;

        // 공급자 정보 (한 행에 압축)
        var supInfo = $"{doc.Supplier.CompanyName}  |  대표: {doc.Supplier.CeoName}  |  {doc.Supplier.Address}  |  Tel: {doc.Supplier.Tel}";
        Merge(ws, row, 1, row, totalCols, supInfo);
        ws.Cells[row, 1].Style.Font.Size = 9;
        ApplyOuterBorder(ws.Cells[row, 1, row, totalCols]);
        ws.Row(row).Height = 15;
        row++;
        return row;
    }

    private static int WriteQuoteDataHeader(ExcelWorksheet ws, int row, bool withQty)
    {
        string[] headers = withQty
            ? new[] { "품     목", "단위", "패  킹", "단  가", "수량", "금  액", "세  액", "합계금액", "비  고" }
            : new[] { "품     목", "단위", "패  킹", "단  가", "비  고" };

        for (int c = 1; c <= headers.Length; c++)
        {
            var cell = ws.Cells[row, c];
            cell.Value = headers[c - 1];
            cell.Style.Font.Bold = true;
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(230, 230, 230));
            ApplyCellBorder(cell);
        }
        ws.Row(row).Height = 18;
        return row + 1;
    }

    private static int WriteQuoteDataRows(ExcelWorksheet ws, int row, QuoteDoc doc, bool withQty)
    {
        var lines = doc.Lines.Where(l => !string.IsNullOrWhiteSpace(l.ItemName) || l.UnitPrice != 0).ToList();
        int noteCol = withQty ? 9 : 5;

        foreach (var line in lines)
        {
            ws.Cells[row, 1].Value = line.ItemName;
            ws.Cells[row, 2].Value = line.Unit;
            ws.Cells[row, 3].Value = line.Packing;
            ws.Row(row).Height = 15;

            SetNum(ws, row, 4, line.UnitPrice);
            if (withQty)
            {
                SetNum(ws, row, 5, line.Qty);
                SetNum(ws, row, 6, line.Amount);
                // 세액: 합계 행에서만 (라인별 비워둠)
                SetNum(ws, row, 8, line.Amount); // 합계금액 = 금액(세액 없이) for line — actual total in total row
            }
            ws.Cells[row, noteCol].Value = line.Note;

            for (int c = 1; c <= noteCol; c++) ApplyCellBorder(ws.Cells[row, c]);
            row++;
        }
        return row;
    }

    private static int WriteQuoteTotalRow(ExcelWorksheet ws, int row, QuoteDoc doc, int totalCols)
    {
        Merge(ws, row, 1, row, 4, "계");
        StyleLabel(ws.Cells[row, 1], bold: true, center: true);

        SetNum(ws, row, 5, doc.TotalQty);
        SetNum(ws, row, 6, doc.TotalAmount);
        SetNum(ws, row, 7, doc.TotalTax);
        SetNum(ws, row, 8, doc.GrandTotal);

        for (int c = 1; c <= totalCols; c++)
        {
            ApplyCellBorder(ws.Cells[row, c]);
            ws.Cells[row, c].Style.Font.Bold = true;
        }
        ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
        ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(230, 230, 230));
        ws.Row(row).Height = 16;
        return row + 1;
    }

    private static int WriteQuoteSupplierFooter(ExcelWorksheet ws, int row, QuoteDoc doc, int totalCols)
    {
        ws.Row(row).Height = 22;
        var dateStr = $"{doc.IssueDate:yyyy년 M월 d일}";
        Merge(ws, row, 1, row, totalCols, dateStr);
        ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        ws.Cells[row, 1].Style.Font.Size = 10;
        row++;

        var supLine = $"상호: {doc.Supplier.CompanyName}   대표자: {doc.Supplier.CeoName}   {doc.Supplier.Address}";
        Merge(ws, row, 1, row, totalCols, supLine);
        ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        ws.Cells[row, 1].Style.Font.Size = 10;
        ws.Row(row).Height = 20;
        return row + 1;
    }

    // ════════════════════════════════════════════════════════════════════
    //  가격조정에 관한 건
    // ════════════════════════════════════════════════════════════════════

    public static void ExportPriceAdjustment(PriceAdjDoc doc, string filePath)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("가격조정공문");

        const int totalCols = 6;

        ws.Column(1).Width = 8;   // 번호
        ws.Column(2).Width = 22;  // 품명
        ws.Column(3).Width = 8;   // 단위
        ws.Column(4).Width = 12;  // 조정 전
        ws.Column(5).Width = 12;  // 조정 후
        ws.Column(6).Width = 16;  // 비고

        SetPrintSettings(ws);

        int row = 1;
        row = WritePriceAdjHeader(ws, row, doc, totalCols);
        row = WritePriceAdjDataHeader(ws, row);
        row = WritePriceAdjDataRows(ws, row, doc);
        row = WritePriceAdjFooter(ws, row, doc, totalCols);

        if (doc.StampImagePath != null && File.Exists(doc.StampImagePath))
            InsertStamp(ws, doc.StampImagePath);

        ExportHelper.SaveExcel(package, filePath);
    }

    private static int WritePriceAdjHeader(ExcelWorksheet ws, int row, PriceAdjDoc doc, int totalCols)
    {
        // 회사 헤더
        var compLine = $"{doc.Supplier.CompanyName}   {doc.Supplier.Address}   Tel: {doc.Supplier.Tel}";
        Merge(ws, row, 1, row, totalCols, compLine);
        ws.Cells[row, 1].Style.Font.Bold = true; ws.Cells[row, 1].Style.Font.Size = 11;
        ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        ws.Row(row).Height = 22; row++;

        // 문서번호 + 발신일
        if (!string.IsNullOrWhiteSpace(doc.DocNo))
        {
            ws.Cells[row, 1].Value = $"문서번호: {doc.DocNo}";
            Merge(ws, row, 4, row, totalCols, $"발신일: {doc.IssueDate:yyyy년 M월 d일}");
            ws.Cells[row, 4].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
            ws.Row(row).Height = 16; row++;
        }

        // 수신 / 발신 / 제목
        WriteQuoteInfoRow(ws, row++, totalCols, "수     신", doc.RecipientName);
        WriteQuoteInfoRow(ws, row++, totalCols, "발     신", $"{doc.Supplier.CompanyName} 대표 {doc.Supplier.CeoName}");
        WriteQuoteInfoRow(ws, row++, totalCols, "제     목", doc.DocTitle);

        // 가격기준 라벨
        WriteQuoteInfoRow(ws, row++, totalCols, "가격기준", $"({doc.PriceBasis}) 기준");

        // 상단 본문 (즐겨찾기 또는 직접 입력)
        if (!string.IsNullOrWhiteSpace(doc.BodyText))
        {
            Merge(ws, row, 1, row, totalCols, doc.BodyText);
            ws.Cells[row, 1].Style.WrapText = true;
            ws.Cells[row, 1].Style.Font.Size = 9;
            ws.Cells[row, 1].Style.VerticalAlignment = ExcelVerticalAlignment.Top;
            ApplyOuterBorder(ws.Cells[row, 1, row, totalCols]);
            ws.Row(row).Height = 80;
            row++;
        }

        // 적용일자
        WriteQuoteInfoRow(ws, row++, totalCols, "적 용 일 자", $"{doc.ApplyDate:yyyy년 M월 d일}부터 적용");

        return row;
    }

    private static int WritePriceAdjDataHeader(ExcelWorksheet ws, int row)
    {
        var headers = new[] { "번호", "품     명", "단위", "조정 전", "조정 후", "비  고" };
        for (int c = 1; c <= 6; c++)
        {
            var cell = ws.Cells[row, c];
            cell.Value = headers[c - 1];
            cell.Style.Font.Bold = true;
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(230, 230, 230));
            ApplyCellBorder(cell);
        }
        ws.Row(row).Height = 18;
        return row + 1;
    }

    private static int WritePriceAdjDataRows(ExcelWorksheet ws, int row, PriceAdjDoc doc)
    {
        int seq = 1;
        foreach (var line in doc.Lines.Where(l => !string.IsNullOrWhiteSpace(l.ItemName)))
        {
            ws.Cells[row, 1].Value = line.No > 0 ? line.No : seq;
            ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            ws.Cells[row, 2].Value = line.ItemName;
            ws.Cells[row, 3].Value = line.Unit;
            ws.Cells[row, 3].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            SetNum(ws, row, 4, line.PriceBefore);
            SetNum(ws, row, 5, line.PriceAfter);
            ws.Cells[row, 6].Value = line.Note;
            for (int c = 1; c <= 6; c++) ApplyCellBorder(ws.Cells[row, c]);
            ws.Row(row).Height = 15;
            seq++; row++;
        }
        return row;
    }

    private static int WritePriceAdjFooter(ExcelWorksheet ws, int row, PriceAdjDoc doc, int totalCols)
    {
        if (!string.IsNullOrWhiteSpace(doc.FooterNote))
            row = WriteFooterNote(ws, row, doc.FooterNote, totalCols);

        ws.Row(row).Height = 22;
        Merge(ws, row, 1, row, totalCols, $"{doc.IssueDate:yyyy년 M월 d일}");
        ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        ws.Cells[row, 1].Style.Font.Size = 10;
        row++;

        var supLine = $"상호: {doc.Supplier.CompanyName}   대표자: {doc.Supplier.CeoName}   {doc.Supplier.Address}";
        Merge(ws, row, 1, row, totalCols, supLine);
        ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
        ws.Cells[row, 1].Style.Font.Size = 10;
        ws.Row(row).Height = 20;
        return row + 1;
    }


    // ── 숫자 셀 공통 ────────────────────────────────────────────────────
    private static void SetNum(ExcelWorksheet ws, int row, int col, decimal v)
    {
        if (v == 0) return;
        ws.Cells[row, col].Value = (double)v;
        ws.Cells[row, col].Style.Numberformat.Format = "#,##0";
        ws.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
    }

    // ════════════════════════════════════════════════════════════════════
    //  매출장 (내부용) — 거래명세표와 동일한 틀에 제조원가/이익액 열을 선택적으로 추가
    // ════════════════════════════════════════════════════════════════════

    private readonly record struct LedgerLayout(
        int ColYear, int ColMonth, int ColDay, int ColItem, int ColSpec, int ColQty,
        int ColUnitPrice, int ColSupply, int ColCost, int ColProfit, int ColNote, int TotalCols)
    {
        public static LedgerLayout Build(bool showCost, bool showProfit)
        {
            int col = 8; // 연/월/일/품목/규격/수량/단가/공급가액까지 고정 8열
            int colCost = showCost ? ++col : -1;
            int colProfit = showProfit ? ++col : -1;
            int colNote = ++col;
            return new LedgerLayout(1, 2, 3, 4, 5, 6, 7, 8, colCost, colProfit, colNote, colNote);
        }
    }

    public static void ExportSalesLedger(SalesLedgerDoc doc, string filePath)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("매출장");
        var layout = LedgerLayout.Build(doc.ShowCost, doc.ShowProfit);

        SetLedgerColumnWidths(ws, layout);
        SetPrintSettings(ws);

        int row = 1;
        row = WriteLedgerTitle(ws, row, layout);
        row = WriteLedgerPartyBlock(ws, row, doc.Supplier, doc.Buyer, layout);
        row = WriteLedgerDataHeader(ws, row, layout);
        row = WriteLedgerDataRows(ws, row, doc, layout);
        row = WriteLedgerTotalRow(ws, row, doc, layout);
        if (!string.IsNullOrWhiteSpace(doc.FooterNote))
            row = WriteFooterNote(ws, row, doc.FooterNote, layout.TotalCols);
        WriteIssueDate(ws, row, doc.IssueDate, layout.TotalCols);

        if (doc.StampImagePath != null && File.Exists(doc.StampImagePath))
            InsertStamp(ws, doc.StampImagePath);

        ExportHelper.SaveExcel(package, filePath);
    }

    private static void SetLedgerColumnWidths(ExcelWorksheet ws, LedgerLayout l)
    {
        ws.Column(l.ColYear).Width = 5;
        ws.Column(l.ColMonth).Width = 4;
        ws.Column(l.ColDay).Width = 4;
        ws.Column(l.ColItem).Width = 20;
        ws.Column(l.ColSpec).Width = 10;
        ws.Column(l.ColQty).Width = 7;
        ws.Column(l.ColUnitPrice).Width = 11;
        ws.Column(l.ColSupply).Width = 11;
        if (l.ColCost > 0) ws.Column(l.ColCost).Width = 11;
        if (l.ColProfit > 0) ws.Column(l.ColProfit).Width = 11;
        ws.Column(l.ColNote).Width = 13;
    }

    private static int WriteLedgerTitle(ExcelWorksheet ws, int row, LedgerLayout l)
    {
        Merge(ws, row, 1, row, l.TotalCols, "매   출   장");
        var cell = ws.Cells[row, 1];
        cell.Style.Font.Size = 18;
        cell.Style.Font.Bold = true;
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        ws.Row(row).Height = 36;

        ws.Cells[row, l.TotalCols].Value = "(내부검토용)";
        ws.Cells[row, l.TotalCols].Style.Font.Size = 8;
        ws.Cells[row, l.TotalCols].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;

        ApplyOuterBorder(ws.Cells[row, 1, row, l.TotalCols]);
        return row + 1;
    }

    private static int WriteLedgerPartyBlock(ExcelWorksheet ws, int startRow, DocParty supplier, DocParty buyer, LedgerLayout l)
    {
        int leftEnd = l.TotalCols / 2;
        int rightStart = leftEnd + 1;
        int rightEnd = l.TotalCols;
        int r = startRow;

        Merge(ws, r, 1, r, leftEnd, "공   급   자");
        StyleLabel(ws.Cells[r, 1], bold: true, center: true, size: 10);
        Merge(ws, r, rightStart, r, rightEnd, "공 급 받 는 자");
        StyleLabel(ws.Cells[r, rightStart], bold: true, center: true, size: 10);
        ApplyOuterBorder(ws.Cells[r, 1, r, l.TotalCols]);
        ws.Row(r).Height = 18;
        r++;

        WriteLedgerPartyRow(ws, r, leftEnd, rightStart, rightEnd, "등록번호", FormatRegNo(supplier.RegNo), "등록번호", FormatRegNo(buyer.RegNo));
        r++;
        WriteLedgerPartyRow(ws, r, leftEnd, rightStart, rightEnd, "상호(법인명)", supplier.CompanyName, "상호(법인명)", buyer.CompanyName);
        r++;
        WriteLedgerPartyRow(ws, r, leftEnd, rightStart, rightEnd, "사업장 주소", supplier.Address, "사업장 주소", buyer.Address);
        r++;
        WriteLedgerPartyRow(ws, r, leftEnd, rightStart, rightEnd, "전화/이메일", Join(supplier.Tel, supplier.Email), "전화/이메일", Join(buyer.Tel, buyer.Email));
        r++;

        return r;
    }

    private static void WriteLedgerPartyRow(ExcelWorksheet ws, int r, int leftEnd, int rightStart, int rightEnd,
        string leftLabel, string leftVal, string rightLabel, string rightVal)
    {
        ws.Cells[r, 1].Value = leftLabel;
        StyleLabel(ws.Cells[r, 1]);
        ws.Cells[r, 1].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        Merge(ws, r, 2, r, leftEnd, leftVal);
        ws.Cells[r, rightStart].Value = rightLabel;
        StyleLabel(ws.Cells[r, rightStart]);
        ws.Cells[r, rightStart].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        Merge(ws, r, rightStart + 1, r, rightEnd, rightVal);
        ApplyOuterBorder(ws.Cells[r, 1, r, rightEnd]);
        ws.Cells[r, leftEnd].Style.Border.Right.Style = ExcelBorderStyle.Medium;
        ws.Row(r).Height = 16;
    }

    private static int WriteLedgerDataHeader(ExcelWorksheet ws, int row, LedgerLayout l)
    {
        void H(int col, string text)
        {
            var cell = ws.Cells[row, col];
            cell.Value = text;
            cell.Style.Font.Bold = true;
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(230, 230, 230));
            ApplyCellBorder(cell);
        }

        H(l.ColYear, "연"); H(l.ColMonth, "월"); H(l.ColDay, "일");
        H(l.ColItem, "품  목"); H(l.ColSpec, "규  격"); H(l.ColQty, "수량");
        H(l.ColUnitPrice, "단  가"); H(l.ColSupply, "공급가액");
        if (l.ColCost > 0) H(l.ColCost, "제조원가");
        if (l.ColProfit > 0) H(l.ColProfit, "이익액");
        H(l.ColNote, "비  고");

        ws.Row(row).Height = 18;
        return row + 1;
    }

    private static int WriteLedgerDataRows(ExcelWorksheet ws, int row, SalesLedgerDoc doc, LedgerLayout l)
    {
        var lines = doc.Lines.Where(x => !IsLedgerLineEmpty(x)).ToList();
        for (int i = 0; i < SalesLedgerDoc.MaxLines; i++)
        {
            var line = i < lines.Count ? lines[i] : null;
            WriteLedgerDataRow(ws, row + i, line, l);
        }
        return row + SalesLedgerDoc.MaxLines;
    }

    private static void WriteLedgerDataRow(ExcelWorksheet ws, int row, SalesLedgerLineItem? line, LedgerLayout l)
    {
        ws.Row(row).Height = 15;

        if (line != null)
        {
            if (line.Year > 0) ws.Cells[row, l.ColYear].Value = line.Year;
            if (line.Month > 0) ws.Cells[row, l.ColMonth].Value = line.Month;
            if (line.Day > 0) ws.Cells[row, l.ColDay].Value = line.Day;
            ws.Cells[row, l.ColItem].Value = line.ItemName;
            ws.Cells[row, l.ColSpec].Value = line.Spec;
            SetNum(ws, row, l.ColQty, line.Qty);
            SetNum(ws, row, l.ColUnitPrice, line.UnitPrice);
            SetNum(ws, row, l.ColSupply, line.SupplyAmount);
            if (l.ColCost > 0) SetNum(ws, row, l.ColCost, line.CostPrice);
            if (l.ColProfit > 0) SetNum(ws, row, l.ColProfit, line.ProfitAmount);
            ws.Cells[row, l.ColNote].Value = line.Note;
        }

        for (int c = 1; c <= l.TotalCols; c++)
        {
            ApplyCellBorder(ws.Cells[row, c]);
            if (c == l.ColYear || c == l.ColMonth || c == l.ColDay)
                ws.Cells[row, c].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }
    }

    private static int WriteLedgerTotalRow(ExcelWorksheet ws, int row, SalesLedgerDoc doc, LedgerLayout l)
    {
        Merge(ws, row, 1, row, l.ColDay, "합   계");
        StyleLabel(ws.Cells[row, 1], bold: true, center: true);

        SetNum(ws, row, l.ColQty, doc.TotalQty);
        ws.Cells[row, l.ColQty].Style.Font.Bold = true;
        SetNum(ws, row, l.ColSupply, doc.TotalSupply);
        ws.Cells[row, l.ColSupply].Style.Font.Bold = true;
        if (l.ColCost > 0)
        {
            SetNum(ws, row, l.ColCost, doc.TotalCost);
            ws.Cells[row, l.ColCost].Style.Font.Bold = true;
        }
        if (l.ColProfit > 0)
        {
            SetNum(ws, row, l.ColProfit, doc.TotalProfit);
            ws.Cells[row, l.ColProfit].Style.Font.Bold = true;
        }

        for (int c = 1; c <= l.TotalCols; c++) ApplyCellBorder(ws.Cells[row, c]);
        ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
        ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(230, 230, 230));
        ws.Row(row).Height = 16;
        return row + 1;
    }

    private static bool IsLedgerLineEmpty(SalesLedgerLineItem l) =>
        string.IsNullOrWhiteSpace(l.ItemName) && l.Qty == 0 && l.UnitPrice == 0;

    // ════════════════════════════════════════════════════════════════════
    //  레거시 거래명세표 마이그레이션 재현 출력 (통일 양식 1종, 거래명세표_DB이식_개발스펙.md §5-A)
    //  원본 8종 템플릿을 그대로 재현하지 않는다 — DocStatementLine은 이관 시점에 VAT 파생이 끝나
    //  공급가액/세액/합계가 항상 채워져 있어 굳이 원본별로 다른 레이아웃을 쓸 필요가 없다.
    //  기존 ColYear~ColNote 상수는 건드리지 않고 이 섹션 전용 지역 상수 세트를 쓴다.
    // ════════════════════════════════════════════════════════════════════

    private const int StColYear = 1;
    private const int StColMonth = 2;
    private const int StColDay = 3;
    private const int StColItem = 4;
    private const int StColSpec = 5;
    private const int StColQty = 6;
    private const int StColUnitPrice = 7;
    private const int StColSupply = 8;
    private const int StColTax = 9;
    private const int StColTotal = 10;
    private const int StColNote = 11;
    private const int StTotalCols = 11;

    /// <summary>선택된 발행건들을 워크북 1개에 시트별로 재현 출력한다(합계는 재계산하지 않고 저장된 값 그대로).</summary>
    public static void ExportLegacyStatements(List<LegacyStatementExportItem> items, string filePath)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var usedSheetNames = new HashSet<string>();

        foreach (var item in items)
        {
            var ws = package.Workbook.Worksheets.Add(UniqueSheetName(usedSheetNames, item.Statement.SourceSheetName));
            SetStatementColumnWidths(ws);
            SetPrintSettings(ws);

            int row = 1;
            row = WriteStatementTitle(ws, row);
            row = WriteStatementPartyBlock(ws, row, item.Supplier, item.Buyer);
            row = WriteStatementDataHeader(ws, row);
            row = WriteStatementDataRows(ws, row, item.Lines);
            row = WriteStatementTotalRow(ws, row, item.Statement);
            if (!string.IsNullOrWhiteSpace(item.Statement.ReconcileNote) || item.Statement.CarryoverBalance != 0)
                row = WriteStatementReconcileNote(ws, row, item.Statement);
            if (item.Statement.IssueDate.HasValue)
                WriteIssueDate(ws, row, item.Statement.IssueDate.Value, StTotalCols);
        }

        ExportHelper.SaveExcel(package, filePath);
    }

    /// <summary>엑셀 시트명 제약(31자, \/?*[]: 금지)과 워크북 내 중복을 함께 방어한다.</summary>
    private static string UniqueSheetName(HashSet<string> used, string desired)
    {
        string name = string.IsNullOrWhiteSpace(desired) ? "Sheet" : desired;
        foreach (var ch in new[] { '\\', '/', '?', '*', '[', ']', ':' }) name = name.Replace(ch, '_');
        if (name.Length > 31) name = name[..31];

        string candidate = name;
        int suffix = 2;
        while (!used.Add(candidate))
        {
            string suffixStr = $"_{suffix++}";
            candidate = name.Length + suffixStr.Length > 31 ? name[..(31 - suffixStr.Length)] + suffixStr : name + suffixStr;
        }
        return candidate;
    }

    private static void SetStatementColumnWidths(ExcelWorksheet ws)
    {
        ws.Column(StColYear).Width = 5;
        ws.Column(StColMonth).Width = 4;
        ws.Column(StColDay).Width = 4;
        ws.Column(StColItem).Width = 22;
        ws.Column(StColSpec).Width = 11;
        ws.Column(StColQty).Width = 7;
        ws.Column(StColUnitPrice).Width = 11;
        ws.Column(StColSupply).Width = 11;
        ws.Column(StColTax).Width = 11;
        ws.Column(StColTotal).Width = 12;
        ws.Column(StColNote).Width = 13;
    }

    private static int WriteStatementTitle(ExcelWorksheet ws, int row)
    {
        Merge(ws, row, 1, row, StTotalCols, "거 래 명 세 표 (이관 재현)");
        var cell = ws.Cells[row, 1];
        cell.Style.Font.Size = 16;
        cell.Style.Font.Bold = true;
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        ws.Row(row).Height = 32;
        ApplyOuterBorder(ws.Cells[row, 1, row, StTotalCols]);
        return row + 1;
    }

    private static int WriteStatementPartyBlock(ExcelWorksheet ws, int startRow, DocParty supplier, DocParty buyer)
    {
        int leftEnd = StTotalCols / 2;
        int rightStart = leftEnd + 1;
        int rightEnd = StTotalCols;
        int r = startRow;

        Merge(ws, r, 1, r, leftEnd, "공   급   자");
        StyleLabel(ws.Cells[r, 1], bold: true, center: true, size: 10);
        Merge(ws, r, rightStart, r, rightEnd, "공 급 받 는 자");
        StyleLabel(ws.Cells[r, rightStart], bold: true, center: true, size: 10);
        ApplyOuterBorder(ws.Cells[r, 1, r, StTotalCols]);
        ws.Row(r).Height = 18;
        r++;

        WriteStatementPartyRow(ws, r, leftEnd, rightStart, rightEnd, "등록번호", FormatRegNo(supplier.RegNo), "등록번호", FormatRegNo(buyer.RegNo));
        r++;
        WriteStatementPartyRow(ws, r, leftEnd, rightStart, rightEnd, "상호(법인명)", supplier.CompanyName, "상호(법인명)", buyer.CompanyName);
        r++;
        WriteStatementPartyRow(ws, r, leftEnd, rightStart, rightEnd, "사업장 주소", supplier.Address, "사업장 주소", buyer.Address);
        r++;
        WriteStatementPartyRow(ws, r, leftEnd, rightStart, rightEnd, "전화/이메일", Join(supplier.Tel, supplier.Email), "전화/이메일", Join(buyer.Tel, buyer.Email));
        r++;

        return r;
    }

    private static void WriteStatementPartyRow(ExcelWorksheet ws, int r, int leftEnd, int rightStart, int rightEnd,
        string leftLabel, string leftVal, string rightLabel, string rightVal)
    {
        ws.Cells[r, 1].Value = leftLabel;
        StyleLabel(ws.Cells[r, 1]);
        ws.Cells[r, 1].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        Merge(ws, r, 2, r, leftEnd, leftVal);
        ws.Cells[r, rightStart].Value = rightLabel;
        StyleLabel(ws.Cells[r, rightStart]);
        ws.Cells[r, rightStart].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        Merge(ws, r, rightStart + 1, r, rightEnd, rightVal);
        ApplyOuterBorder(ws.Cells[r, 1, r, rightEnd]);
        ws.Cells[r, leftEnd].Style.Border.Right.Style = ExcelBorderStyle.Medium;
        ws.Row(r).Height = 16;
    }

    private static int WriteStatementDataHeader(ExcelWorksheet ws, int row)
    {
        var headers = new[] { "연", "월", "일", "품  목", "규  격", "수량", "단  가", "공급가액", "세  액", "합계금액", "비  고" };
        for (int c = 1; c <= StTotalCols; c++)
        {
            var cell = ws.Cells[row, c];
            cell.Value = headers[c - 1];
            cell.Style.Font.Bold = true;
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(230, 230, 230));
            ApplyCellBorder(cell);
        }
        ws.Row(row).Height = 18;
        return row + 1;
    }

    private static int WriteStatementDataRows(ExcelWorksheet ws, int row, List<DocStatementLine> lines)
    {
        foreach (var line in lines)
        {
            ws.Row(row).Height = 15;
            if (line.LineDate.HasValue)
            {
                ws.Cells[row, StColYear].Value = line.LineDate.Value.Year;
                ws.Cells[row, StColMonth].Value = line.LineDate.Value.Month;
                ws.Cells[row, StColDay].Value = line.LineDate.Value.Day;
            }
            ws.Cells[row, StColItem].Value = line.ItemName;
            ws.Cells[row, StColSpec].Value = line.Spec;
            SetNum(ws, row, StColQty, line.Qty);
            SetNum(ws, row, StColUnitPrice, line.UnitPrice);
            SetNum(ws, row, StColSupply, line.SupplyAmount);
            SetNum(ws, row, StColTax, line.Tax);
            SetNum(ws, row, StColTotal, line.Total);
            ws.Cells[row, StColNote].Value = line.Note;

            for (int c = 1; c <= StTotalCols; c++)
            {
                ApplyCellBorder(ws.Cells[row, c]);
                if (c == StColYear || c == StColMonth || c == StColDay)
                    ws.Cells[row, c].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }
            row++;
        }
        return row;
    }

    private static int WriteStatementTotalRow(ExcelWorksheet ws, int row, DocStatement statement)
    {
        Merge(ws, row, 1, row, StColDay, "합   계");
        StyleLabel(ws.Cells[row, 1], bold: true, center: true);

        SetNum(ws, row, StColQty, statement.TotalQty);
        ws.Cells[row, StColQty].Style.Font.Bold = true;
        SetNum(ws, row, StColSupply, statement.TotalSupply);
        ws.Cells[row, StColSupply].Style.Font.Bold = true;
        SetNum(ws, row, StColTax, statement.TotalTax);
        ws.Cells[row, StColTax].Style.Font.Bold = true;
        SetNum(ws, row, StColTotal, statement.TotalAmount);
        ws.Cells[row, StColTotal].Style.Font.Bold = true;

        for (int c = 1; c <= StTotalCols; c++) ApplyCellBorder(ws.Cells[row, c]);
        ws.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
        ws.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(230, 230, 230));
        ws.Row(row).Height = 16;
        return row + 1;
    }

    private static int WriteStatementReconcileNote(ExcelWorksheet ws, int row, DocStatement statement)
    {
        string text = statement.CarryoverBalance != 0
            ? $"이월잔액: {statement.CarryoverBalance:#,##0}원" + (string.IsNullOrWhiteSpace(statement.ReconcileNote) ? "" : $" / {statement.ReconcileNote}")
            : statement.ReconcileNote;
        Merge(ws, row, 1, row, StTotalCols, text);
        ws.Cells[row, 1].Style.Font.Size = 9;
        ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        ApplyOuterBorder(ws.Cells[row, 1, row, StTotalCols]);
        ws.Row(row).Height = 18;
        return row + 1;
    }
}

using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Exporters;

/// <summary>
/// CSKU별 통계 배치를 엑셀 6시트로 내보낸다(CSKU별통계_개발기획서.md §8 — S7).
/// 피벗 편의를 위해 집계 시트는 병합셀·합계행 없는 순수 flat 표로 만든다.
/// </summary>
public static class CskuStatExporter
{
    private static readonly string[] AggregateHeaders =
        ["기간", "구분", "채널코드", "채널명", "CSKU", "상품그룹", "상품명", "건수", "수량", "매출액", "정산액", "배송비", "입출고비", "이익액", "마진율"];

    private static readonly string[] AmazonExtraHeaders = ["매출액(원)", "정산액(원)", "이익액(원)", "적용환율"];

    private static readonly string[] ExceptionHeaders =
        ["파일명", "구분", "채널코드", "채널명", "상품그룹", "상품명", "옵션명", "매핑SKU", "수량", "매출액", "정산액", "배송비", "입출고비", "이익액", "상태", "분류"];

    private static readonly string[] RawHeaders =
        ["채널", "상품그룹", "상품명", "옵션명", "매핑SKU", "수량", "매출액", "정산액", "배송비", "입출고비", "이익액", "상태", "파일명"];

    public static void Export(
        CskuStatBatch batch,
        IReadOnlyList<CskuStatLine> lines,
        IReadOnlyList<CskuStatSourceRow> sourceRows,
        IReadOnlyList<CskuStatFile> files,
        string filePath,
        bool includeRawSheet,
        Func<string, string> resolveChannelName)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();

        WriteAggregateSheet(package, "CSKU집계", batch, lines.Where(l => l.FileKind == CskuFileKind.General).ToList(), isAmazon: false);

        if (lines.Any(l => l.FileKind == CskuFileKind.RocketGross))
        {
            WriteAggregateSheet(package, "로켓그로스", batch, lines.Where(l => l.FileKind == CskuFileKind.RocketGross).ToList(), isAmazon: false, shippingHeaderOverride: "그로스배송비");
        }

        if (lines.Any(l => l.FileKind == CskuFileKind.Amazon))
        {
            WriteAggregateSheet(package, "아마존", batch, lines.Where(l => l.FileKind == CskuFileKind.Amazon).ToList(), isAmazon: true);
        }

        WriteExceptionSheet(package, sourceRows.Where(r => r.RowClass != CskuStatRowClass.Normal).ToList(), resolveChannelName);

        if (includeRawSheet)
        {
            WriteRawSheet(package, sourceRows.Where(r => r.RowClass == CskuStatRowClass.Normal).ToList());
        }

        WriteLoadInfoSheet(package, batch, sourceRows, files);

        ExportHelper.SaveExcel(package, filePath);
    }

    private static void WriteAggregateSheet(
        ExcelPackage package, string sheetName, CskuStatBatch batch, IReadOnlyList<CskuStatLine> lines, bool isAmazon, string? shippingHeaderOverride = null)
    {
        var sheet = package.Workbook.Worksheets.Add(sheetName);
        var headers = isAmazon ? [.. AggregateHeaders, .. AmazonExtraHeaders] : AggregateHeaders;
        for (int i = 0; i < headers.Length; i++) sheet.Cells[1, i + 1].Value = headers[i];
        if (shippingHeaderOverride != null)
        {
            sheet.Cells[1, Array.IndexOf(AggregateHeaders, "배송비") + 1].Value = shippingHeaderOverride;
        }

        int row = 2;
        foreach (var line in lines)
        {
            int col = 1;
            sheet.Cells[row, col++].Value = batch.Period;
            sheet.Cells[row, col++].Value = line.FileKind.ToDisplayName();
            sheet.Cells[row, col++].Value = line.ChannelCode;
            sheet.Cells[row, col++].Value = line.ChannelName;
            sheet.Cells[row, col++].Value = line.CskuCode;
            sheet.Cells[row, col++].Value = line.ProductGroup;
            sheet.Cells[row, col++].Value = line.ProductName;
            sheet.Cells[row, col++].Value = line.RowCount;
            sheet.Cells[row, col++].Value = line.Qty;
            sheet.Cells[row, col].Value = line.Revenue; sheet.Cells[row, col].Style.Numberformat.Format = "#,##0"; col++;
            sheet.Cells[row, col].Value = line.Settlement; sheet.Cells[row, col].Style.Numberformat.Format = "#,##0"; col++;
            sheet.Cells[row, col].Value = line.Shipping; sheet.Cells[row, col].Style.Numberformat.Format = "#,##0"; col++;
            sheet.Cells[row, col].Value = line.Fee; sheet.Cells[row, col].Style.Numberformat.Format = "#,##0"; col++;
            sheet.Cells[row, col].Value = line.Profit; sheet.Cells[row, col].Style.Numberformat.Format = "#,##0"; col++;
            if (line.MarginRate.HasValue)
            {
                sheet.Cells[row, col].Value = line.MarginRate.Value;
                sheet.Cells[row, col].Style.Numberformat.Format = "0.0%";
            }
            col++;

            if (isAmazon)
            {
                sheet.Cells[row, col].Value = line.Revenue * batch.ExchangeRate; sheet.Cells[row, col].Style.Numberformat.Format = "#,##0"; col++;
                sheet.Cells[row, col].Value = line.Settlement * batch.ExchangeRate; sheet.Cells[row, col].Style.Numberformat.Format = "#,##0"; col++;
                sheet.Cells[row, col].Value = line.Profit * batch.ExchangeRate; sheet.Cells[row, col].Style.Numberformat.Format = "#,##0"; col++;
                sheet.Cells[row, col].Value = batch.ExchangeRate;
            }

            row++;
        }

        sheet.Cells[1, 1, 1, headers.Length].AutoFitColumns(8, 50);
    }

    private static void WriteExceptionSheet(ExcelPackage package, IReadOnlyList<CskuStatSourceRow> rows, Func<string, string> resolveChannelName)
    {
        var sheet = package.Workbook.Worksheets.Add("예외·미매핑");
        for (int i = 0; i < ExceptionHeaders.Length; i++) sheet.Cells[1, i + 1].Value = ExceptionHeaders[i];

        int row = 2;
        foreach (var r in rows)
        {
            sheet.Cells[row, 1].Value = r.FileName;
            sheet.Cells[row, 2].Value = r.FileKind.ToDisplayName();
            sheet.Cells[row, 3].Value = r.ChannelCode;
            sheet.Cells[row, 4].Value = resolveChannelName(r.ChannelCode);
            sheet.Cells[row, 5].Value = r.ProductGroup;
            sheet.Cells[row, 6].Value = r.ProductName;
            sheet.Cells[row, 7].Value = r.OptionName;
            sheet.Cells[row, 8].Value = r.CskuCode;
            sheet.Cells[row, 9].Value = r.Qty;
            sheet.Cells[row, 10].Value = r.Revenue;
            sheet.Cells[row, 11].Value = r.Settlement;
            sheet.Cells[row, 12].Value = r.Shipping;
            sheet.Cells[row, 13].Value = r.Fee;
            sheet.Cells[row, 14].Value = r.Profit;
            sheet.Cells[row, 15].Value = r.Status;
            sheet.Cells[row, 16].Value = r.RowClass == CskuStatRowClass.Excluded ? "예외" : "미매핑";
            row++;
        }

        sheet.Cells[1, 1, 1, ExceptionHeaders.Length].AutoFitColumns(8, 50);
    }

    private static void WriteRawSheet(ExcelPackage package, IReadOnlyList<CskuStatSourceRow> rows)
    {
        var sheet = package.Workbook.Worksheets.Add("원본행");
        for (int i = 0; i < RawHeaders.Length; i++) sheet.Cells[1, i + 1].Value = RawHeaders[i];

        int row = 2;
        foreach (var r in rows)
        {
            sheet.Cells[row, 1].Value = r.ChannelCode;
            sheet.Cells[row, 2].Value = r.ProductGroup;
            sheet.Cells[row, 3].Value = r.ProductName;
            sheet.Cells[row, 4].Value = r.OptionName;
            sheet.Cells[row, 5].Value = r.CskuCode;
            sheet.Cells[row, 6].Value = r.Qty;
            sheet.Cells[row, 7].Value = r.Revenue;
            sheet.Cells[row, 8].Value = r.Settlement;
            sheet.Cells[row, 9].Value = r.Shipping;
            sheet.Cells[row, 10].Value = r.Fee;
            sheet.Cells[row, 11].Value = r.Profit;
            sheet.Cells[row, 12].Value = r.Status;
            sheet.Cells[row, 13].Value = r.FileName;
            row++;
        }

        sheet.Cells[1, 1, 1, RawHeaders.Length].AutoFitColumns(8, 50);
    }

    private static void WriteLoadInfoSheet(ExcelPackage package, CskuStatBatch batch, IReadOnlyList<CskuStatSourceRow> sourceRows, IReadOnlyList<CskuStatFile> files)
    {
        var sheet = package.Workbook.Worksheets.Add("로드정보");

        sheet.Cells[1, 1].Value = "기간"; sheet.Cells[1, 2].Value = batch.Period;
        sheet.Cells[2, 1].Value = "환율"; sheet.Cells[2, 2].Value = batch.ExchangeRate;
        sheet.Cells[3, 1].Value = "메모"; sheet.Cells[3, 2].Value = batch.Memo;
        sheet.Cells[4, 1].Value = "생성일시"; sheet.Cells[4, 2].Value = batch.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
        sheet.Cells[5, 1].Value = "배치Id"; sheet.Cells[5, 2].Value = batch.Id;

        string[] fileTableHeaders = ["파일명", "구분", "전체행수", "정상행수", "예외행수", "미매핑행수", "수량합", "매출액합", "이익액합"];
        var headerRow = 7;
        for (int i = 0; i < fileTableHeaders.Length; i++) sheet.Cells[headerRow, i + 1].Value = fileTableHeaders[i];

        var byFile = sourceRows.ToLookup(r => r.FileName);
        var row = headerRow + 1;
        foreach (var file in files)
        {
            var rowsForFile = byFile[file.FileName];
            sheet.Cells[row, 1].Value = file.FileName;
            sheet.Cells[row, 2].Value = file.FileKind.ToDisplayName();
            sheet.Cells[row, 3].Value = file.RowCount;
            sheet.Cells[row, 4].Value = rowsForFile.Count(r => r.RowClass == CskuStatRowClass.Normal);
            sheet.Cells[row, 5].Value = rowsForFile.Count(r => r.RowClass == CskuStatRowClass.Excluded);
            sheet.Cells[row, 6].Value = rowsForFile.Count(r => r.RowClass == CskuStatRowClass.Unmapped);
            sheet.Cells[row, 7].Value = file.SumQty;
            sheet.Cells[row, 8].Value = file.SumRevenue;
            sheet.Cells[row, 9].Value = file.SumProfit;
            row++;
        }

        sheet.Cells[headerRow, 1, headerRow, fileTableHeaders.Length].AutoFitColumns(8, 50);
    }
}

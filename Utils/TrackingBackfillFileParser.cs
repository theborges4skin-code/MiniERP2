using System.Globalization;
using MiniERP2.Models;
using OfficeOpenXml;

namespace MiniERP2.Utils;

/// <summary>
/// 운송장 결과 파일을 "운송장 파일 누락건 점검"용으로 읽어 <see cref="TrackingBackfillRow"/> 목록으로
/// 변환합니다. 기존 발주/출고 이력 관리창의 운송장번호 불러오기(OutboundHistoryForm.ImportTrackingFile)와
/// 달리, DB의 기존 발주확정 건과 대조하지 않고 파일에 있는 유효 행을 전부(등록 여부 무관) 읽습니다 —
/// 운임 통계는 등록 여부와 무관하게 파일 단위로 산출해야 하기 때문입니다.
/// </summary>
public static class TrackingBackfillFileParser
{
    public class ParseResult
    {
        public List<TrackingBackfillRow> Rows { get; init; } = [];
        public int SkippedNoTrackingNo { get; init; }
        public string? Error { get; init; }
    }

    public static ParseResult Parse(ExcelWorksheet worksheet, CourierMaster courier, string sourceFileName)
    {
        var headerRow = courier.TrackingImportHeaderRow;
        if (worksheet.Dimension == null || headerRow > worksheet.Dimension.End.Row)
        {
            return new ParseResult { Error = "엑셀 파일에서 데이터를 찾을 수 없습니다." };
        }

        var recipientHeaderCandidates = courier.TrackingImportRecipientHeader
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int? recipientCol = null, trackingCol = null, orderNoCol = null, addressCol = null,
            productNameCol = null, receivedDateCol = null, freightCol = null;

        for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
        {
            var header = worksheet.Cells[headerRow, col].Value?.ToString()?.Trim();
            if (header is null) continue;
            if (recipientHeaderCandidates.Any(h => string.Equals(header, h, StringComparison.OrdinalIgnoreCase))) recipientCol = col;
            if (string.Equals(header, courier.TrackingImportTrackingNoHeader, StringComparison.OrdinalIgnoreCase)) trackingCol = col;
            if (!string.IsNullOrWhiteSpace(courier.TrackingImportOrderNoHeader) && string.Equals(header, courier.TrackingImportOrderNoHeader, StringComparison.OrdinalIgnoreCase)) orderNoCol = col;
            if (!string.IsNullOrWhiteSpace(courier.TrackingImportAddressHeader) && string.Equals(header, courier.TrackingImportAddressHeader, StringComparison.OrdinalIgnoreCase)) addressCol = col;
            if (!string.IsNullOrWhiteSpace(courier.TrackingImportProductNameHeader) && string.Equals(header, courier.TrackingImportProductNameHeader, StringComparison.OrdinalIgnoreCase)) productNameCol = col;
            if (!string.IsNullOrWhiteSpace(courier.TrackingImportReceivedDateHeader) && string.Equals(header, courier.TrackingImportReceivedDateHeader, StringComparison.OrdinalIgnoreCase)) receivedDateCol = col;
            if (!string.IsNullOrWhiteSpace(courier.TrackingImportFreightCostHeader) && string.Equals(header, courier.TrackingImportFreightCostHeader, StringComparison.OrdinalIgnoreCase)) freightCol = col;
        }

        if (recipientCol is null || trackingCol is null)
        {
            return new ParseResult
            {
                Error = $"설정된 헤더(\"{courier.TrackingImportRecipientHeader}\"/\"{courier.TrackingImportTrackingNoHeader}\")를 {headerRow}행에서 찾지 못했습니다.\n택배사 양식 설정을 확인하세요.",
            };
        }

        var rows = new List<TrackingBackfillRow>();
        var skipped = 0;

        for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
        {
            var recipient = worksheet.Cells[row, recipientCol.Value].Value?.ToString()?.Trim() ?? string.Empty;
            var trackingNo = worksheet.Cells[row, trackingCol.Value].Value?.ToString()?.Trim() ?? string.Empty;

            // 운송장번호 공란 행은 키를 만들 수 없어 주입 대상에서 제외한다(§3.4).
            if (string.IsNullOrWhiteSpace(trackingNo))
            {
                skipped++;
                continue;
            }

            rows.Add(new TrackingBackfillRow
            {
                ReceivedAt = receivedDateCol is { } rc ? ParseDate(worksheet.Cells[row, rc]) : null,
                TrackingNo = trackingNo,
                Recipient = recipient,
                Address = addressCol is { } ac ? worksheet.Cells[row, ac].Value?.ToString()?.Trim() ?? string.Empty : string.Empty,
                ProductName = productNameCol is { } pc ? worksheet.Cells[row, pc].Value?.ToString()?.Trim() ?? string.Empty : string.Empty,
                OrderNoMemo = orderNoCol is { } oc ? worksheet.Cells[row, oc].Value?.ToString()?.Trim() ?? string.Empty : string.Empty,
                FreightCost = freightCol is { } fc ? ParseFreightCost(worksheet.Cells[row, fc]) : 0m,
                SourceFileName = sourceFileName,
                SourceRowNumber = row,
            });
        }

        foreach (var r in rows) r.Label = TrackingLabelClassifier.Classify(r);

        return new ParseResult { Rows = rows, SkippedNoTrackingNo = skipped };
    }

    private static DateTime? ParseDate(ExcelRange cell)
    {
        if (cell.Value is DateTime dt) return dt;
        if (cell.Value is double serial && serial > 0)
        {
            try { return DateTime.FromOADate(serial); }
            catch (ArgumentException) { /* 아래 문자열 파싱으로 계속 시도 */ }
        }

        var text = cell.Text?.Trim();
        if (!string.IsNullOrEmpty(text) && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        return null;
    }

    private static decimal ParseFreightCost(ExcelRange cell)
    {
        if (cell.Value is double d) return (decimal)d;
        if (cell.Value is decimal m) return m;

        var text = cell.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return 0m;

        var cleaned = text.Replace(",", "").Replace("₩", "").Replace("원", "").Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }
}

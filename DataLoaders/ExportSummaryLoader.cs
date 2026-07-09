using System.Globalization;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 수출요약보고서(export_summary_report_기획.md)의 3개 트랙(A=수출신고, B=판매, C=송금)을 읽어
/// (마켓, 연월, 금액) 단위 원자료로 변환한다. 세 트랙은 서로 금액을 일치시키려 하지 않고
/// 각자 독립적으로 집계한다(기획서 1절 설계 원칙).
/// </summary>
public static class ExportSummaryLoader
{
    public record LoadResult(List<ExportSummaryEntry> Entries, int UnmatchedRowCount, string? UnmatchedSample, List<ExportSummaryRawEntry>? UsdRawEntries = null);

    /// <summary>트랙A: 수출신고 내역 한 파일. 통화코드 컬럼으로 마켓을 나눈다. CurrencyToMarket에
    /// 없는 통화(예: USD)는 미매핑으로 세어 UnmatchedRowCount로 알려준다.</summary>
    public static LoadResult LoadDeclarationFile(string filePath, DeclarationTrackConfig config)
    {
        using var package = ExcelFileOpener.Open(filePath);
        var sheet = RequireFirstSheet(package);

        var columns = ResolveColumns(sheet, config.HeaderRow, config.DateColumn, config.AmountColumn, config.CurrencyColumn);
        RequireColumns(columns, config.DateColumn, config.AmountColumn, config.CurrencyColumn);

        var entries = new List<ExportSummaryEntry>();
        var usdRawEntries = new List<ExportSummaryRawEntry>();
        var unmatched = 0;
        string? unmatchedSample = null;
        var lastRow = sheet.Dimension?.End.Row ?? config.HeaderRow;

        for (var row = config.HeaderRow + 1; row <= lastRow; row++)
        {
            var currency = sheet.Cells[row, columns[config.CurrencyColumn]].Text?.Trim();
            if (string.IsNullOrEmpty(currency)) continue;

            var date = ParseDate(sheet.Cells[row, columns[config.DateColumn]]);
            if (date is null) continue;

            var amount = ParseAmount(sheet.Cells[row, columns[config.AmountColumn]]);

            if (!config.CurrencyToMarket.TryGetValue(currency, out var marketCode))
            {
                // USD 등 미분리 통화: UnmatchedRowCount로 집계하고 상세도 보존
                unmatched++;
                unmatchedSample ??= currency;
                usdRawEntries.Add(new ExportSummaryRawEntry(date.Value.ToString("yyyy-MM-dd"), currency, amount));
                continue;
            }

            entries.Add(new ExportSummaryEntry("A", marketCode, ToYearMonth(date.Value), amount, Indicator: "신고액", Currency: currency));

            // USD_UNMATCHED로 매핑된 통화(CurrencyToMarket에 USD→USD_UNMATCHED 등록 시)는
            // 정상 집계에 포함하면서 상세도 함께 보존한다.
            if (marketCode == "USD_UNMATCHED")
                usdRawEntries.Add(new ExportSummaryRawEntry(date.Value.ToString("yyyy-MM-dd"), currency, amount));

        }

        return new LoadResult(entries, unmatched, unmatchedSample, usdRawEntries.Count > 0 ? usdRawEntries : null);
    }

    /// <summary>트랙B: 마켓 하나에 해당하는 판매(주문) 파일 한 개.</summary>
    /// <param name="salesCurrency">해당 마켓의 판매통화(ExportSummaryMarket.Currency). tidy 출력에서 통화 열에 사용.</param>
    public static LoadResult LoadSalesFile(string filePath, SalesMarketMapping mapping, string salesCurrency = "")
    {
        using var package = ExcelFileOpener.Open(filePath);
        var sheet = RequireFirstSheet(package);

        var columns = ResolveColumns(sheet, mapping.HeaderRow, mapping.DateColumn, mapping.AmountColumn);
        RequireColumns(columns, mapping.DateColumn, mapping.AmountColumn);

        var entries = new List<ExportSummaryEntry>();
        var lastRow = sheet.Dimension?.End.Row ?? mapping.HeaderRow;

        for (var row = mapping.HeaderRow + 1; row <= lastRow; row++)
        {
            var date = ParseDate(sheet.Cells[row, columns[mapping.DateColumn]]);
            if (date is null) continue;

            var amount = ParseAmount(sheet.Cells[row, columns[mapping.AmountColumn]]);
            entries.Add(new ExportSummaryEntry("B", mapping.MarketCode, ToYearMonth(date.Value), amount, Indicator: "매출액", Currency: salesCurrency));
        }

        return new LoadResult(entries, UnmatchedRowCount: 0, UnmatchedSample: null);
    }

    /// <summary>트랙C: 송금(Payoneer 등) 내역 한 파일. Description 문구로 마켓을 나누고,
    /// ExcludeContains에 해당하는 행(내부이체 등)은 아예 건너뛴다.</summary>
    public static LoadResult LoadRemittanceFile(string filePath, RemittanceTrackConfig config)
    {
        using var package = ExcelFileOpener.Open(filePath);
        var sheet = RequireFirstSheet(package);

        var columns = ResolveColumns(sheet, config.HeaderRow, config.DateColumn, config.AmountColumn, config.DescriptionColumn);
        RequireColumns(columns, config.DateColumn, config.AmountColumn, config.DescriptionColumn);

        var entries = new List<ExportSummaryEntry>();
        var unmatched = 0;
        string? unmatchedSample = null;
        var lastRow = sheet.Dimension?.End.Row ?? config.HeaderRow;

        for (var row = config.HeaderRow + 1; row <= lastRow; row++)
        {
            var description = sheet.Cells[row, columns[config.DescriptionColumn]].Text?.Trim();
            if (string.IsNullOrEmpty(description)) continue;

            if (config.ExcludeContains.Any(x => description.Contains(x, StringComparison.OrdinalIgnoreCase)))
                continue;

            var marketCode = config.Rules.FirstOrDefault(rule =>
                rule.Contains.All(c => description.Contains(c, StringComparison.OrdinalIgnoreCase)))?.MarketCode;

            if (marketCode is null)
            {
                unmatched++;
                unmatchedSample ??= description;
                continue;
            }

            var date = ParseDate(sheet.Cells[row, columns[config.DateColumn]]);
            if (date is null) continue;

            var amount = ParseAmount(sheet.Cells[row, columns[config.AmountColumn]]);
            entries.Add(new ExportSummaryEntry("C", marketCode, ToYearMonth(date.Value), amount, Indicator: "실익액", Currency: "USD"));
        }

        return new LoadResult(entries, unmatched, unmatchedSample);
    }

    private static ExcelWorksheet RequireFirstSheet(ExcelPackage package)
    {
        return package.Workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("엑셀 파일에서 시트를 찾을 수 없습니다.");
    }

    private static Dictionary<string, int> ResolveColumns(ExcelWorksheet sheet, int headerRow, params string[] headers)
    {
        var map = new Dictionary<string, int>();
        var lastCol = sheet.Dimension?.End.Column ?? 0;

        for (var col = 1; col <= lastCol; col++)
        {
            var text = sheet.Cells[headerRow, col].Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            foreach (var header in headers)
            {
                if (!map.ContainsKey(header) && string.Equals(text, header, StringComparison.OrdinalIgnoreCase))
                    map[header] = col;
            }
        }

        return map;
    }

    private static void RequireColumns(Dictionary<string, int> columns, params string[] headers)
    {
        var missing = headers.Where(h => !columns.ContainsKey(h)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"헤더 행에서 다음 컬럼을 찾지 못했습니다: {string.Join(", ", missing)}\n" +
                "export_summary_config.json의 컬럼명이 실제 파일 헤더와 일치하는지 확인하세요.");
        }
    }

    private static DateTime? ParseDate(ExcelRange cell)
    {
        if (cell.Value is DateTime dt) return dt;
        if (cell.Value is double serial && serial > 0)
        {
            try { return DateTime.FromOADate(serial); }
            catch (ArgumentException) { /* 날짜로 변환 불가한 숫자값 - 아래 문자열 파싱으로 계속 시도 */ }
        }

        var text = cell.Text?.Trim();
        if (!string.IsNullOrEmpty(text) && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        return null;
    }

    private static decimal ParseAmount(ExcelRange cell)
    {
        switch (cell.Value)
        {
            case double d: return (decimal)d;
            case decimal dec: return dec;
            case int i: return i;
        }

        var text = cell.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return 0m;

        var cleaned = new string(text.Where(c => char.IsDigit(c) || c is '.' or '-').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0m;
    }

    private static string ToYearMonth(DateTime date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}

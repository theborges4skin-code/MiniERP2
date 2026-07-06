using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 광고비 파일(채널별 광고 리포트)을 읽어 광고비 항목 목록으로 변환합니다. 발주서 OrderLoader와
/// 같은 구조지만 AdFieldMappings/AdStdField를 사용합니다. xlsx와 csv 모두 지원합니다.
/// </summary>
public class AdSpendLoader
{
    public bool LastLoadHeaderRowLooksEmpty { get; private set; }

    public async Task<List<AdSpendItem>> LoadFromFileAsync(AdMappingEngine engine, ChannelConfig channelConfig, string filePath, string? password = null)
    {
        LastLoadHeaderRowLooksEmpty = false;

        if (string.Equals(Path.GetExtension(filePath), ".csv", StringComparison.OrdinalIgnoreCase))
            return await LoadFromCsvAsync(engine, channelConfig, filePath);

        return await LoadFromExcelAsync(engine, channelConfig, filePath, password);
    }

    private async Task<List<AdSpendItem>> LoadFromExcelAsync(AdMappingEngine engine, ChannelConfig channelConfig, string filePath, string? password)
    {
        var items = new List<AdSpendItem>();

        await Task.Run(() =>
        {
            using var package = ExcelFileOpener.Open(filePath, password);

            var firstValidMapping = channelConfig.AdFieldMappings.Values.FirstOrDefault(m => !string.IsNullOrEmpty(m.Column));
            if (firstValidMapping == null)
                throw new InvalidOperationException($"채널 '{channelConfig.ChannelName}'에 유효한 광고비 필드 매핑 설정이 없습니다.");

            var sheetName = firstValidMapping.SheetName;
            var headerRow = firstValidMapping.HeaderRow;

            var worksheet = !string.IsNullOrEmpty(sheetName)
                ? package.Workbook.Worksheets[sheetName]
                : package.Workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
                throw new FileNotFoundException($"엑셀 파일에서 '{sheetName ?? "첫 번째"}' 시트를 찾을 수 없습니다.");

            var headerToIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[headerRow, col].Value?.ToString();
                if (!string.IsNullOrEmpty(header) && !headerToIndexMap.ContainsKey(header))
                    headerToIndexMap[header] = col;
            }

            var (stdMap, fixedValues) = BuildStdMaps(channelConfig, headerToIndexMap);
            LastLoadHeaderRowLooksEmpty = stdMap.Count == 0 && fixedValues.Count == 0;

            for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
            {
                var item = new AdSpendItem
                {
                    ChannelCode = channelConfig.ChannelCode,
                    ProductName = NE(GetValue(worksheet, row, stdMap, fixedValues, AdStdField.ProductName)),
                    ProductId   = NE(GetValue(worksheet, row, stdMap, fixedValues, AdStdField.ProductId)),
                    OptionName  = NE(GetValue(worksheet, row, stdMap, fixedValues, AdStdField.OptionName)),
                    Cost        = ParseCost(GetValue(worksheet, row, stdMap, fixedValues, AdStdField.Cost)),
                    Extra1      = NE(GetValue(worksheet, row, stdMap, fixedValues, AdStdField.Extra1)),
                    Extra2      = NE(GetValue(worksheet, row, stdMap, fixedValues, AdStdField.Extra2)),
                    Note1       = NE(GetValue(worksheet, row, stdMap, fixedValues, AdStdField.Note1)),
                    Note2       = NE(GetValue(worksheet, row, stdMap, fixedValues, AdStdField.Note2)),
                    Note3       = NE(GetValue(worksheet, row, stdMap, fixedValues, AdStdField.Note3)),
                    RawValues   = headerToIndexMap.ToDictionary(
                        kv => kv.Key,
                        kv => worksheet.Cells[row, kv.Value].Value?.ToString() ?? string.Empty),
                };

                if (string.IsNullOrWhiteSpace(item.ProductName) && string.IsNullOrWhiteSpace(item.ProductId)) continue;

                engine.ApplyMapping(item);
                items.Add(item);
            }
        });

        return items;
    }

    private async Task<List<AdSpendItem>> LoadFromCsvAsync(AdMappingEngine engine, ChannelConfig channelConfig, string filePath)
    {
        var items = new List<AdSpendItem>();

        await Task.Run(() =>
        {
            var allRows = ReadCsvRows(filePath);
            if (allRows.Count == 0) return;

            var firstValidMapping = channelConfig.AdFieldMappings.Values.FirstOrDefault(m => !string.IsNullOrEmpty(m.Column));
            var headerRowIndex = (firstValidMapping?.HeaderRow ?? 1) - 1; // 0-based

            if (headerRowIndex >= allRows.Count) return;

            var headers = allRows[headerRowIndex];
            var headerToIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
            {
                if (!string.IsNullOrEmpty(headers[i]) && !headerToIndexMap.ContainsKey(headers[i]))
                    headerToIndexMap[headers[i]] = i;
            }

            var (stdMap, fixedValues) = BuildStdMaps(channelConfig, headerToIndexMap);
            LastLoadHeaderRowLooksEmpty = stdMap.Count == 0 && fixedValues.Count == 0;

            for (int rowIdx = headerRowIndex + 1; rowIdx < allRows.Count; rowIdx++)
            {
                var cols = allRows[rowIdx];

                string? CsvGet(AdStdField f)
                {
                    if (fixedValues.TryGetValue(f, out var fv)) return fv;
                    return stdMap.TryGetValue(f, out var colI) && colI < cols.Length ? cols[colI] : null;
                }

                var item = new AdSpendItem
                {
                    ChannelCode = channelConfig.ChannelCode,
                    ProductName = NE(CsvGet(AdStdField.ProductName)),
                    ProductId   = NE(CsvGet(AdStdField.ProductId)),
                    OptionName  = NE(CsvGet(AdStdField.OptionName)),
                    Cost        = ParseCost(CsvGet(AdStdField.Cost)),
                    Extra1      = NE(CsvGet(AdStdField.Extra1)),
                    Extra2      = NE(CsvGet(AdStdField.Extra2)),
                    Note1       = NE(CsvGet(AdStdField.Note1)),
                    Note2       = NE(CsvGet(AdStdField.Note2)),
                    Note3       = NE(CsvGet(AdStdField.Note3)),
                    RawValues   = headerToIndexMap.ToDictionary(
                        kv => kv.Key,
                        kv => kv.Value < cols.Length ? cols[kv.Value] : string.Empty),
                };

                if (string.IsNullOrWhiteSpace(item.ProductName) && string.IsNullOrWhiteSpace(item.ProductId)) continue;

                engine.ApplyMapping(item);
                items.Add(item);
            }
        });

        return items;
    }

    // ── 공통 헬퍼 ──────────────────────────────────────────────────

    private static (Dictionary<AdStdField, int> stdMap, Dictionary<AdStdField, string> fixedValues)
        BuildStdMaps(ChannelConfig channelConfig, Dictionary<string, int> headerToIndexMap)
    {
        var stdMap = new Dictionary<AdStdField, int>();
        var fixedValues = new Dictionary<AdStdField, string>();

        foreach (var (stdField, mapping) in channelConfig.AdFieldMappings)
        {
            if (!string.IsNullOrEmpty(mapping.FixedValue))
                fixedValues[stdField] = mapping.FixedValue;
            else if (!string.IsNullOrEmpty(mapping.Column) && headerToIndexMap.TryGetValue(mapping.Column, out var idx))
                stdMap[stdField] = idx;
        }

        return (stdMap, fixedValues);
    }

    private static List<string[]> ReadCsvRows(string filePath)
    {
        var rows = new List<string[]>();
        System.Text.Encoding enc = new System.Text.UTF8Encoding(true);

        // Try UTF-8; if it contains replacement characters, fall back to EUC-KR (CP949)
        var lines = File.ReadAllLines(filePath, enc);
        if (lines.Length > 0 && lines[0].Contains('�'))
        {
            try { lines = File.ReadAllLines(filePath, System.Text.Encoding.GetEncoding(949)); }
            catch { /* keep UTF-8 result */ }
        }

        foreach (var line in lines)
            rows.Add(ParseCsvLine(line));

        return rows;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                i++;
                var sb = new System.Text.StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; }
                    else if (line[i] == '"') { i++; break; }
                    else { sb.Append(line[i++]); }
                }
                fields.Add(sb.ToString());
                if (i < line.Length && line[i] == ',') i++;
            }
            else
            {
                int start = i;
                while (i < line.Length && line[i] != ',') i++;
                fields.Add(line[start..i]);
                if (i < line.Length) i++;
            }
        }
        return [..fields];
    }

    private static string? NE(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static decimal ParseCost(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0m;
        var cleaned = text.Trim()
            .Replace("−", "-").Replace("(", "-").Replace(")", "")
            .Replace(",", "").Replace("원", "");
        return decimal.TryParse(cleaned, out var value) ? value : 0m;
    }

    private static string? GetValue(ExcelWorksheet worksheet, int row,
        Dictionary<AdStdField, int> map, Dictionary<AdStdField, string> fixedValues, AdStdField field)
    {
        if (fixedValues.TryGetValue(field, out var fv)) return fv;
        return map.TryGetValue(field, out var colIndex)
            ? worksheet.Cells[row, colIndex].Value?.ToString()
            : null;
    }
}

using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 광고비 파일(채널별 광고 리포트)을 읽어 광고비 항목 목록으로 변환합니다. 발주서 OrderLoader와
/// 같은 구조지만 AdFileLayout/AdStdField를 사용합니다. xlsx와 csv 모두 지원합니다.
/// </summary>
public class AdSpendLoader
{
    public bool LastLoadHeaderRowLooksEmpty { get; private set; }

    // ── 레이아웃 자동탐지 ──────────────────────────────────────────

    /// <summary>
    /// 파일의 1~50행을 스캔해 각 레이아웃의 MatchColumns를 모두 포함하는 행이 있는지 확인합니다.
    /// MatchColumns가 비어있는 레이아웃은 대상에서 제외합니다(수동 선택만 가능).
    /// </summary>
    public List<AdFileLayout> DetectLayout(string filePath, IReadOnlyList<AdFileLayout> layouts, string? password = null)
    {
        if (layouts.Count == 0) return [];
        var candidates = layouts.Where(l => l.MatchColumns.Count > 0).ToList();
        if (candidates.Count == 0) return [];

        return string.Equals(Path.GetExtension(filePath), ".csv", StringComparison.OrdinalIgnoreCase)
            ? DetectLayoutCsv(filePath, candidates)
            : DetectLayoutExcel(filePath, candidates, password);
    }

    private static List<AdFileLayout> DetectLayoutExcel(string filePath, List<AdFileLayout> candidates, string? password)
    {
        var matched = new List<AdFileLayout>();
        try
        {
            using var package = ExcelFileOpener.Open(filePath, password);
            var ws = package.Workbook.Worksheets.FirstOrDefault();
            if (ws?.Dimension == null) return matched;

            int maxRow = Math.Min(ws.Dimension.End.Row, 50);
            int maxCol = ws.Dimension.End.Column;

            for (int row = 1; row <= maxRow; row++)
            {
                var cellValues = Enumerable.Range(1, maxCol)
                    .Select(c => ws.Cells[row, c].Value?.ToString() ?? string.Empty)
                    .Where(v => v.Length > 0)
                    .ToList();

                foreach (var layout in candidates)
                {
                    if (!matched.Contains(layout) &&
                        layout.MatchColumns.All(mc => cellValues.Any(v => v.Contains(mc, StringComparison.OrdinalIgnoreCase))))
                    {
                        matched.Add(layout);
                    }
                }
            }
        }
        catch { /* 탐지 실패 시 빈 목록 반환 */ }
        return matched;
    }

    private static List<AdFileLayout> DetectLayoutCsv(string filePath, List<AdFileLayout> candidates)
    {
        var matched = new List<AdFileLayout>();
        try
        {
            var allRows = ReadCsvRows(filePath);
            int maxRow = Math.Min(allRows.Count, 50);
            for (int r = 0; r < maxRow; r++)
            {
                var cellValues = allRows[r].Where(v => v.Length > 0).ToList();
                foreach (var layout in candidates)
                {
                    if (!matched.Contains(layout) &&
                        layout.MatchColumns.All(mc => cellValues.Any(v => v.Contains(mc, StringComparison.OrdinalIgnoreCase))))
                    {
                        matched.Add(layout);
                    }
                }
            }
        }
        catch { }
        return matched;
    }

    // ── 파일 로드 ─────────────────────────────────────────────────

    public async Task<List<AdSpendItem>> LoadFromFileAsync(
        AdMappingEngine engine, string channelCode, AdFileLayout layout, string filePath, string? password = null)
    {
        LastLoadHeaderRowLooksEmpty = false;

        if (string.Equals(Path.GetExtension(filePath), ".csv", StringComparison.OrdinalIgnoreCase))
            return await LoadFromCsvAsync(engine, channelCode, layout, filePath);

        return await LoadFromExcelAsync(engine, channelCode, layout, filePath, password);
    }

    private async Task<List<AdSpendItem>> LoadFromExcelAsync(
        AdMappingEngine engine, string channelCode, AdFileLayout layout, string filePath, string? password)
    {
        var items = new List<AdSpendItem>();

        await Task.Run(() =>
        {
            using var package = ExcelFileOpener.Open(filePath, password);

            var firstValidMapping = layout.FieldMappings.Values.FirstOrDefault(m => !string.IsNullOrEmpty(m.Column));
            if (firstValidMapping == null)
                throw new InvalidOperationException($"레이아웃 '{layout.LayoutName}'에 유효한 광고비 필드 매핑 설정이 없습니다.");

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

            var (stdMap, fixedValues) = BuildStdMaps(layout, headerToIndexMap);
            LastLoadHeaderRowLooksEmpty = stdMap.Count == 0 && fixedValues.Count == 0;

            for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
            {
                var item = new AdSpendItem
                {
                    ChannelCode = channelCode,
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
                if (IsTotalRow(item)) continue;
                // 광고비 0원 행(노출만 되고 과금 없음, "-" 표기 포함)은 분석 대상이 아니므로 불러오지 않는다.
                if (item.Cost == 0m) continue;

                engine.ApplyMapping(item);
                items.Add(item);
            }
        });

        return items;
    }

    private async Task<List<AdSpendItem>> LoadFromCsvAsync(
        AdMappingEngine engine, string channelCode, AdFileLayout layout, string filePath)
    {
        var items = new List<AdSpendItem>();

        await Task.Run(() =>
        {
            var allRows = ReadCsvRows(filePath);
            if (allRows.Count == 0) return;

            var firstValidMapping = layout.FieldMappings.Values.FirstOrDefault(m => !string.IsNullOrEmpty(m.Column));
            var headerRowIndex = (firstValidMapping?.HeaderRow ?? 1) - 1; // 0-based

            if (headerRowIndex >= allRows.Count) return;

            var headers = allRows[headerRowIndex];
            var headerToIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
            {
                if (!string.IsNullOrEmpty(headers[i]) && !headerToIndexMap.ContainsKey(headers[i]))
                    headerToIndexMap[headers[i]] = i;
            }

            var (stdMap, fixedValues) = BuildStdMaps(layout, headerToIndexMap);
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
                    ChannelCode = channelCode,
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
                if (IsTotalRow(item)) continue;
                // 광고비 0원 행(노출만 되고 과금 없음, "-" 표기 포함)은 분석 대상이 아니므로 불러오지 않는다.
                if (item.Cost == 0m) continue;

                engine.ApplyMapping(item);
                items.Add(item);
            }
        });

        return items;
    }

    // ── 공통 헬퍼 ──────────────────────────────────────────────────

    private static (Dictionary<AdStdField, int> stdMap, Dictionary<AdStdField, string> fixedValues)
        BuildStdMaps(AdFileLayout layout, Dictionary<string, int> headerToIndexMap)
    {
        var stdMap = new Dictionary<AdStdField, int>();
        var fixedValues = new Dictionary<AdStdField, string>();

        foreach (var (stdField, mapping) in layout.FieldMappings)
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
        var lines = ReadCsvText(filePath).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        var delimiter = DetectDelimiter(lines);
        return lines.Select(line => ParseCsvLine(line, delimiter)).ToList();
    }

    /// <summary>
    /// BOM(UTF-8/UTF-16)이 있으면 그대로 따르고, 없으면 UTF-8로 엄격하게 읽어보고 깨지면 CP949로
    /// 읽는다. 11번가 광고 리포트는 "CSV"지만 실제로는 UTF-16 탭 구분 파일이고, 스마트스토어는
    /// UTF-8(BOM), 엑셀에서 저장한 CSV는 CP949다.
    /// </summary>
    private static string ReadCsvText(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (System.Text.DecoderFallbackException)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(949).GetString(bytes);
        }
    }

    /// <summary>앞쪽 몇 줄에서 탭이 쉼표보다 많으면 탭 구분 파일로 본다.</summary>
    private static char DetectDelimiter(List<string> lines)
    {
        var sample = lines.Where(l => l.Length > 0).Take(5).ToList();
        var tabs = sample.Sum(l => l.Count(c => c == '\t'));
        var commas = sample.Sum(l => l.Count(c => c == ','));
        return tabs > commas ? '\t' : ',';
    }

    /// <summary>
    /// 리포트 상단/하단의 합계 행(예: 11번가 "합계")은 상품 행의 합과 같은 금액이라, 그대로 읽으면
    /// 광고비가 두 번 잡힌다.
    /// </summary>
    private static readonly HashSet<string> TotalRowLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "합계", "총합계", "총계", "전체", "Total", "Grand Total",
    };

    private static bool IsTotalRow(AdSpendItem item) =>
        (item.ProductId != null && TotalRowLabels.Contains(item.ProductId.Trim())) ||
        (item.ProductName != null && TotalRowLabels.Contains(item.ProductName.Trim()));

    private static string[] ParseCsvLine(string line, char delimiter)
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
                if (i < line.Length && line[i] == delimiter) i++;
            }
            else
            {
                int start = i;
                while (i < line.Length && line[i] != delimiter) i++;
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

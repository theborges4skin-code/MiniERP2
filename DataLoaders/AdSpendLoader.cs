using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 광고비 파일(채널별 광고 리포트)을 읽어 광고비 항목 목록으로 변환합니다. 발주서 OrderLoader와
/// 같은 구조지만 AdFieldMappings/AdStdField를 사용합니다. 채널마다 리포트 헤더 구성이 매우
/// 다양해(다중 시트, 헤더명 변경 등) 처음엔 단일 시트/단일 헤더만 지원하고, 실제 파일로 테스트하며
/// 채널설정에서 시트/헤더행/열을 맞춰가는 것을 전제로 한다(누락된 필드는 빈 값으로 둠).
/// </summary>
public class AdSpendLoader
{
    public bool LastLoadHeaderRowLooksEmpty { get; private set; }

    public async Task<List<AdSpendItem>> LoadFromFileAsync(AdMappingEngine engine, ChannelConfig channelConfig, string filePath, string? password = null)
    {
        LastLoadHeaderRowLooksEmpty = false;
        var items = new List<AdSpendItem>();

        await Task.Run(() =>
        {
            using var package = ExcelFileOpener.Open(filePath, password);

            var firstValidMapping = channelConfig.AdFieldMappings.Values.FirstOrDefault(m => !string.IsNullOrEmpty(m.Column));
            if (firstValidMapping == null)
            {
                throw new InvalidOperationException($"채널 '{channelConfig.ChannelName}'에 유효한 광고비 필드 매핑 설정이 없습니다.");
            }

            var sheetName = firstValidMapping.SheetName;
            var headerRow = firstValidMapping.HeaderRow;

            var worksheet = !string.IsNullOrEmpty(sheetName)
                ? package.Workbook.Worksheets[sheetName]
                : package.Workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
            {
                throw new FileNotFoundException($"엑셀 파일에서 '{sheetName ?? "첫 번째"}' 시트를 찾을 수 없습니다.");
            }

            var headerToIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[headerRow, col].Value?.ToString();
                if (!string.IsNullOrEmpty(header) && !headerToIndexMap.ContainsKey(header))
                {
                    headerToIndexMap[header] = col;
                }
            }

            var stdFieldToIndexMap = new Dictionary<AdStdField, int>();
            var fixedValues = new Dictionary<AdStdField, string>();
            foreach (var (stdField, mapping) in channelConfig.AdFieldMappings)
            {
                if (!string.IsNullOrEmpty(mapping.FixedValue))
                {
                    fixedValues[stdField] = mapping.FixedValue;
                }
                else if (!string.IsNullOrEmpty(mapping.Column) && headerToIndexMap.TryGetValue(mapping.Column, out var index))
                {
                    stdFieldToIndexMap[stdField] = index;
                }
            }

            LastLoadHeaderRowLooksEmpty = stdFieldToIndexMap.Count == 0 && fixedValues.Count == 0;

            for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
            {
                var costText = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, AdStdField.Cost);
                var item = new AdSpendItem
                {
                    ChannelCode = channelConfig.ChannelCode,
                    ProductName = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, AdStdField.ProductName),
                    ProductId = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, AdStdField.ProductId),
                    OptionName = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, AdStdField.OptionName),
                    Cost = ParseCost(costText),
                    Extra1 = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, AdStdField.Extra1),
                    Extra2 = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, AdStdField.Extra2),
                };

                if (string.IsNullOrWhiteSpace(item.ProductName) && string.IsNullOrWhiteSpace(item.ProductId)) continue;

                engine.ApplyMapping(item);
                items.Add(item);
            }
        });

        return items;
    }

    /// <summary>쉼표/통화 표기/공백/음수괄호 등을 정리해 숫자로 변환합니다(레거시 ad_engine.py와 동일한 전처리).</summary>
    private static decimal ParseCost(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0m;

        var cleaned = text.Trim()
            .Replace("−", "-")
            .Replace("(", "-")
            .Replace(")", "")
            .Replace(",", "")
            .Replace("원", "");

        return decimal.TryParse(cleaned, out var value) ? value : 0m;
    }

    private static string? GetValue(ExcelWorksheet worksheet, int row, Dictionary<AdStdField, int> map, Dictionary<AdStdField, string> fixedValues, AdStdField field)
    {
        if (fixedValues.TryGetValue(field, out var fixedValue)) return fixedValue;

        return map.TryGetValue(field, out var colIndex)
            ? worksheet.Cells[row, colIndex].Value?.ToString()
            : null;
    }
}

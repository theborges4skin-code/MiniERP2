using MiniERP2.Models;
using MiniERP2.Mapping;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 발주 파일(엑셀)을 읽어 주문 목록으로 변환하는 기능을 제공합니다.
/// </summary>
public class OrderLoader
{
    /// <summary>
    /// 직전 LoadFromFileAsync 호출에서, 설정된 헤더 행에서 매핑된 표준 필드를 하나도 찾지 못했는지 여부입니다.
    /// 헤더 행이 비어있거나 셀이 병합되어 있을 때(설정 오류) 주로 발생하며, 호출 측에서 경고를 띄우는 데 사용합니다.
    /// </summary>
    public bool LastLoadHeaderRowLooksEmpty { get; private set; }

    /// <summary>
    /// 지정된 엑셀 파일 경로에서 주문 데이터를 비동기적으로 로드합니다.
    /// </summary>
    /// <param name="skuMapper">SKU 매핑을 수행할 SkuMapper 인스턴스</param>
    /// <param name="channelConfig">데이터를 해석할 채널 설정</param>
    /// <param name="filePath">엑셀 파일 경로</param>
    /// <param name="password">암호로 보호된 파일일 경우의 비밀번호. 모르면 생략하고, 실패 시 EncryptedExcelFileException을 받아 재시도하세요.</param>
    /// <returns>로드된 주문 항목의 리스트</returns>
    public async Task<List<OfsOrderItem>> LoadFromFileAsync(SkuMapper skuMapper, ChannelConfig channelConfig, string filePath, string? password = null)
    {
        LastLoadHeaderRowLooksEmpty = false;
        var items = new List<OfsOrderItem>();

        await Task.Run(() =>
        {
            using var package = ExcelFileOpener.Open(filePath, password);

            // 채널 설정에서 주로 사용할 시트와 헤더 행을 결정합니다.
            // 여기서는 첫 번째 유효한 매핑 설정을 기준으로 합니다.
            var firstValidMapping = channelConfig.OrderFieldMappings.Values.FirstOrDefault(m => !string.IsNullOrEmpty(m.Column));
            if (firstValidMapping == null)
            {
                throw new InvalidOperationException($"채널 '{channelConfig.ChannelName}'에 유효한 필드 매핑 설정이 없습니다.");
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

            // 헤더 이름을 키로, 열 인덱스를 값으로 하는 맵을 생성합니다.
            var headerToIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[headerRow, col].Value?.ToString();
                if (!string.IsNullOrEmpty(header) && !headerToIndexMap.ContainsKey(header))
                {
                    headerToIndexMap[header] = col;
                }
            }

            // 표준 필드를 키로, 열 인덱스를 값으로 하는 맵을 생성합니다.
            // 고정값이 설정된 필드는 엑셀에서 읽지 않고 항상 그 값을 사용합니다(예: 고정거래처의 수취인/주소).
            var stdFieldToIndexMap = new Dictionary<StdField, int>();
            var fixedValues = new Dictionary<StdField, string>();
            foreach (var (stdField, mapping) in channelConfig.OrderFieldMappings)
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

            // 설정된 매핑이 하나도 헤더 행과 맞지 않으면(헤더 행이 비어있거나 셀이 병합된 경우 등)
            // 이후 모든 값이 공란으로 나오게 되므로, 호출 측이 경고할 수 있도록 표시해둔다.
            LastLoadHeaderRowLooksEmpty = stdFieldToIndexMap.Count == 0 && fixedValues.Count == 0;

            // 데이터 행을 순회하며 OfsOrderItem 객체를 생성합니다.
            for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
            {
                var orderItem = new OfsOrderItem
                {
                    // 각 속성에 대해 매핑된 열 인덱스를 사용하여 값을 가져옵니다.
                    ChannelCode = channelConfig.ChannelCode,
                    OrderNo = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.ProductNo),
                    ProductName = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.ProductName),
                    OptionName = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.OptionName),
                    Quantity = int.TryParse(GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.Quantity), out var qty) ? qty : 0,
                    Recipient = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.Recipient),
                    Phone = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.Phone),
                    Address = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.Address),
                    DeliveryMessage = GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.DeliveryMessage),
                    Status = "로드 완료"
                };

                // SKU 자동 매핑 적용
                skuMapper.ApplyMapping(orderItem);
                items.Add(orderItem);
            }
        });

        return items;
    }

    private string? GetValue(ExcelWorksheet worksheet, int row, Dictionary<StdField, int> map, Dictionary<StdField, string> fixedValues, StdField field)
    {
        if (fixedValues.TryGetValue(field, out var fixedValue)) return fixedValue;

        return map.TryGetValue(field, out var colIndex)
            ? worksheet.Cells[row, colIndex].Value?.ToString()
            : null;
    }
}
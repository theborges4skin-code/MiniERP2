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
                    OrderDate = GetDateValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.OrderDate),
                    Status = "로드 완료"
                };

                // 매핑된 필드 값이 전부 공란인 행(엑셀의 빈 줄, 서식만 있는 꼬리 행 등)은 건너뛴다.
                if (IsBlankRow(orderItem)) continue;

                // SKU 자동 매핑 적용
                skuMapper.ApplyMapping(orderItem);
                items.Add(orderItem);
            }
        });

        return items;
    }

    /// <summary>
    /// 매핑된 모든 필드 값이 공란인 행인지 판단한다(엑셀의 빈 줄, 서식만 있는 꼬리 행 등을
    /// 걸러내기 위함). 고정값(예: 고정거래처의 수취인)이 설정된 필드는 항상 채워져 있으므로,
    /// 그런 채널은 이 검사로 행이 걸러지지 않는다(의도된 동작 — 실제로 빈 행이 아님). 수량은
    /// 빈칸이어도 0으로 기본값이 들어가 정보가 없다는 신호가 아니라서 판단에서 제외한다.
    /// </summary>
    public static bool IsBlankRow(OfsOrderItem item) =>
        string.IsNullOrWhiteSpace(item.OrderNo)
        && string.IsNullOrWhiteSpace(item.ProductName)
        && string.IsNullOrWhiteSpace(item.OptionName)
        && string.IsNullOrWhiteSpace(item.Recipient)
        && string.IsNullOrWhiteSpace(item.Phone)
        && string.IsNullOrWhiteSpace(item.Address)
        && string.IsNullOrWhiteSpace(item.DeliveryMessage)
        && item.OrderDate is null;

    private string? GetValue(ExcelWorksheet worksheet, int row, Dictionary<StdField, int> map, Dictionary<StdField, string> fixedValues, StdField field)
    {
        if (fixedValues.TryGetValue(field, out var fixedValue)) return fixedValue;

        return map.TryGetValue(field, out var colIndex)
            ? worksheet.Cells[row, colIndex].Value?.ToString()
            : null;
    }

    /// <summary>
    /// 발주일처럼 날짜로 다뤄야 하는 필드를 읽는다. 엑셀에서 날짜 서식 셀은 EPPlus가 내부적으로
    /// 일련번호(double)로 들고 있을 수 있어 GetValue&lt;DateTime?&gt;로 먼저 시도하고, 텍스트로
    /// 입력된 날짜("2026-06-27" 등)는 문자열 파싱으로 한 번 더 시도한다.
    /// </summary>
    private DateTime? GetDateValue(ExcelWorksheet worksheet, int row, Dictionary<StdField, int> map, Dictionary<StdField, string> fixedValues, StdField field)
    {
        if (fixedValues.TryGetValue(field, out var fixedValue))
        {
            return DateTime.TryParse(fixedValue, out var fixedDate) ? fixedDate : null;
        }

        if (!map.TryGetValue(field, out var colIndex)) return null;

        var cell = worksheet.Cells[row, colIndex];
        var dateValue = cell.GetValue<DateTime?>();
        if (dateValue.HasValue) return dateValue;

        return DateTime.TryParse(cell.Value?.ToString(), out var parsed) ? parsed : null;
    }
}
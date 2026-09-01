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

            // 빈 행 판단에 쓸 열 인덱스 목록: 수량은 공란이어도 0으로 기본값이 채워지므로 제외한다.
            // 고정값(FixedValue) 필드는 셀이 비어도 항상 채워지므로 여기서 제외해야
            // 고정 수취인/주소를 쓰는 채널에서도 꼬리 빈 행을 올바르게 걸러낼 수 있다.
            var blankCheckColIndices = stdFieldToIndexMap
                .Where(kv => kv.Key != StdField.Quantity)
                .Select(kv => kv.Value)
                .ToList();

            // 재로드해도 같은 줄을 같은 발주로 알아볼 수 있도록, 파일명+엑셀 행번호로 안정 식별자를
            // 만든다(ShipmentGrouping.GetEffectiveGroupId가 이력 저장 키를 만들 때 사용).
            var sourceFileName = Path.GetFileName(filePath);

            // 데이터 행을 순회하며 OfsOrderItem 객체를 생성합니다.
            for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
            {
                // 셀 기준 빈 행 체크: 고정값·수량을 제외한 모든 매핑 컬럼이 공란이면 건너뛴다.
                if (blankCheckColIndices.Count > 0 &&
                    blankCheckColIndices.All(ci =>
                        string.IsNullOrWhiteSpace(worksheet.Cells[row, ci].Value?.ToString())))
                    continue;

                // 매핑되어 있을 때만 읽는다(없으면 null 유지 → SkuMapper가 1:1 정확매핑을 4필드로
                // 확장할 때 가격 정보 없는 채널로 자연스럽게 처리됨. 매핑시스템 통합개편 기획서 §6.1).
                decimal? revenue = null;
                if (stdFieldToIndexMap.ContainsKey(StdField.Revenue) || fixedValues.ContainsKey(StdField.Revenue))
                {
                    revenue = decimal.TryParse(GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.Revenue), out var revenueValue) ? revenueValue : 0m;
                }

                var orderItem = new OfsOrderItem
                {
                    // 각 속성에 대해 매핑된 열 인덱스를 사용하여 값을 가져옵니다.
                    ChannelCode = channelConfig.ChannelCode,
                    SourceRowKey = $"{sourceFileName}#{row}",
                    OrderNo = GetTextValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.ProductNo),
                    ProductName = GetTextValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.ProductName),
                    OptionName = GetTextValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.OptionName),
                    Quantity = int.TryParse(GetValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.Quantity), out var qty) ? qty : 0,
                    Revenue = revenue,
                    Recipient = GetTextValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.Recipient),
                    Phone = GetTextValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.Phone),
                    Address = GetTextValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.Address),
                    TrackingNo = GetTextValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.TrackingNo),
                    CourierName = GetTextValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.CourierName),
                    DeliveryMessage = GetTextValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.DeliveryMessage),
                    Remark = GetTextValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.Remark),
                    ChannelHint = GetTextValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.ChannelHint),
                    OrderDate = GetDateValue(worksheet, row, stdFieldToIndexMap, fixedValues, StdField.OrderDate),
                    Status = "로드 완료"
                };

                // "샘플등"(§2 D3) 채널은 애초에 판매용 CSKU가 없을 수밖에 없으므로, 발주 파일로
                // 불러온 행도 ManualOrderDialog.PresetLineKindIfSampleChannel과 동일하게 "구분"을
                // 기본으로 "샘플"로 채워, SKU 매핑 없이도 발주확정 저장이 가능하도록 한다.
                if (channelConfig.ChannelCode == "SAMPLE") orderItem.LineKind = LineKinds.Sample;

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
        && string.IsNullOrWhiteSpace(item.Remark)
        && item.OrderDate is null;

    private string? GetValue(ExcelWorksheet worksheet, int row, Dictionary<StdField, int> map, Dictionary<StdField, string> fixedValues, StdField field)
    {
        if (fixedValues.TryGetValue(field, out var fixedValue)) return fixedValue;

        return map.TryGetValue(field, out var colIndex)
            ? worksheet.Cells[row, colIndex].Value?.ToString()
            : null;
    }

    /// <summary>
    /// 수령인/전화번호/주소 등 문자열 성격 필드 전용. 전화번호·우편번호처럼 숫자로 입력된 값은
    /// 셀 원본값(.Value)에서 앞자리 0이 사라지고 화면에 보이는 서식(.Text)에만 남는 경우가 흔해서
    /// (엑셀의 잘 알려진 함정), 셀 값이 이미 문자열이 아니면 화면에 보이는 형식(.Text) 그대로
    /// 읽는다 — 그래야 "누적발주서 송장번호 입력"의 수령인+전화번호+주소 완전일치 매칭이 화면에
    /// 보이는 값 기준으로 정확히 동작한다. 수량/매출액처럼 TryParse로 숫자 변환해야 하는 필드는
    /// 천단위 구분자·통화기호가 섞인 서식 텍스트를 피하기 위해 이 메서드를 쓰지 않고 GetValue를 쓴다.
    /// </summary>
    private string? GetTextValue(ExcelWorksheet worksheet, int row, Dictionary<StdField, int> map, Dictionary<StdField, string> fixedValues, StdField field)
    {
        if (fixedValues.TryGetValue(field, out var fixedValue)) return fixedValue;
        if (!map.TryGetValue(field, out var colIndex)) return null;

        var cell = worksheet.Cells[row, colIndex];
        return cell.Value is string s ? s : cell.Text;
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
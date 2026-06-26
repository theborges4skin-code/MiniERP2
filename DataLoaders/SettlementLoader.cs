using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 온라인 정산 파일(엑셀)을 읽어 채널별 손익 공식을 적용한 결과로 변환하는 기능을 제공합니다.
/// 기획서 5.6절 '이익분석(자동)'.
/// </summary>
public class SettlementLoader
{
    /// <summary>
    /// 지정된 엑셀 파일 경로에서 정산 데이터를 비동기적으로 로드하고 이익을 계산합니다.
    /// </summary>
    /// <param name="skuMapper">SKU 매핑을 수행할 SkuMapper 인스턴스</param>
    /// <param name="itemRepository">제조원가 조회를 위한 Repository</param>
    /// <param name="channelConfig">데이터를 해석할 채널 설정</param>
    /// <param name="filePath">엑셀 파일 경로</param>
    public async Task<List<SettlementData>> LoadFromFileAsync(SkuMapper skuMapper, ItemRepository itemRepository, ChannelConfig channelConfig, string filePath)
    {
        var rows = new List<SettlementData>();

        await Task.Run(() =>
        {
            ExcelLicense.Ensure();
            using var package = new ExcelPackage(new FileInfo(filePath));

            var firstValidMapping = channelConfig.FieldMappings.Values.FirstOrDefault(m => !string.IsNullOrEmpty(m.Column));
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

            var headerToIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[headerRow, col].Value?.ToString();
                if (!string.IsNullOrEmpty(header) && !headerToIndexMap.ContainsKey(header))
                {
                    headerToIndexMap[header] = col;
                }
            }

            var stdFieldToIndexMap = new Dictionary<StdField, int>();
            foreach (var (stdField, mapping) in channelConfig.FieldMappings)
            {
                if (!string.IsNullOrEmpty(mapping.Column) && headerToIndexMap.TryGetValue(mapping.Column, out var index))
                {
                    stdFieldToIndexMap[stdField] = index;
                }
            }

            for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
            {
                var productName = GetValue(worksheet, row, stdFieldToIndexMap, StdField.ProductName);
                var optionName = GetValue(worksheet, row, stdFieldToIndexMap, StdField.OptionName);
                var qty = int.TryParse(GetValue(worksheet, row, stdFieldToIndexMap, StdField.Quantity), out var qtyValue) ? qtyValue : 0;
                var settlement = decimal.TryParse(GetValue(worksheet, row, stdFieldToIndexMap, StdField.SettlementAmount), out var settlementValue) ? settlementValue : 0m;
                var shipping = decimal.TryParse(GetValue(worksheet, row, stdFieldToIndexMap, StdField.ShippingFee), out var shippingValue) ? shippingValue : 0m;
                var fee = decimal.TryParse(GetValue(worksheet, row, stdFieldToIndexMap, StdField.HandlingFee), out var feeValue) ? feeValue : 0m;

                if (string.IsNullOrWhiteSpace(productName) && string.IsNullOrWhiteSpace(optionName)) continue;

                var settlementData = new SettlementData
                {
                    ChannelCode = channelConfig.ChannelCode,
                    ProductName = productName,
                    OptionName = optionName,
                    Qty = qty,
                    Settlement = settlement,
                    Shipping = shipping,
                    Fee = fee,
                };

                ApplyMappingAndProfit(settlementData, skuMapper, itemRepository, channelConfig);
                rows.Add(settlementData);
            }

            // 기획서 5.6절 특수 규칙: 쿠팡일반은 배송비를 전체 합산하여 첫 행에만 표기
            ProfitCalculator.ApplyCoupangGeneralShippingAggregation(channelConfig.ChannelType, rows);
        });

        return rows;
    }

    private static void ApplyMappingAndProfit(SettlementData data, SkuMapper skuMapper, ItemRepository itemRepository, ChannelConfig channelConfig)
    {
        var orderItem = new OfsOrderItem
        {
            ProductName = data.ProductName,
            OptionName = data.OptionName,
        };
        skuMapper.ApplyMapping(orderItem);
        data.Msku = orderItem.MappedSku;
        data.Status = orderItem.Status;

        if (string.IsNullOrWhiteSpace(data.Msku))
        {
            data.Profit = 0m;
            return;
        }

        var item = itemRepository.GetBySku(data.Msku);
        if (item == null)
        {
            data.Status = "원가 정보 없음";
            data.Profit = 0m;
            return;
        }

        data.Profit = ProfitCalculator.Calculate(channelConfig.ChannelType, data.Settlement, item.CostPrice, data.Qty, data.Shipping, data.Fee, channelConfig.ExchangeRate);
    }

    private string? GetValue(ExcelWorksheet worksheet, int row, Dictionary<StdField, int> map, StdField field)
    {
        return map.TryGetValue(field, out var colIndex)
            ? worksheet.Cells[row, colIndex].Value?.ToString()
            : null;
    }
}

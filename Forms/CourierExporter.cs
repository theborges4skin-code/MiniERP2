using System.Text.Json;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Exporters;

/// <summary>
/// 주문 데이터를 특정 택배사 양식의 엑셀 파일로 내보내는 기능을 제공합니다.
/// </summary>
public class CourierExporter
{
    /// <summary>
    /// "품목"란에 매핑됐을 가능성이 있는 속성들. 이 속성에 한해서만, 같은 묶음(송장) 안의 모든
    /// 줄을 줄바꿈으로 이어붙인 문자열로 출력한다(나머지 속성은 묶음의 대표 줄 값을 그대로 쓴다).
    /// </summary>
    private static readonly HashSet<string> ItemDescriptionProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(OfsOrderItem.InvoiceLabel),
        nameof(OfsOrderItem.ProductName),
    };

    /// <summary>
    /// 주문 목록을 지정된 택배사 양식의 엑셀 파일로 비동기적으로 내보냅니다. 같은 묶음(송장 1건
    /// 단위, <see cref="ShipmentGrouping.GetEffectiveGroupId"/>)에 속한 줄들은 출력에서 한 행으로
    /// 합쳐진다(기본값은 주문번호 단위, OFS 그리드에서 분리배송/합포장을 지정하면 그 값을 따른다).
    /// </summary>
    /// <param name="orders">내보낼 주문 항목 목록입니다.</param>
    /// <param name="courier">택배사 마스터 정보입니다.</param>
    /// <param name="filePath">저장할 파일의 전체 경로입니다.</param>
    /// <param name="channelConfigsByCode">
    /// 채널코드별 ChannelConfig. 주문의 채널에 이 택배사/헤더에 대한 고정값 설정이 있으면
    /// 주문 데이터 대신 그 고정값을 출력합니다(예: 채널별 고정 도착지 코드). 생략 시 고정값을 적용하지 않습니다.
    /// </param>
    /// <returns>
    /// 품목이 4줄을 초과해 줄바꿈으로 다 표시되지 못할 수 있는 묶음들의 대표 주문번호 목록입니다.
    /// 비어있으면 모든 묶음이 4줄 이하입니다. 내보내기 자체는 초과 여부와 무관하게 항상 끝까지 진행됩니다.
    /// </returns>
    public async Task<List<string>> ExportAsync(IEnumerable<OfsOrderItem> orders, CourierMaster courier, string filePath, IReadOnlyDictionary<string, ChannelConfig>? channelConfigsByCode = null)
    {
        return await Task.Run(() =>
        {
            var headerMapping = JsonSerializer.Deserialize<Dictionary<string, string>>(courier.HeaderMappingJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                                ?? throw new InvalidOperationException("택배사 헤더 매핑 정보가 유효하지 않습니다.");

            var headers = headerMapping.Keys.ToList();
            var groups = orders.GroupBy(ShipmentGrouping.GetEffectiveGroupId).ToList();
            var overflowGroups = new List<string>();

            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Sheet1");

            // 1. 헤더 쓰기
            for (int i = 0; i < headers.Count; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }

            // 2. 데이터 쓰기 — 묶음(=송장) 단위로 한 행씩 출력한다.
            var row = 2;
            foreach (var group in groups)
            {
                var groupItems = group.ToList();
                var representative = groupItems[0];

                var combinedDescription = ShipmentGrouping.BuildCombinedItemDescription(groupItems);
                if (ShipmentGrouping.CountDescriptionLines(groupItems) > 4)
                {
                    overflowGroups.Add(representative.OrderNo ?? group.Key);
                }

                for (int col = 0; col < headers.Count; col++)
                {
                    var header = headers[col];

                    var fixedValue = GetFixedOverride(channelConfigsByCode, representative.ChannelCode, courier.CourierName, header);
                    if (fixedValue != null)
                    {
                        worksheet.Cells[row, col + 1].Value = fixedValue;
                        continue;
                    }

                    if (headerMapping.TryGetValue(header, out var propertyName))
                    {
                        if (ItemDescriptionProperties.Contains(propertyName))
                        {
                            worksheet.Cells[row, col + 1].Value = combinedDescription;
                            continue;
                        }

                        // 리플렉션을 사용하여 OfsOrderItem의 속성 값을 가져옵니다(묶음의 대표 줄 기준).
                        var property = typeof(OfsOrderItem).GetProperty(propertyName);
                        if (property != null)
                        {
                            worksheet.Cells[row, col + 1].Value = property.GetValue(representative);
                        }
                    }
                }
                row++;
            }

            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            package.SaveAs(new FileInfo(filePath));

            return overflowGroups;
        });
    }

    private static string? GetFixedOverride(IReadOnlyDictionary<string, ChannelConfig>? channelConfigsByCode, string? channelCode, string courierName, string header)
    {
        if (channelConfigsByCode == null || string.IsNullOrEmpty(channelCode)) return null;
        if (!channelConfigsByCode.TryGetValue(channelCode, out var config)) return null;

        return config.CourierHeaderOverrides
            .FirstOrDefault(o => o.CourierName == courierName && o.Header == header)
            ?.FixedValue;
    }
}
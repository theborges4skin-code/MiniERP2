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
    /// 주문 목록을 지정된 택배사 양식의 엑셀 파일로 비동기적으로 내보냅니다.
    /// </summary>
    /// <param name="orders">내보낼 주문 항목 목록입니다.</param>
    /// <param name="courier">택배사 마스터 정보입니다.</param>
    /// <param name="filePath">저장할 파일의 전체 경로입니다.</param>
    /// <param name="channelConfigsByCode">
    /// 채널코드별 ChannelConfig. 주문의 채널에 이 택배사/헤더에 대한 고정값 설정이 있으면
    /// 주문 데이터 대신 그 고정값을 출력합니다(예: 채널별 고정 도착지 코드). 생략 시 고정값을 적용하지 않습니다.
    /// </param>
    public async Task ExportAsync(IEnumerable<OfsOrderItem> orders, CourierMaster courier, string filePath, IReadOnlyDictionary<string, ChannelConfig>? channelConfigsByCode = null)
    {
        await Task.Run(() =>
        {
            var headerMapping = JsonSerializer.Deserialize<Dictionary<string, string>>(courier.HeaderMappingJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                                ?? throw new InvalidOperationException("택배사 헤더 매핑 정보가 유효하지 않습니다.");

            var headers = headerMapping.Keys.ToList();

            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Sheet1");

            // 1. 헤더 쓰기
            for (int i = 0; i < headers.Count; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }

            // 2. 데이터 쓰기
            var row = 2;
            foreach (var order in orders)
            {
                for (int col = 0; col < headers.Count; col++)
                {
                    var header = headers[col];

                    var fixedValue = GetFixedOverride(channelConfigsByCode, order.ChannelCode, courier.CourierName, header);
                    if (fixedValue != null)
                    {
                        worksheet.Cells[row, col + 1].Value = fixedValue;
                        continue;
                    }

                    if (headerMapping.TryGetValue(header, out var propertyName))
                    {
                        // 리플렉션을 사용하여 OfsOrderItem의 속성 값을 가져옵니다.
                        var property = typeof(OfsOrderItem).GetProperty(propertyName);
                        if (property != null)
                        {
                            worksheet.Cells[row, col + 1].Value = property.GetValue(order);
                        }
                    }
                }
                row++;
            }

            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            package.SaveAs(new FileInfo(filePath));
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
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Exporters;

/// <summary>
/// 주문 데이터를 특정 택배사 양식의 엑셀 파일로 내보내는 기능을 제공합니다.
/// </summary>
public class CourierExporter
{
    private readonly ChannelSkuRepository _channelSkuRepository;

    public CourierExporter(ChannelSkuRepository? channelSkuRepository = null)
    {
        _channelSkuRepository = channelSkuRepository ?? new ChannelSkuRepository();
    }

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
    /// <param name="appendCourierNameColumn">
    /// true면 택배사 고유 양식 컬럼 뒤에 "택배사" 열을 하나 더 붙여 courier.CourierName을 채운다.
    /// 택배사 프로그램에 그대로 업로드하는 파일(OfsForm의 최초 발주 출력)에서는 컬럼 구성이 어긋나면
    /// 택배사 쪽 인식이 깨질 수 있어 기본값 false로 두고, 이미 운송장이 확정된 뒤 거래처에 그대로
    /// 전달만 하는 용도(OutboundHistoryForm 재출력)에서만 true로 켠다.
    /// </param>
    public async Task<List<string>> ExportAsync(IEnumerable<OfsOrderItem> orders, CourierMaster courier, string filePath, IReadOnlyDictionary<string, ChannelConfig>? channelConfigsByCode = null, bool appendCourierNameColumn = false)
    {
        return await Task.Run(() =>
        {
            // 샘플 양식에서 불러온 순서 그대로 출력해야 택배사 프로그램이 파일을 인식할 수 있으므로,
            // 순서가 보장되는 목록(CourierHeaderMapping)을 그대로 순회한다(Dictionary 키 순서에
            // 의존하지 않음).
            var entries = CourierHeaderMapping.Parse(courier.HeaderMappingJson);
            if (entries.Count == 0)
            {
                throw new InvalidOperationException("택배사 헤더 매핑 정보가 유효하지 않습니다.");
            }

            var headers = entries.Select(en => en.Header).ToList();
            var groups = orders.GroupBy(ShipmentGrouping.GetEffectiveGroupId).ToList();
            var overflowGroups = new List<string>();
            // 채널코드+CSKU코드별 송장표시명 조회 결과를 캐싱한다(같은 CSKU가 여러 줄에 반복돼도
            // DB 조회를 한 번만 하면 됨).
            var invoiceDisplayNameCache = new Dictionary<(string ChannelCode, string CskuCode), string?>();

            ExcelLicense.Ensure();
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Sheet1");

            // 1. 헤더 쓰기
            for (int i = 0; i < headers.Count; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }
            if (appendCourierNameColumn)
            {
                worksheet.Cells[1, headers.Count + 1].Value = "택배사";
            }

            // 2. 데이터 쓰기 — 묶음(=송장) 단위로 한 행씩 출력한다.
            var row = 2;
            foreach (var group in groups)
            {
                var groupItems = group.ToList();
                var representative = groupItems[0];

                if (ShipmentGrouping.CountDescriptionLines(groupItems) > 4)
                {
                    overflowGroups.Add(representative.OrderNo ?? group.Key);
                }

                var channelConfig = channelConfigsByCode != null && !string.IsNullOrEmpty(representative.ChannelCode)
                    ? channelConfigsByCode.GetValueOrDefault(representative.ChannelCode)
                    : null;

                for (int col = 0; col < entries.Count; col++)
                {
                    // 미리보기(OfsForm)와 같은 로직(CourierFieldResolver)을 공유해, 미리본 내용과
                    // 실제 내보내기 결과가 항상 일치하게 한다.
                    var value = CourierFieldResolver.Resolve(
                        entries[col], groupItems, courier, channelConfig,
                        item => ResolveInvoiceDisplayName(item, invoiceDisplayNameCache));
                    worksheet.Cells[row, col + 1].Value = value;
                }
                if (appendCourierNameColumn)
                {
                    worksheet.Cells[row, entries.Count + 1].Value = courier.CourierName;
                }
                row++;
            }

            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            ExportHelper.SaveExcel(package, filePath);

            return overflowGroups;
        });
    }

    private string? ResolveInvoiceDisplayName(OfsOrderItem item, Dictionary<(string ChannelCode, string CskuCode), string?> cache)
    {
        if (string.IsNullOrEmpty(item.ChannelCode) || string.IsNullOrEmpty(item.MappedSku)) return null;

        var key = (item.ChannelCode, item.MappedSku);
        if (cache.TryGetValue(key, out var cached)) return cached;

        var name = _channelSkuRepository.GetByChannelAndCskuCode(item.ChannelCode, item.MappedSku)?.InvoiceDisplayName;
        cache[key] = name;
        return name;
    }
}
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 온라인 거래처 취합(OnlinePartnerConsolidation_Spec.md §6.1 ①~④) — 이익분석 내보내기 결과
/// xlsx 1개를 읽어 _META 파싱, 미매핑/예외 분리, CSKU 축 정규화까지 수행한다. 집계(§6.2 이후)는
/// 하지 않는다. 마감/이익분석 화면의 계산 로직(SettlementLoader/ProfitCalculator)은 건드리지 않고
/// 그 결과 파일만 다시 읽는다(§1).
/// </summary>
public static class PartnerConsolidationFileLoader
{
    private const string DetailSheetName = "분석결과상세";
    private const string RawDataSheetName = "원본데이터";

    /// <param name="channelConfigService">§6.3 송장번호 추출용. null이면 배송건수 산정에 필요한
    /// TrackingNumbers를 채우지 않는다(파일 목록만 볼 때는 불필요).</param>
    public static PartnerConsolidationFile Load(string filePath, ChannelSkuRepository channelSkuRepository, ChannelConfigService? channelConfigService = null)
    {
        try
        {
            ExcelLicense.Ensure();
            using var package = ExcelFileOpener.Open(filePath);
            return LoadFromPackage(package, filePath, channelSkuRepository, channelConfigService);
        }
        catch (Exception ex)
        {
            return new PartnerConsolidationFile
            {
                FilePath = filePath,
                ErrorMessage = $"파일을 여는 중 오류: {ex.Message}",
            };
        }
    }

    public static PartnerConsolidationFile LoadFromPackage(ExcelPackage package, string filePath,
        ChannelSkuRepository channelSkuRepository, ChannelConfigService? channelConfigService = null)
    {
        var meta = MetaSheetHelper.TryReadFromPackage(package);

        var file = new PartnerConsolidationFile
        {
            FilePath = filePath,
            CompanyName = meta?.CompanyName ?? "",
            ChannelCode = meta?.ChannelCode ?? "",
            ChannelName = meta?.ChannelName ?? "",
            HasMetaSheet = meta != null,
            IsSchemaV1 = meta != null && meta.SchemaVersion < 2,
        };

        var sheet = package.Workbook.Worksheets[DetailSheetName];
        if (sheet == null)
        {
            file.ErrorMessage = $"'{DetailSheetName}' 시트를 찾을 수 없습니다. 이익분석 내보내기 결과 파일이 맞는지 확인하세요.";
            return file;
        }

        var headerMap = BuildHeaderMap(sheet);
        var lastRow = sheet.Dimension?.End.Row ?? 1;
        var rows = new List<PartnerConsolidationRow>();

        // 채널별 CSKU 목록 캐시 — 파일이 여러 개라도 같은 채널이면 한 번만 조회.
        var cskuByChannel = new Dictionary<string, List<ChannelSkuModel>>(StringComparer.Ordinal);

        for (int r = 2; r <= lastRow; r++)
        {
            var channelCode = GetText(sheet, headerMap, r, "채널");
            var productName = GetText(sheet, headerMap, r, "상품명") ?? "";
            var optionName = GetText(sheet, headerMap, r, "옵션명") ?? "";
            var mappedSku = GetText(sheet, headerMap, r, "매핑SKU") ?? "";
            var status = GetText(sheet, headerMap, r, "상태") ?? "";
            var qty = GetQuantity(sheet, headerMap, r);
            var shipping = GetDecimal(sheet, headerMap, r, "배송비");

            // 완전 공백 행(꼬리 서식 등)은 건너뛴다.
            if (string.IsNullOrWhiteSpace(channelCode) && string.IsNullOrWhiteSpace(productName) &&
                string.IsNullOrWhiteSpace(mappedSku) && qty == 0)
                continue;

            var effectiveChannelCode = string.IsNullOrWhiteSpace(channelCode) ? file.ChannelCode : channelCode!;

            // SettlementRowStatus의 미매핑/제외 판정을 그대로 재사용(마감/이익분석과 동일 기준 — §6.4).
            var statusProbe = new SettlementData { Msku = mappedSku, Status = status };
            var kind = SettlementRowStatus.IsExcludedByExceptionRule(statusProbe)
                ? PartnerConsolidationRowKind.Excluded
                : SettlementRowStatus.IsUnresolved(statusProbe)
                    ? PartnerConsolidationRowKind.Unmapped
                    : PartnerConsolidationRowKind.Mapped;

            var row = new PartnerConsolidationRow
            {
                CompanyName = file.CompanyName,
                ChannelCode = effectiveChannelCode,
                ChannelName = file.ChannelName,
                ProductName = productName,
                OptionName = optionName,
                Quantity = qty,
                Shipping = shipping,
                RawMappedSku = mappedSku,
                RawStatus = status,
                Kind = kind,
                SourceFileName = file.FileName,
            };

            if (kind == PartnerConsolidationRowKind.Mapped)
                ResolveCsku(row, mappedSku, effectiveChannelCode, channelSkuRepository, cskuByChannel);

            rows.Add(row);
        }

        file.Rows = rows;
        file.RowCount = rows.Count;

        if (channelConfigService != null && !string.IsNullOrWhiteSpace(file.ChannelCode))
            file.TrackingNumbers = ExtractTrackingNumbers(package, file.ChannelCode, channelConfigService);

        return file;
    }

    /// <summary>
    /// §6.3 — 송장번호는 '분석결과상세'에 컬럼이 없어 '원본데이터' 시트에서 읽어야 한다. 어느 열이
    /// 송장번호인지는 채널의 정산서 매핑(SettlementFieldMappings의 TrackingNo 표준필드)에 지정된
    /// 원본 헤더명으로 찾는다. 매핑이 없거나 그 헤더가 원본데이터에 없으면 빈 목록을 반환한다
    /// (호출자는 이를 "송장번호 전무"로 보고 배송비÷단가 추정으로 넘어간다 — D11).
    /// </summary>
    private static List<string> ExtractTrackingNumbers(ExcelPackage package, string channelCode, ChannelConfigService channelConfigService)
    {
        var channelConfig = channelConfigService.Load().FirstOrDefault(c => c.ChannelCode == channelCode);
        var headerName = channelConfig?.SettlementFieldMappings.GetValueOrDefault(StdField.TrackingNo)?.Column;
        if (string.IsNullOrWhiteSpace(headerName)) return [];

        var rawSheet = package.Workbook.Worksheets[RawDataSheetName];
        if (rawSheet == null) return [];

        var rawHeaderMap = BuildHeaderMap(rawSheet);
        if (!rawHeaderMap.TryGetValue(headerName, out var col)) return [];

        var lastRow = rawSheet.Dimension?.End.Row ?? 1;
        var values = new List<string>();
        for (int r = 2; r <= lastRow; r++)
        {
            var text = rawSheet.Cells[r, col].Text?.Trim();
            if (!string.IsNullOrEmpty(text))
                values.Add(text);
        }
        return values;
    }

    /// <summary>
    /// §6.1 ④: 매핑SKU를 (채널코드, 코드)로 ChannelSkuTable 조회해 CSKU로 확정한다. 없으면
    /// 마스터SKU로 간주해 그 채널에서 같은 Msku를 가진 CSKU를 찾는다 — 정확히 1개면 승격,
    /// 0개/2개 이상이면 "CSKU 미확정"(CskuUnresolved)으로 분리한다.
    /// </summary>
    private static void ResolveCsku(PartnerConsolidationRow row, string mappedSku, string channelCode,
        ChannelSkuRepository channelSkuRepository, Dictionary<string, List<ChannelSkuModel>> cache)
    {
        if (!cache.TryGetValue(channelCode, out var cskus))
        {
            cskus = channelSkuRepository.GetAllByChannel(channelCode);
            cache[channelCode] = cskus;
        }

        var direct = cskus.FirstOrDefault(c => string.Equals(c.CskuCode, mappedSku, StringComparison.Ordinal));
        if (direct != null)
        {
            row.ResolvedCskuCode = direct.CskuCode;
            row.ResolvedMsku = direct.Msku;
            return;
        }

        var byMsku = cskus.Where(c => string.Equals(c.Msku, mappedSku, StringComparison.Ordinal)).ToList();
        if (byMsku.Count == 1)
        {
            row.ResolvedCskuCode = byMsku[0].CskuCode;
            row.ResolvedMsku = byMsku[0].Msku;
            return;
        }

        row.Kind = PartnerConsolidationRowKind.CskuUnresolved;
    }

    private static Dictionary<string, int> BuildHeaderMap(ExcelWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var lastCol = sheet.Dimension?.End.Column ?? 0;
        for (int c = 1; c <= lastCol; c++)
        {
            var text = sheet.Cells[1, c].Text?.Trim();
            if (!string.IsNullOrEmpty(text) && !map.ContainsKey(text))
                map[text] = c;
        }
        return map;
    }

    private static string? GetText(ExcelWorksheet sheet, Dictionary<string, int> headerMap, int row, string header)
        => headerMap.TryGetValue(header, out var col) ? sheet.Cells[row, col].Text?.Trim() : null;

    private static int GetQuantity(ExcelWorksheet sheet, Dictionary<string, int> headerMap, int row)
    {
        if (!headerMap.TryGetValue("수량", out var col)) return 0;
        var value = sheet.Cells[row, col].Value;
        return value switch
        {
            double d => (int)Math.Round(d),
            int i => i,
            _ => int.TryParse(sheet.Cells[row, col].Text?.Replace(",", ""), out var parsed) ? parsed : 0,
        };
    }

    private static decimal GetDecimal(ExcelWorksheet sheet, Dictionary<string, int> headerMap, int row, string header)
    {
        if (!headerMap.TryGetValue(header, out var col)) return 0m;
        var value = sheet.Cells[row, col].Value;
        return value switch
        {
            double d => (decimal)d,
            int i => i,
            decimal m => m,
            _ => decimal.TryParse(sheet.Cells[row, col].Text?.Replace(",", ""), out var parsed) ? parsed : 0m,
        };
    }
}

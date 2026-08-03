using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Exporters;

/// <summary>
/// 채널 일괄등록(엑셀) 기획서 §4.1/§4.2. 빈 양식 다운로드와, 등록된 전체 채널을 같은 양식으로
/// 채워 내보내는 "현재 설정 내보내기"(§4.6 라이트립 — 무수정 재업로드 시 변경 0건이어야 함)를 제공한다.
/// </summary>
public static class ChannelTemplateExporter
{
    /// <summary>빈 템플릿(헤더 + 참고 시트만 채움)을 저장한다.</summary>
    public static void ExportBlank(string filePath)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        BuildSheets(package);
        WriteMeta(package);
        ExportHelper.SaveExcel(package, filePath);
    }

    /// <summary>
    /// 등록된 전체 채널의 현재 값을 양식에 채워 저장한다. 채널코드를 채워서 출력하므로,
    /// 무수정으로 그대로 재업로드하면 전부 "수정" 판정에 실질 변경 0건이 되어야 한다(§4.6).
    /// </summary>
    public static void ExportCurrent(string filePath, List<SalesChannel> channels, List<ChannelConfig> configs, List<DocParty> parties)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var (channelSheet, orderSheet, settlementSheet, partySheet) = BuildSheets(package);

        var configsByCode = configs.ToDictionary(c => c.ChannelCode);
        var partiesByCode = parties.Where(p => !string.IsNullOrEmpty(p.ChannelCode)).ToDictionary(p => p.ChannelCode);

        var row = 2;
        foreach (var channel in channels.OrderBy(c => c.GroupName).ThenBy(c => c.DisplayOrder).ThenBy(c => c.ChannelName))
        {
            if (!configsByCode.TryGetValue(channel.ChannelCode, out var config)) continue;

            WriteChannelRow(channelSheet, row, channel, config);
            row++;

            WriteMappingRows(orderSheet, channel.ChannelName, config.OrderFieldMappings);
            WriteMappingRows(settlementSheet, channel.ChannelName, config.SettlementFieldMappings);

            if (partiesByCode.TryGetValue(channel.ChannelCode, out var party))
            {
                WritePartyRow(partySheet, channel.ChannelName, party);
            }
        }

        WriteMeta(package);
        ExportHelper.SaveExcel(package, filePath);
    }

    private static (ExcelWorksheet Channel, ExcelWorksheet Order, ExcelWorksheet Settlement, ExcelWorksheet Party) BuildSheets(ExcelPackage package)
    {
        var channelSheet = package.Workbook.Worksheets.Add(ChannelBulkImportSchema.ChannelSheet);
        WriteHeaders(channelSheet, ChannelBulkImportSchema.ChannelHeaders);

        var orderSheet = package.Workbook.Worksheets.Add(ChannelBulkImportSchema.OrderMappingSheet);
        WriteHeaders(orderSheet, ChannelBulkImportSchema.MappingHeaders);

        var settlementSheet = package.Workbook.Worksheets.Add(ChannelBulkImportSchema.SettlementMappingSheet);
        WriteHeaders(settlementSheet, ChannelBulkImportSchema.MappingHeaders);

        var partySheet = package.Workbook.Worksheets.Add(ChannelBulkImportSchema.PartySheet);
        WriteHeaders(partySheet, ChannelBulkImportSchema.PartyHeaders);

        BuildReferenceSheet(package);

        return (channelSheet, orderSheet, settlementSheet, partySheet);
    }

    private static void WriteHeaders(ExcelWorksheet sheet, string[] headers)
    {
        for (var col = 0; col < headers.Length; col++)
        {
            sheet.Cells[1, col + 1].Value = headers[col];
        }
        sheet.View.FreezePanes(2, 1);
        sheet.Cells[1, 1, 1, headers.Length].Style.Font.Bold = true;
    }

    private static void BuildReferenceSheet(ExcelPackage package)
    {
        var sheet = package.Workbook.Worksheets.Add(ChannelBulkImportSchema.ReferenceSheet);
        sheet.Cells[1, 1].Value = "채널유형";
        sheet.Cells[1, 2].Value = "발주서 표준필드";
        sheet.Cells[1, 3].Value = "정산서 표준필드(전체 채널유형 통합, §2.4 참고)";
        sheet.Cells[1, 4].Value = "Y/N";
        sheet.Cells[1, 1, 1, 4].Style.Font.Bold = true;

        var channelTypeLabels = Enum.GetValues<ChannelType>().Select(t => t.ToKoreanLabel()).Distinct().ToList();
        for (var i = 0; i < channelTypeLabels.Count; i++)
            sheet.Cells[i + 2, 1].Value = channelTypeLabels[i];

        var orderFieldLabels = ChannelFieldSets.OrderMappingFields.Select(StdFieldLabels.GetLabel).ToList();
        for (var i = 0; i < orderFieldLabels.Count; i++)
            sheet.Cells[i + 2, 2].Value = orderFieldLabels[i];

        var settlementFieldLabels = ChannelFieldSets.AllSettlementMappingFields.Select(StdFieldLabels.GetLabel).ToList();
        for (var i = 0; i < settlementFieldLabels.Count; i++)
            sheet.Cells[i + 2, 3].Value = settlementFieldLabels[i];

        sheet.Cells[2, 4].Value = "Y";
        sheet.Cells[3, 4].Value = "N";

        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
    }

    private static void WriteChannelRow(ExcelWorksheet sheet, int row, SalesChannel channel, ChannelConfig config)
    {
        sheet.Cells[row, 1].Value = channel.ChannelCode;
        sheet.Cells[row, 2].Value = channel.ChannelName;
        sheet.Cells[row, 3].Value = config.ChannelType.ToKoreanLabel();
        sheet.Cells[row, 4].Value = channel.GroupName;
        sheet.Cells[row, 5].Value = channel.DisplayOrder;
        sheet.Cells[row, 6].Value = ChannelBulkImportSchema.ToYn(channel.IsFavorite);
        sheet.Cells[row, 7].Value = ChannelBulkImportSchema.ToYn(channel.IsPurchase);
        sheet.Cells[row, 8].Value = ChannelBulkImportSchema.ToYn(channel.IsSales);
        sheet.Cells[row, 9].Value = config.ExchangeRate;
        sheet.Cells[row, 10].Value = ChannelBulkImportSchema.ToYn(config.IsCumulativeOrderFile);
        sheet.Cells[row, 11].Value = config.CumulativeOrderWindowDays;
        sheet.Cells[row, 12].Value = config.AutoOrderHints;
        // 13열(설정복사원본)은 신규 등록 전용 입력칸이라 내보내기에서는 채우지 않는다.
    }

    private static void WriteMappingRows(ExcelWorksheet sheet, string channelName, Dictionary<StdField, FieldMapping> mappings)
    {
        foreach (var (field, mapping) in mappings)
        {
            if (string.IsNullOrEmpty(mapping.Column) && string.IsNullOrEmpty(mapping.FixedValue)) continue;

            var row = sheet.Dimension?.End.Row + 1 ?? 2;
            sheet.Cells[row, 1].Value = channelName;
            sheet.Cells[row, 2].Value = StdFieldLabels.GetLabel(field);
            sheet.Cells[row, 3].Value = mapping.SheetName;
            sheet.Cells[row, 4].Value = mapping.HeaderRow;
            sheet.Cells[row, 5].Value = mapping.Column;
            sheet.Cells[row, 6].Value = mapping.FixedValue;
        }
    }

    private static void WritePartyRow(ExcelWorksheet sheet, string channelName, DocParty party)
    {
        var row = sheet.Dimension?.End.Row + 1 ?? 2;
        sheet.Cells[row, 1].Value = channelName;
        sheet.Cells[row, 2].Value = party.RegNo;
        sheet.Cells[row, 3].Value = party.CompanyName;
        sheet.Cells[row, 4].Value = party.CeoName;
        sheet.Cells[row, 5].Value = party.Address;
        sheet.Cells[row, 6].Value = party.BizType;
        sheet.Cells[row, 7].Value = party.BizItem;
        sheet.Cells[row, 8].Value = party.Tel;
        sheet.Cells[row, 9].Value = party.Email;
    }

    private static void WriteMeta(ExcelPackage package)
    {
        var sheet = package.Workbook.Worksheets.Add(ChannelBulkImportSchema.MetaSheet);
        sheet.Cells[1, 1].Value = "schema_version";
        sheet.Cells[1, 2].Value = ChannelBulkImportSchema.SchemaVersion;
        sheet.Cells[2, 1].Value = "exported_at";
        sheet.Cells[2, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        sheet.Cells[3, 1].Value = "app";
        sheet.Cells[3, 2].Value = "MiniERP2";
        package.Workbook.Worksheets.MoveToStart(ChannelBulkImportSchema.MetaSheet);
    }
}

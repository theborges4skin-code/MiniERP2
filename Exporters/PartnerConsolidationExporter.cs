using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Exporters;

/// <summary>
/// 온라인 거래처 취합(OnlinePartnerConsolidation_Spec.md §6.6) — 집계 결과를 엑셀 6시트로
/// 내보낸다: 거래처요약(+합계행) / CSKU상세 / 채널별배송건수(+청구액) / 단가미배정 / 미매핑·제외 / _META.
/// </summary>
public static class PartnerConsolidationExporter
{
    public static void Export(
        IReadOnlyList<PartnerConsolidationCompanySummary> companySummaries,
        IReadOnlyList<PartnerConsolidationCskuDetail> cskuDetails,
        IReadOnlyList<PartnerConsolidationChannelShipment> channelShipments,
        IReadOnlyList<PartnerConsolidationRow> unmappedExcludedRows,
        IReadOnlyList<PartnerConsolidationFile> sourceFiles,
        Func<string, decimal> resolveBillingRate,
        string filePath)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();

        WriteCompanySummarySheet(package, companySummaries);
        WriteCskuDetailSheet(package, cskuDetails);
        WriteChannelShipmentSheet(package, channelShipments, resolveBillingRate);
        WriteUnassignedSheet(package, cskuDetails.Where(d => d.IsPriceUnassigned).ToList());
        WriteUnmappedExcludedSheet(package, unmappedExcludedRows);
        WriteMetaSheet(package, sourceFiles);

        ExportHelper.SaveExcel(package, filePath);
    }

    private static readonly string[] CompanySummaryHeaders =
        ["상호명", "채널수", "총수량", "납품매출액", "납품이익액", "배송건수", "배송비청구액", "미배정건수"];

    private static void WriteCompanySummarySheet(ExcelPackage package, IReadOnlyList<PartnerConsolidationCompanySummary> summaries)
    {
        var sheet = package.Workbook.Worksheets.Add("거래처요약");
        for (int i = 0; i < CompanySummaryHeaders.Length; i++) sheet.Cells[1, i + 1].Value = CompanySummaryHeaders[i];

        int row = 2;
        foreach (var s in summaries)
        {
            sheet.Cells[row, 1].Value = s.CompanyName;
            sheet.Cells[row, 2].Value = s.ChannelCount;
            sheet.Cells[row, 3].Value = s.TotalQuantity;
            sheet.Cells[row, 4].Value = s.TotalSupplyRevenue;
            sheet.Cells[row, 5].Value = s.TotalSupplyProfit;
            sheet.Cells[row, 6].Value = s.ShipmentCount;
            sheet.Cells[row, 7].Value = s.ShippingFeeTotal;
            sheet.Cells[row, 8].Value = s.UnassignedPriceCount;
            row++;
        }

        // 합계행(§6.6).
        sheet.Cells[row, 1].Value = "합계";
        sheet.Cells[row, 3].Value = summaries.Sum(s => s.TotalQuantity);
        sheet.Cells[row, 4].Value = summaries.Sum(s => s.TotalSupplyRevenue);
        sheet.Cells[row, 5].Value = summaries.Sum(s => s.TotalSupplyProfit);
        sheet.Cells[row, 6].Value = summaries.Sum(s => s.ShipmentCount);
        sheet.Cells[row, 7].Value = summaries.Sum(s => s.ShippingFeeTotal);
        sheet.Cells[row, 8].Value = summaries.Sum(s => s.UnassignedPriceCount);

        sheet.Cells[1, 1, 1, CompanySummaryHeaders.Length].AutoFitColumns(8, 50);
    }

    private static readonly string[] CskuDetailHeaders =
        ["상호명", "CSKU", "품목명", "마스터SKU", "수량", "납품단가", "단가출처", "납품매출액", "제조원가", "납품이익액"];

    private static void WriteCskuDetailSheet(ExcelPackage package, IReadOnlyList<PartnerConsolidationCskuDetail> details)
    {
        var sheet = package.Workbook.Worksheets.Add("CSKU상세");
        for (int i = 0; i < CskuDetailHeaders.Length; i++) sheet.Cells[1, i + 1].Value = CskuDetailHeaders[i];

        int row = 2;
        foreach (var d in details)
        {
            sheet.Cells[row, 1].Value = d.CompanyName;
            sheet.Cells[row, 2].Value = d.CskuCode;
            sheet.Cells[row, 3].Value = d.ProductName;
            sheet.Cells[row, 4].Value = d.Msku;
            sheet.Cells[row, 5].Value = d.Quantity;
            sheet.Cells[row, 6].Value = d.SupplyPrice;
            sheet.Cells[row, 7].Value = d.PriceSourceDisplay;
            sheet.Cells[row, 8].Value = d.SupplyRevenue;
            sheet.Cells[row, 9].Value = (object?)d.CostPrice ?? "";
            sheet.Cells[row, 10].Value = (object?)d.SupplyProfit ?? "";
            row++;
        }
        sheet.Cells[1, 1, 1, CskuDetailHeaders.Length].AutoFitColumns(8, 50);
    }

    private static readonly string[] ChannelShipmentHeaders = ["상호명", "채널", "건수", "산정근거", "배송비총액", "청구액"];

    private static void WriteChannelShipmentSheet(ExcelPackage package, IReadOnlyList<PartnerConsolidationChannelShipment> shipments, Func<string, decimal> resolveBillingRate)
    {
        var sheet = package.Workbook.Worksheets.Add("채널별배송건수");
        for (int i = 0; i < ChannelShipmentHeaders.Length; i++) sheet.Cells[1, i + 1].Value = ChannelShipmentHeaders[i];

        int row = 2;
        foreach (var c in shipments)
        {
            sheet.Cells[row, 1].Value = c.CompanyName;
            sheet.Cells[row, 2].Value = string.IsNullOrWhiteSpace(c.ChannelName) ? c.ChannelCode : c.ChannelName;
            sheet.Cells[row, 3].Value = c.ShipmentCount;
            sheet.Cells[row, 4].Value = c.BasisDisplay;
            sheet.Cells[row, 5].Value = c.ShippingTotal;
            sheet.Cells[row, 6].Value = c.ShipmentCount * resolveBillingRate(c.CompanyName);
            row++;
        }
        sheet.Cells[1, 1, 1, ChannelShipmentHeaders.Length].AutoFitColumns(8, 50);
    }

    private static readonly string[] UnassignedHeaders = ["상호명", "CSKU", "품목명", "마스터SKU", "수량"];

    private static void WriteUnassignedSheet(ExcelPackage package, IReadOnlyList<PartnerConsolidationCskuDetail> unassigned)
    {
        var sheet = package.Workbook.Worksheets.Add("단가미배정");
        for (int i = 0; i < UnassignedHeaders.Length; i++) sheet.Cells[1, i + 1].Value = UnassignedHeaders[i];

        int row = 2;
        foreach (var d in unassigned)
        {
            sheet.Cells[row, 1].Value = d.CompanyName;
            sheet.Cells[row, 2].Value = d.CskuCode;
            sheet.Cells[row, 3].Value = d.ProductName;
            sheet.Cells[row, 4].Value = d.Msku;
            sheet.Cells[row, 5].Value = d.Quantity;
            row++;
        }
        sheet.Cells[1, 1, 1, UnassignedHeaders.Length].AutoFitColumns(8, 50);
    }

    private static readonly string[] UnmappedExcludedHeaders =
        ["상호명", "채널", "상품명", "매핑SKU", "상태", "분류", "파일"];

    private static void WriteUnmappedExcludedSheet(ExcelPackage package, IReadOnlyList<PartnerConsolidationRow> rows)
    {
        var sheet = package.Workbook.Worksheets.Add("미매핑·제외");
        for (int i = 0; i < UnmappedExcludedHeaders.Length; i++) sheet.Cells[1, i + 1].Value = UnmappedExcludedHeaders[i];

        int row = 2;
        foreach (var r in rows)
        {
            sheet.Cells[row, 1].Value = r.CompanyName;
            sheet.Cells[row, 2].Value = r.ChannelCode;
            sheet.Cells[row, 3].Value = r.ProductName;
            sheet.Cells[row, 4].Value = r.RawMappedSku;
            sheet.Cells[row, 5].Value = r.RawStatus;
            sheet.Cells[row, 6].Value = r.Kind.ToString();
            sheet.Cells[row, 7].Value = r.SourceFileName;
            row++;
        }
        sheet.Cells[1, 1, 1, UnmappedExcludedHeaders.Length].AutoFitColumns(8, 50);
    }

    /// <summary>
    /// 표준 _META 7행(MetaSheetHelper.WriteToPackage)에 이어, 이 내보내기 고유 정보(취합 대상
    /// 파일 목록)를 추가로 적는다 — 이 파일은 최종 산출물이라 다시 읽어들일 필요가 없으므로
    /// FileMeta 스키마를 확장하지 않고 여기서만 몇 줄 더 붙인다.
    /// </summary>
    private static void WriteMetaSheet(ExcelPackage package, IReadOnlyList<PartnerConsolidationFile> sourceFiles)
    {
        MetaSheetHelper.WriteToPackage(package, new FileMeta
        {
            SourceType = "partner_rollup",
            Period = DateTime.Now.ToString("yyyyMM"),
        });

        var sheet = package.Workbook.Worksheets["_META"]!;
        sheet.Cells[8, 1].Value = "source_files";
        sheet.Cells[8, 2].Value = string.Join("; ", sourceFiles.Select(f => f.FileName));
    }
}

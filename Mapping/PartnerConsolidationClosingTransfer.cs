using System.Globalization;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Mapping;

/// <summary>온라인 거래처 취합 파일에서 읽은 거래처 1곳분 — 품목별 합계 라인 + 배송비 청구.</summary>
public class PartnerConsolidationClosingPackage
{
    public required string CompanyName { get; init; }

    /// <summary>품목별 합계 라인(택배비 제외). 단가·원가는 VAT포함(ChannelSkuTable.SupplyPrice 기준).</summary>
    public List<PartnerClosingLine> ProductLines { get; } = [];

    public int ShipmentCount { get; set; }
    public decimal ShippingFeeTotal { get; set; }
    public int UnassignedPriceCount { get; set; }

    /// <summary>제조원가가 비어 있던 품목 수(원가 0으로 들어가 이익이 부풀려진다).</summary>
    public int MissingCostCount { get; set; }

    public decimal ProductQty => ProductLines.Sum(l => l.Qty);
    public decimal ProductSupply => ProductLines.Sum(l => l.Qty * l.UnitPrice);
    public decimal TotalSupply => ProductSupply + ShippingFeeTotal;

    /// <summary>마감보드에 넣을 라인: 품목 라인 + (배송건수가 있으면) 택배비 라인 1개. 일자는 마감월 말일.</summary>
    public List<PartnerClosingLine> BuildClosingLines(string period)
    {
        var lineDate = DateTime.ParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture).AddMonths(1).AddDays(-1);
        var lines = ProductLines.Select(l => new PartnerClosingLine
        {
            LineDate = lineDate,
            CskuCode = l.CskuCode,
            MasterSku = l.MasterSku,
            ItemName = l.ItemName,
            Qty = l.Qty,
            UnitPrice = l.UnitPrice,
            CostPrice = l.CostPrice,
            Profit = (l.UnitPrice - l.CostPrice) * l.Qty,
        }).ToList();

        if (ShipmentCount > 0)
        {
            // 실배송비는 그대로 청구하는 실비라 원가=단가(이익 0)로 둔다.
            var rate = ShippingFeeTotal / ShipmentCount;
            lines.Add(new PartnerClosingLine
            {
                LineDate = lineDate,
                CskuCode = PartnerConsolidationClosingTransfer.ShippingLineCode,
                ItemName = PartnerConsolidationClosingTransfer.ShippingLineCode,
                Qty = ShipmentCount,
                UnitPrice = rate,
                CostPrice = rate,
                Profit = 0,
            });
        }
        return lines;
    }
}

/// <summary>
/// 온라인 거래처 취합 결과 파일(PartnerConsolidationExporter)을 거래처 마감보드로 보낸다. 거래처마다
/// 상호명과 같은 이름의 수동(MANUAL) 거래처에 품목별 합계 라인과 택배비 라인을 저장한다 — 상세
/// 출고이력은 DB에 두지 않고 취합 파일이 원본이다(비고에 파일명을 남긴다). 명세표의 공급받는자
/// 정보는 같은 상호명의 거래처 프로필(DocPartyTable)에서 찾는다.
/// </summary>
public class PartnerConsolidationClosingTransfer(PartnerClosingRepository closingRepo, PartnerMasterRepository masterRepo)
{
    public const string ShippingLineCode = "택배비";

    public sealed record ReadResult(List<PartnerConsolidationClosingPackage> Packages, DateTime? FileCreatedAt, string? Error);

    public static ReadResult ReadFile(string filePath)
    {
        try
        {
            ExcelLicense.Ensure();
            using var package = ExcelFileOpener.Open(filePath);
            return ReadPackage(package);
        }
        catch (Exception ex)
        {
            return new ReadResult([], null, $"파일을 여는 중 오류: {ex.Message}");
        }
    }

    public static ReadResult ReadPackage(ExcelPackage package)
    {
        var summarySheet = package.Workbook.Worksheets["거래처요약"];
        var detailSheet = package.Workbook.Worksheets["CSKU상세"];
        if (summarySheet == null || detailSheet == null)
            return new ReadResult([], null, "'거래처요약'/'CSKU상세' 시트가 없습니다. 온라인 거래처 취합에서 내보낸 파일이 맞는지 확인하세요.");

        var byCompany = new Dictionary<string, PartnerConsolidationClosingPackage>(StringComparer.Ordinal);
        PartnerConsolidationClosingPackage GetOrAdd(string company) =>
            byCompany.TryGetValue(company, out var p) ? p : byCompany[company] = new PartnerConsolidationClosingPackage { CompanyName = company };

        var sh = HeaderMap(summarySheet);
        for (int r = 2; r <= (summarySheet.Dimension?.End.Row ?? 1); r++)
        {
            var company = Text(summarySheet, sh, r, "상호명");
            if (string.IsNullOrWhiteSpace(company) || company == "합계") continue;
            var p = GetOrAdd(company);
            p.ShipmentCount = (int)Number(summarySheet, sh, r, "배송건수");
            p.ShippingFeeTotal = Number(summarySheet, sh, r, "배송비청구액");
            p.UnassignedPriceCount = (int)Number(summarySheet, sh, r, "미배정건수");
        }

        var dh = HeaderMap(detailSheet);
        for (int r = 2; r <= (detailSheet.Dimension?.End.Row ?? 1); r++)
        {
            var company = Text(detailSheet, dh, r, "상호명");
            if (string.IsNullOrWhiteSpace(company) || !byCompany.TryGetValue(company, out var p)) continue;

            var invoiceName = Text(detailSheet, dh, r, "송장출력용 상품명");
            var costText = Text(detailSheet, dh, r, "제조원가");
            if (string.IsNullOrWhiteSpace(costText)) p.MissingCostCount++;

            p.ProductLines.Add(new PartnerClosingLine
            {
                CskuCode = Text(detailSheet, dh, r, "CSKU"),
                MasterSku = Text(detailSheet, dh, r, "마스터SKU"),
                ItemName = string.IsNullOrWhiteSpace(invoiceName) ? Text(detailSheet, dh, r, "품목명") : invoiceName,
                Qty = Number(detailSheet, dh, r, "수량"),
                UnitPrice = Number(detailSheet, dh, r, "납품단가"),
                CostPrice = Number(detailSheet, dh, r, "제조원가"),
            });
        }

        DateTime? createdAt = null;
        var meta = package.Workbook.Worksheets["_META"];
        if (meta?.Dimension != null)
        {
            for (int r = 1; r <= meta.Dimension.End.Row; r++)
            {
                if (meta.Cells[r, 1].Text.Trim() == "file_created_at" &&
                    DateTime.TryParse(meta.Cells[r, 2].Text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    createdAt = dt;
            }
        }

        return new ReadResult(byCompany.Values.ToList(), createdAt, null);
    }

    /// <summary>상호명과 같은 이름의 수동 거래처 키. 없으면 null(아직 마감보드에 올린 적 없음).</summary>
    public string? FindPartyKey(string companyName) =>
        masterRepo.GetAll().FirstOrDefault(p => p.IsManual && p.PartyName == companyName)?.PartyKey;

    public PartnerClosing? GetExistingHeader(string period, string companyName) =>
        FindPartyKey(companyName) is { } key ? closingRepo.GetHeader(period, key) : null;

    /// <summary>거래처 1곳을 마감보드로 보낸다. 이미 확정된 마감이면 InvalidOperationException.</summary>
    public PartnerClosing Transfer(string period, PartnerConsolidationClosingPackage package, string sourceFileName, bool confirm)
    {
        var partyKey = masterRepo.GetOrAddManualPartner(package.CompanyName);

        var note = $"온라인 거래처 취합: {sourceFileName}";
        return closingRepo.ReplaceManualLines(period, partyKey, package.CompanyName, package.BuildClosingLines(period), note, confirm);
    }

    private static Dictionary<string, int> HeaderMap(ExcelWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int c = 1; c <= (sheet.Dimension?.End.Column ?? 0); c++)
        {
            var text = sheet.Cells[1, c].Text?.Trim();
            if (!string.IsNullOrEmpty(text) && !map.ContainsKey(text)) map[text] = c;
        }
        return map;
    }

    private static string Text(ExcelWorksheet sheet, Dictionary<string, int> map, int row, string header) =>
        map.TryGetValue(header, out var c) ? sheet.Cells[row, c].Text?.Trim() ?? "" : "";

    private static decimal Number(ExcelWorksheet sheet, Dictionary<string, int> map, int row, string header)
    {
        if (!map.TryGetValue(header, out var c)) return 0m;
        return sheet.Cells[row, c].Value switch
        {
            double d => (decimal)d,
            int i => i,
            decimal m => m,
            _ => decimal.TryParse(sheet.Cells[row, c].Text?.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m,
        };
    }
}

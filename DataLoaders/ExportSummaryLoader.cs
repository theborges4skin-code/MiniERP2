using System.Globalization;
using System.Text;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 수출요약보고서(export_summary_report_기획.md)의 3개 트랙(A=수출신고, B=판매, C=송금)을 읽어
/// (마켓, 연월, 금액) 단위 원자료로 변환한다. 세 트랙은 서로 금액을 일치시키려 하지 않고
/// 각자 독립적으로 집계한다(기획서 1절 설계 원칙).
/// </summary>
public static class ExportSummaryLoader
{
    public record LoadResult(List<ExportSummaryEntry> Entries, int UnmatchedRowCount, string? UnmatchedSample, List<ExportSummaryRawEntry>? UsdRawEntries = null);

    /// <summary>트랙A: 수출신고 내역 한 파일. 통화코드 컬럼으로 마켓을 나눈다. CurrencyToMarket에
    /// 없는 통화(예: USD)는 미매핑으로 세어 UnmatchedRowCount로 알려준다.</summary>
    public static LoadResult LoadDeclarationFile(string filePath, DeclarationTrackConfig config)
    {
        using var package = ExcelFileOpener.Open(filePath);
        var sheet = RequireFirstSheet(package);

        var columns = ResolveColumns(sheet, config.HeaderRow, config.DateColumn, config.AmountColumn, config.CurrencyColumn);
        RequireColumns(columns, config.DateColumn, config.AmountColumn, config.CurrencyColumn);

        var entries = new List<ExportSummaryEntry>();
        var usdRawEntries = new List<ExportSummaryRawEntry>();
        var unmatched = 0;
        string? unmatchedSample = null;
        var lastRow = sheet.Dimension?.End.Row ?? config.HeaderRow;

        for (var row = config.HeaderRow + 1; row <= lastRow; row++)
        {
            var currency = sheet.Cells[row, columns[config.CurrencyColumn]].Text?.Trim();
            if (string.IsNullOrEmpty(currency)) continue;

            var date = ParseDate(sheet.Cells[row, columns[config.DateColumn]]);
            if (date is null) continue;

            var amount = ParseAmount(sheet.Cells[row, columns[config.AmountColumn]]);

            if (!config.CurrencyToMarket.TryGetValue(currency, out var marketCode))
            {
                // USD 등 미분리 통화: UnmatchedRowCount로 집계하고 상세도 보존
                unmatched++;
                unmatchedSample ??= currency;
                usdRawEntries.Add(new ExportSummaryRawEntry(date.Value.ToString("yyyy-MM-dd"), currency, amount));
                continue;
            }

            entries.Add(new ExportSummaryEntry("A", marketCode, ToYearMonth(date.Value), amount, Indicator: "신고액", Currency: currency));

            // USD_UNMATCHED로 매핑된 통화(CurrencyToMarket에 USD→USD_UNMATCHED 등록 시)는
            // 정상 집계에 포함하면서 상세도 함께 보존한다.
            if (marketCode == "USD_UNMATCHED")
                usdRawEntries.Add(new ExportSummaryRawEntry(date.Value.ToString("yyyy-MM-dd"), currency, amount));

        }

        return new LoadResult(entries, unmatched, unmatchedSample, usdRawEntries.Count > 0 ? usdRawEntries : null);
    }

    /// <summary>트랙B: 마켓 하나에 해당하는 판매(주문) 파일 한 개.</summary>
    /// <param name="salesCurrency">해당 마켓의 판매통화(ExportSummaryMarket.Currency). tidy 출력에서 통화 열에 사용.</param>
    public static LoadResult LoadSalesFile(string filePath, SalesMarketMapping mapping, string salesCurrency = "")
    {
        using var package = ExcelFileOpener.Open(filePath);
        var sheet = RequireFirstSheet(package);

        // 주문번호 컬럼을 지정하면 "주문 단위" 모드: 판매액·차감 컬럼이 주문 전체 금액이라 주문의 모든 줄에
        // 같은 값이 반복되므로(쇼피 Grand Total 등) 주문번호당 첫 줄 한 번만 센다.
        // 지정하지 않으면 줄 단위 모드: 줄마다 판매액(수량 컬럼이 있으면 ×수량)을 더한다.
        var orderLevel = !string.IsNullOrWhiteSpace(mapping.OrderIdColumn);
        var useQuantity = !orderLevel && !string.IsNullOrWhiteSpace(mapping.QuantityColumn);
        var subtracts = orderLevel
            ? mapping.SubtractColumns.Where(s => !string.IsNullOrWhiteSpace(s)).Select(ParseSubtractColumn).ToList()
            : new List<(string Header, decimal Factor)>();

        var headers = new List<string> { mapping.DateColumn, mapping.AmountColumn };
        if (useQuantity) headers.Add(mapping.QuantityColumn);
        if (orderLevel) headers.Add(mapping.OrderIdColumn);
        headers.AddRange(subtracts.Select(s => s.Header));
        var headerArray = headers.Distinct().ToArray();
        var columns = ResolveColumns(sheet, mapping.HeaderRow, headerArray);
        RequireColumns(columns, headerArray);

        var entries = new List<ExportSummaryEntry>();
        var seenOrders = new HashSet<string>();
        var lastRow = sheet.Dimension?.End.Row ?? mapping.HeaderRow;

        for (var row = mapping.HeaderRow + 1; row <= lastRow; row++)
        {
            if (orderLevel)
            {
                var orderId = sheet.Cells[row, columns[mapping.OrderIdColumn]].Text?.Trim();
                if (string.IsNullOrEmpty(orderId) || !seenOrders.Add(orderId)) continue;
            }

            var date = ParseDate(sheet.Cells[row, columns[mapping.DateColumn]]);
            if (date is null) continue;

            var amount = ParseAmount(sheet.Cells[row, columns[mapping.AmountColumn]]);
            if (useQuantity)
                amount *= ParseAmount(sheet.Cells[row, columns[mapping.QuantityColumn]]);
            foreach (var (header, factor) in subtracts)
                amount -= ParseAmount(sheet.Cells[row, columns[header]]) * factor;
            entries.Add(new ExportSummaryEntry("B", mapping.MarketCode, ToYearMonth(date.Value), amount, Indicator: "매출액", Currency: salesCurrency));
        }

        return new LoadResult(entries, UnmatchedRowCount: 0, UnmatchedSample: null);
    }

    /// <summary>트랙C: 송금(Payoneer 등) 내역 한 파일. Description 문구로 마켓을 나누고,
    /// ExcludeContains에 해당하는 행(내부이체 등)은 아예 건너뛴다.</summary>
    public static LoadResult LoadRemittanceFile(string filePath, RemittanceTrackConfig config)
    {
        using var package = ExcelFileOpener.Open(filePath);
        var sheet = RequireFirstSheet(package);

        var columns = ResolveColumns(sheet, config.HeaderRow, config.DateColumn, config.AmountColumn, config.DescriptionColumn, config.CurrencyColumn);
        RequireColumns(columns, config.DateColumn, config.AmountColumn, config.DescriptionColumn);
        columns.TryGetValue(config.CurrencyColumn, out var currencyCol);

        var entries = new List<ExportSummaryEntry>();
        var unmatched = 0;
        string? unmatchedSample = null;
        var lastRow = sheet.Dimension?.End.Row ?? config.HeaderRow;

        for (var row = config.HeaderRow + 1; row <= lastRow; row++)
        {
            var description = sheet.Cells[row, columns[config.DescriptionColumn]].Text?.Trim();
            if (string.IsNullOrEmpty(description)) continue;

            if (config.ExcludeContains.Any(x => description.Contains(x, StringComparison.OrdinalIgnoreCase)))
                continue;

            var marketCode = config.Rules.FirstOrDefault(rule =>
                rule.Contains.All(c => description.Contains(c, StringComparison.OrdinalIgnoreCase)))?.MarketCode;

            if (marketCode is null)
            {
                unmatched++;
                unmatchedSample ??= description;
                continue;
            }

            var date = ParseDate(sheet.Cells[row, columns[config.DateColumn]]);
            if (date is null) continue;

            var amount = ParseAmount(sheet.Cells[row, columns[config.AmountColumn]]);
            // 아마존 일본 송금은 JPY로 들어오므로 통화 컬럼이 있으면 그 값을 쓴다.
            var currency = currencyCol > 0 ? sheet.Cells[row, currencyCol].Text?.Trim() : null;
            entries.Add(new ExportSummaryEntry("C", marketCode, ToYearMonth(date.Value), amount, Indicator: "실익액",
                Currency: string.IsNullOrEmpty(currency) ? "USD" : currency));
        }

        return new LoadResult(entries, unmatched, unmatchedSample);
    }

    /// <summary>수출신고 대사용: 신고 내역 한 줄(신고번호·신고일자·통화·결제금액).</summary>
    public record DeclarationRow(string DeclarationNo, DateTime Date, string Currency, decimal Amount, string FileName);

    /// <summary>수출신고 대사용: 쇼피 등 판매 파일의 주문 1건.
    /// PaidAmount는 판매 트랙(LoadSalesFile 주문 단위 모드)과 같은 계산(소비자 실결제 상품금액),
    /// SellerBaseAmount는 할인가×수량 합 − 판매자 부담 쿠폰(수출신고 금액의 기준이 되는 값).</summary>
    public record SalesOrderRecord(
        string OrderId, string MarketCode, string Currency, string FileName,
        DateTime? CompletedAt, DateTime? ShippedAt, decimal PaidAmount, decimal SellerBaseAmount, string Skus);

    /// <summary>수출신고 파일의 행을 대사용 원본 그대로 읽는다(통화→마켓 매핑 없이 전 통화).</summary>
    public static List<DeclarationRow> LoadDeclarationRows(string filePath, DeclarationTrackConfig config)
    {
        using var package = ExcelFileOpener.Open(filePath);
        var sheet = RequireFirstSheet(package);

        var columns = ResolveColumns(sheet, config.HeaderRow, config.DateColumn, config.AmountColumn, config.CurrencyColumn, config.DeclarationNoColumn);
        RequireColumns(columns, config.DateColumn, config.AmountColumn, config.CurrencyColumn);
        columns.TryGetValue(config.DeclarationNoColumn, out var noCol);

        var rows = new List<DeclarationRow>();
        var fileName = Path.GetFileName(filePath);
        var lastRow = sheet.Dimension?.End.Row ?? config.HeaderRow;
        for (var row = config.HeaderRow + 1; row <= lastRow; row++)
        {
            var currency = sheet.Cells[row, columns[config.CurrencyColumn]].Text?.Trim();
            if (string.IsNullOrEmpty(currency)) continue;
            var date = ParseDate(sheet.Cells[row, columns[config.DateColumn]]);
            if (date is null) continue;

            var no = noCol > 0 ? sheet.Cells[row, noCol].Text?.Trim() ?? "" : "";
            rows.Add(new DeclarationRow(no, date.Value.Date, currency, ParseAmount(sheet.Cells[row, columns[config.AmountColumn]]), fileName));
        }
        return rows;
    }

    /// <summary>판매 파일을 주문 단위 레코드로 읽는다(수출신고 대사용). 주문 단위 모드(OrderIdColumn)와
    /// Recon 매핑이 있는 마켓만 가능하다.</summary>
    public static List<SalesOrderRecord> LoadSalesOrders(string filePath, SalesMarketMapping mapping, string currency)
    {
        if (string.IsNullOrWhiteSpace(mapping.OrderIdColumn) || mapping.Recon is null)
            throw new InvalidOperationException($"{mapping.MarketCode}: 수출신고 대사 설정(주문번호 컬럼·Recon)이 없습니다.");
        var recon = mapping.Recon;

        using var package = ExcelFileOpener.Open(filePath);
        var sheet = RequireFirstSheet(package);

        var subtracts = mapping.SubtractColumns.Where(s => !string.IsNullOrWhiteSpace(s)).Select(ParseSubtractColumn).ToList();
        var required = new List<string>
        {
            mapping.OrderIdColumn, mapping.DateColumn, mapping.AmountColumn,
            recon.ShipDateColumn, recon.UnitPriceColumn, recon.QuantityColumn,
        };
        if (!string.IsNullOrWhiteSpace(recon.SellerVoucherColumn)) required.Add(recon.SellerVoucherColumn);
        required.AddRange(subtracts.Select(s => s.Header));
        var headerArray = required.Distinct().ToArray();
        var columns = ResolveColumns(sheet, mapping.HeaderRow, headerArray.Append(recon.SkuColumn).ToArray());
        RequireColumns(columns, headerArray);
        columns.TryGetValue(recon.SkuColumn, out var skuCol);

        var orders = new Dictionary<string, (int FirstRow, decimal LineTotal, List<string> Skus)>();
        var order = new List<string>();
        var lastRow = sheet.Dimension?.End.Row ?? mapping.HeaderRow;
        for (var row = mapping.HeaderRow + 1; row <= lastRow; row++)
        {
            var orderId = sheet.Cells[row, columns[mapping.OrderIdColumn]].Text?.Trim();
            if (string.IsNullOrEmpty(orderId)) continue;

            var line = ParseAmount(sheet.Cells[row, columns[recon.UnitPriceColumn]]) * ParseAmount(sheet.Cells[row, columns[recon.QuantityColumn]]);
            var sku = skuCol > 0 ? sheet.Cells[row, skuCol].Text?.Trim() : null;
            var qtyText = sheet.Cells[row, columns[recon.QuantityColumn]].Text?.Trim();
            var skuLabel = $"{(string.IsNullOrEmpty(sku) ? "(SKU없음)" : sku)}×{qtyText}";

            if (orders.TryGetValue(orderId, out var existing))
            {
                existing.Skus.Add(skuLabel);
                orders[orderId] = (existing.FirstRow, existing.LineTotal + line, existing.Skus);
            }
            else
            {
                orders[orderId] = (row, line, new List<string> { skuLabel });
                order.Add(orderId);
            }
        }

        var fileName = Path.GetFileName(filePath);
        var result = new List<SalesOrderRecord>();
        foreach (var orderId in order)
        {
            var (row, lineTotal, skus) = orders[orderId];

            // 판매 트랙(주문 단위 모드)과 같은 계산: 주문 전체 금액 컬럼 − 차감 컬럼, 첫 줄 기준
            var paid = ParseAmount(sheet.Cells[row, columns[mapping.AmountColumn]]);
            foreach (var (header, factor) in subtracts)
                paid -= ParseAmount(sheet.Cells[row, columns[header]]) * factor;

            var sellerVoucher = string.IsNullOrWhiteSpace(recon.SellerVoucherColumn)
                ? 0m : ParseAmount(sheet.Cells[row, columns[recon.SellerVoucherColumn]]);
            var shipped = ParseDate(sheet.Cells[row, columns[recon.ShipDateColumn]])?.AddHours(recon.ShipDateOffsetHours);

            result.Add(new SalesOrderRecord(
                orderId, mapping.MarketCode, currency, fileName,
                ParseDate(sheet.Cells[row, columns[mapping.DateColumn]]),
                shipped, paid, lineTotal - sellerVoucher, string.Join(", ", skus)));
        }
        return result;
    }

    /// <summary>차감 컬럼 표기 "헤더" 또는 "헤더*배율"(예: 쇼피 브라질 코인 개수 → 금액 "Compensar Moedas Shopee*0.01").</summary>
    private static (string Header, decimal Factor) ParseSubtractColumn(string spec)
    {
        var star = spec.LastIndexOf('*');
        if (star > 0 && decimal.TryParse(spec[(star + 1)..].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var factor))
            return (spec[..star].Trim(), factor);
        return (spec.Trim(), 1m);
    }

    private static ExcelWorksheet RequireFirstSheet(ExcelPackage package)
    {
        return package.Workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("엑셀 파일에서 시트를 찾을 수 없습니다.");
    }

    private static Dictionary<string, int> ResolveColumns(ExcelWorksheet sheet, int headerRow, params string[] headers)
    {
        var map = new Dictionary<string, int>();
        var lastCol = sheet.Dimension?.End.Column ?? 0;

        for (var col = 1; col <= lastCol; col++)
        {
            var text = sheet.Cells[headerRow, col].Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            // 쇼피 베트남 파일은 헤더마다 유니코드 조합형(NFD)/완성형(NFC)이 섞여 있어(예: "Giá ưu đãi"가 NFD)
            // 눈으로는 같아도 문자열 비교가 실패한다. 양쪽을 NFC로 맞춰 비교한다.
            text = text.Normalize(NormalizationForm.FormC);

            foreach (var header in headers)
            {
                if (!map.ContainsKey(header) && string.Equals(text, header.Trim().Normalize(NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase))
                    map[header] = col;
            }
        }

        return map;
    }

    private static void RequireColumns(Dictionary<string, int> columns, params string[] headers)
    {
        var missing = headers.Where(h => !columns.ContainsKey(h)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"헤더 행에서 다음 컬럼을 찾지 못했습니다: {string.Join(", ", missing)}\n" +
                "export_summary_config.json의 컬럼명이 실제 파일 헤더와 일치하는지 확인하세요.");
        }
    }

    /// <summary>InvariantCulture 기본 파싱이 못 읽는 형식: 페이오니어 "30 Sep, 2026", 아마존 "Jun 1, 2026 12:49:34 AM PDT"(끝 시간대 약어 제거 후).</summary>
    private static readonly string[] ExtraDateFormats =
    [
        "d MMM, yyyy", "dd MMM, yyyy", "MMM d, yyyy h:mm:ss tt", "MMM d, yyyy", "yyyy/MM/dd", "yyyy/MM/dd HH:mm:ss",
    ];

    internal static DateTime? ParseDateText(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return parsed;
        if (DateTime.TryParseExact(text, ExtraDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed)) return parsed;

        // 끝의 시간대 약어(PDT, PST, JST 등)를 떼고 다시 시도
        var lastSpace = text.LastIndexOf(' ');
        if (lastSpace > 0 && text[(lastSpace + 1)..].All(char.IsLetter))
        {
            var trimmed = text[..lastSpace];
            if (DateTime.TryParseExact(trimmed, ExtraDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed)) return parsed;
            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) return parsed;
        }
        return null;
    }

    private static DateTime? ParseDate(ExcelRange cell)
    {
        if (cell.Value is DateTime dt) return dt;
        if (cell.Value is double serial && serial > 0)
        {
            try { return DateTime.FromOADate(serial); }
            catch (ArgumentException) { /* 날짜로 변환 불가한 숫자값 - 아래 문자열 파싱으로 계속 시도 */ }
        }

        return ParseDateText(cell.Text);
    }

    internal static decimal ParseAmountText(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return 0m;
        var cleaned = new string(text.Where(c => char.IsDigit(c) || c is '.' or '-').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0m;
    }

    private static decimal ParseAmount(ExcelRange cell)
    {
        switch (cell.Value)
        {
            case double d: return (decimal)d;
            case decimal dec: return dec;
            case int i: return i;
        }

        return ParseAmountText(cell.Text);
    }

    private static string ToYearMonth(DateTime date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}

using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// 거래처 마감보드(Forms/PartnerBulkOrderImportDialog)에서 OFS를 경유하지 않은 거래내역을
/// 엑셀로 한 번에 올릴 때 쓰는 파서. 고정 컬럼 순서가 아니라 1행 헤더명(매출일/수량/CSKU/단가)으로
/// 열을 찾는다(DataLoaders/ChannelBulkImportLoader.cs의 BuildHeaderMap 패턴과 동일) — 사용자가
/// 컬럼 순서를 바꿔도 되고, 다른 참고용 컬럼을 옆에 추가해도 무방하다.
/// </summary>
public static class PartnerBulkOrderLoader
{
    public const string SaleDateHeader = "매출일";
    public const string QtyHeader = "수량";
    public const string CskuHeader = "CSKU";
    public const string UnitPriceHeader = "단가";

    private static readonly string[] RequiredHeaders = [SaleDateHeader, QtyHeader, CskuHeader, UnitPriceHeader];

    public static List<PartnerBulkOrderRow> Load(string filePath)
    {
        using var package = ExcelFileOpener.Open(filePath);
        var sheet = package.Workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("엑셀 파일에 시트가 없습니다.");

        var headerMap = BuildHeaderMap(sheet);
        var missingHeaders = RequiredHeaders.Where(h => !headerMap.ContainsKey(h)).ToList();
        if (missingHeaders.Count > 0)
            throw new InvalidOperationException($"엑셀 1행에 다음 헤더가 없습니다: {string.Join(", ", missingHeaders)}");

        var rows = new List<PartnerBulkOrderRow>();
        var lastRow = sheet.Dimension?.End.Row ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            if (IsRowBlank(sheet, row, headerMap)) continue;
            rows.Add(ParseRow(sheet, row, headerMap));
        }
        return rows;
    }

    private static PartnerBulkOrderRow ParseRow(ExcelWorksheet sheet, int row, Dictionary<string, int> headerMap)
    {
        var errors = new List<string>();

        var saleDate = ParseDateCell(sheet, row, headerMap[SaleDateHeader], errors);
        var qty = ParseIntCell(sheet, row, headerMap[QtyHeader], errors);
        var cskuCode = GetText(sheet, row, headerMap[CskuHeader]);
        var unitPrice = ParseDecimalCell(sheet, row, headerMap[UnitPriceHeader], errors);

        if (string.IsNullOrEmpty(cskuCode)) errors.Add("CSKU 값이 없습니다.");
        if (qty <= 0) errors.Add("수량은 0보다 커야 합니다.");
        if (unitPrice < 0) errors.Add("단가는 0 이상이어야 합니다.");

        return new PartnerBulkOrderRow
        {
            RowNumber = row,
            SaleDate = saleDate,
            Qty = qty,
            CskuCode = cskuCode ?? "",
            UnitPrice = unitPrice,
            Errors = errors,
        };
    }

    private static Dictionary<string, int> BuildHeaderMap(ExcelWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        var lastCol = sheet.Dimension?.End.Column ?? 0;
        for (var col = 1; col <= lastCol; col++)
        {
            var header = sheet.Cells[1, col].Text?.Trim();
            if (!string.IsNullOrEmpty(header) && !map.ContainsKey(header)) map[header] = col;
        }
        return map;
    }

    private static bool IsRowBlank(ExcelWorksheet sheet, int row, Dictionary<string, int> headerMap)
    {
        foreach (var col in headerMap.Values)
        {
            if (!string.IsNullOrWhiteSpace(sheet.Cells[row, col].Text)) return false;
        }
        return true;
    }

    private static string? GetText(ExcelWorksheet sheet, int row, int col)
    {
        var text = sheet.Cells[row, col].Text;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static DateTime? ParseDateCell(ExcelWorksheet sheet, int row, int col, List<string> errors)
    {
        var rawValue = sheet.Cells[row, col].Value;
        switch (rawValue)
        {
            case DateTime dtv:
                return dtv;
            case double dv:
                return DateTime.FromOADate(dv);
            case null:
                errors.Add($"'{SaleDateHeader}' 값이 없습니다.");
                return null;
            default:
                var text = rawValue.ToString();
                if (DateTime.TryParse(text, out var parsed)) return parsed;
                errors.Add($"'{SaleDateHeader}' 값을 날짜로 읽을 수 없습니다: '{text}'");
                return null;
        }
    }

    private static int ParseIntCell(ExcelWorksheet sheet, int row, int col, List<string> errors)
    {
        var text = GetText(sheet, row, col);
        if (text == null)
        {
            errors.Add($"'{QtyHeader}' 값이 없습니다.");
            return 0;
        }
        if (int.TryParse(text, out var value)) return value;
        errors.Add($"'{QtyHeader}' 값이 숫자가 아닙니다: '{text}'");
        return 0;
    }

    private static decimal ParseDecimalCell(ExcelWorksheet sheet, int row, int col, List<string> errors)
    {
        var text = GetText(sheet, row, col);
        if (text == null)
        {
            errors.Add($"'{UnitPriceHeader}' 값이 없습니다.");
            return 0;
        }
        if (decimal.TryParse(text, out var value)) return value;
        errors.Add($"'{UnitPriceHeader}' 값이 숫자가 아닙니다: '{text}'");
        return 0;
    }
}

public class PartnerBulkOrderRow
{
    public int RowNumber { get; set; }
    public DateTime? SaleDate { get; set; }
    public int Qty { get; set; }
    public string CskuCode { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public List<string> Errors { get; set; } = [];
}

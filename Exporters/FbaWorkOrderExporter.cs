using System.Globalization;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace MiniERP2.Exporters;

/// <summary>
/// 아마존 FBA 발주 1건 또는 여러 건을 박스 포장용 작업지시서(피킹리스트)로 내보낸다. 아마존
/// 제출용이 아니라 순수 내부 포장 작업용이라 CSKU/상품명(내부관리용)/유통기한/박스별 수량/총계만
/// 담는다. 행 = CSKU 단위(같은 CSKU가 유통기한이 다른 배치로 여러 박스에 나뉘어도 한 행에 합산하고,
/// 유통기한은 참고용으로 쉼표 구분 나열 — 요청사항, 박스별 수량 자체는 유통기한 구분 없이 합산됨).
/// 열 = 박스. 여러 발주를 한 번에 낼 때는 발주번호로 박스 열을 묶어 상단에 병합 헤더를 추가하고,
/// 발주가 1건뿐이면 그 헤더 행은 생략한다. 맨 마지막 행은 박스별 합계, 데이터 범위 전체에는
/// 테두리를 두른다(요청사항).
/// </summary>
public static class FbaWorkOrderExporter
{
    public record OrderBoxSet(string FbaNo, List<FbaBox> Boxes, List<FbaBoxItem> Items);

    public static void Export(List<OrderBoxSet> orders, string filePath)
    {
        var ordered = orders.Where(o => o.Boxes.Count > 0).OrderBy(o => o.FbaNo, StringComparer.OrdinalIgnoreCase).ToList();

        var boxColumns = ordered
            .SelectMany(o => o.Boxes.OrderBy(b => b.BoxSeq).Select(b => (o.FbaNo, b.BoxSeq)))
            .ToList();

        var cskuRows = ordered.SelectMany(o => o.Items)
            .GroupBy(i => i.Csku, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Csku = g.Key,
                ItemName = g.Select(i => string.IsNullOrWhiteSpace(i.InvoiceDisplayName) ? i.ItemName : i.InvoiceDisplayName)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? string.Empty,
                ExpiryDates = g.Select(i => i.ExpiryDate)
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(e => NormalizeExpiryDate(e!))
                    .Distinct()
                    .ToList(),
                QtyByBox = g.GroupBy(i => (i.FbaNo, i.BoxSeq)).ToDictionary(bg => bg.Key, bg => bg.Sum(i => i.Qty)),
                Total = g.Sum(i => i.Qty),
            })
            .OrderBy(r => r.Csku, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("작업지시서");

        var showOrderHeader = ordered.Count > 1;
        var headerRow = showOrderHeader ? 2 : 1;
        const int firstBoxCol = 4;
        var totalCol = firstBoxCol + boxColumns.Count;

        if (showOrderHeader)
        {
            var col = firstBoxCol;
            foreach (var o in ordered)
            {
                sheet.Cells[1, col].Value = o.FbaNo;
                if (o.Boxes.Count > 1) sheet.Cells[1, col, 1, col + o.Boxes.Count - 1].Merge = true;
                col += o.Boxes.Count;
            }
            sheet.Cells[1, 1, 2, 1].Merge = true;
            sheet.Cells[1, 2, 2, 2].Merge = true;
            sheet.Cells[1, 3, 2, 3].Merge = true;
            sheet.Cells[1, totalCol, 2, totalCol].Merge = true;
        }

        sheet.Cells[headerRow, 1].Value = "CSKU";
        sheet.Cells[headerRow, 2].Value = "상품명(내부관리용)";
        sheet.Cells[headerRow, 3].Value = "유통기한";
        for (int i = 0; i < boxColumns.Count; i++)
            sheet.Cells[headerRow, firstBoxCol + i].Value = $"박스{boxColumns[i].BoxSeq}";
        sheet.Cells[headerRow, totalCol].Value = "총계";

        var boxTotals = new int[boxColumns.Count];
        var row = headerRow + 1;
        foreach (var r in cskuRows)
        {
            sheet.Cells[row, 1].Value = r.Csku;
            sheet.Cells[row, 2].Value = r.ItemName;
            sheet.Cells[row, 3].Value = string.Join(", ", r.ExpiryDates);
            for (int i = 0; i < boxColumns.Count; i++)
            {
                if (r.QtyByBox.TryGetValue(boxColumns[i], out var qty) && qty != 0)
                {
                    sheet.Cells[row, firstBoxCol + i].Value = qty;
                    boxTotals[i] += qty;
                }
            }
            sheet.Cells[row, totalCol].Value = r.Total;
            row++;
        }

        // 맨 마지막 행: 박스별 합계(요청사항). CSKU/상품명/유통기한 3열은 "합계" 라벨로 병합한다.
        sheet.Cells[row, 1].Value = "합계";
        sheet.Cells[row, 1, row, 3].Merge = true;
        for (int i = 0; i < boxColumns.Count; i++)
        {
            if (boxTotals[i] != 0) sheet.Cells[row, firstBoxCol + i].Value = boxTotals[i];
        }
        sheet.Cells[row, totalCol].Value = boxTotals.Sum();
        var lastRow = row;

        // 데이터가 있는 전체 범위(헤더~합계 행)에 테두리를 두른다(요청사항).
        var dataRange = sheet.Cells[1, 1, lastRow, totalCol];
        dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
        dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
        dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;

        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        ExportHelper.SaveExcel(package, filePath);
    }

    /// <summary>그리드 힌트는 "YYYYMMDD"지만 실제로는 사람이 "yyyy.MM.dd."처럼 점을 찍어 입력하는
    /// 경우가 흔해서(끝에 점이 붙는 경우 포함) 여러 형식을 순서대로 시도한다(FbaShipmentExporter와
    /// 동일). 내부 작업지시서라 파싱에 실패해도 값을 비우지 않고 입력된 원문을 그대로 보여준다.</summary>
    private static readonly string[] ExpiryDateFormats =
    [
        "yyyyMMdd", "yyyy.MM.dd", "yyyy.M.d", "yyyy-MM-dd", "yyyy/MM/dd",
    ];

    private static string NormalizeExpiryDate(string expiryDate)
    {
        var trimmed = expiryDate.Trim().TrimEnd('.');
        return DateTime.TryParseExact(trimmed, ExpiryDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd")
            : trimmed;
    }
}

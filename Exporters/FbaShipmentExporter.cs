using System.Globalization;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Exporters;

/// <summary>
/// 아마존 FBA 발주를 아마존 제출용 "선적명세"(shipment manifest) 양식으로 내보낸다(기획서 §6).
/// 행 = FbaBoxItem 그대로, 정렬은 carton no.(BoxSeq) → ItemSeq 순. carton/size는 박스 대표행(각
/// 박스의 첫 품목 행) 1개에만 값을 채우고 나머지 행은 진짜 빈 셀로 남긴다(carton은 "0"이 아니라
/// 공백이어야 함) — §6.2의 "미확정 1건": 박스 내 모든 행에 반복 출력해야 한다면 isFirstRowOfBox
/// 분기(9~12열, carton 열)만 걷어내면 된다. weight(kg/lb)는 박스 총무게가 아니라 CSKU 행별로
/// UnitWeightG×Qty를 계산해 모든 행에 채운다(요청사항 — 박스 총무게를 대표행에만 몰아주지 않고
/// CSKU별로 분할 배분). 반올림은 전부 사사오입(AwayFromZero)이다.
/// 맨 왼쪽 열(상품명(내부관리용))은 아마존 제출 양식에는 없는 열로, 사람이 행을 알아보기 위한
/// 용도이지 업로드용이 아니다 — InvoiceDisplayName(송장출력용 표시명, 비어있으면 ItemName)을 쓴다.
/// 맨 오른쪽 두 열(유통기한/MOCRA Listing No.)도 아마존 제출 양식에는 없는 내부관리용 열이다.
/// 유통기한은 발주 시 입력한 값(그리드 힌트는 YYYYMMDD이지만 실제로는 "yyyy.MM.dd."처럼 점으로
/// 구분해 입력한 값도 흔해 ExpiryDateFormats 여러 형식을 시도한다)을 "MMM.DD.YYYY.EXP." 형식으로
/// (예: 2026-08-05 → "AUG.05.2026.EXP.") 변환하고, 어떤 형식으로도 파싱할 수 없으면 빈 칸으로 둔다.
/// MOCRA Listing No.는 CSKU 마스터 값을 그대로 쓴다(품목별 값이라 cskus 인자로 조회한다).
/// </summary>
public static class FbaShipmentExporter
{
    private static readonly string[] Headers =
    [
        "상품명(내부관리용)",
        "ASIN", "Commodity Descriptions", "hs code", "carton no.", "carton", "unit", "qty",
        "item price", "weight (kg)", "weight (lb)", "size (cm)", "size (inch)",
        "유통기한", "MOCRA Listing No.",
    ];

    public static void Export(List<FbaBox> boxes, List<FbaBoxItem> items, List<FbaCskuModel> cskus, string filePath)
    {
        var boxesBySeq = boxes.ToDictionary(b => b.BoxSeq);
        var mocraByCsku = cskus.ToDictionary(c => c.Csku, c => c.MocraListingNo, StringComparer.OrdinalIgnoreCase);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("선적명세");

        for (int col = 0; col < Headers.Length; col++)
        {
            sheet.Cells[1, col + 1].Value = Headers[col];
        }

        var row = 2;
        foreach (var boxGroup in items.GroupBy(i => i.BoxSeq).OrderBy(g => g.Key))
        {
            if (!boxesBySeq.TryGetValue(boxGroup.Key, out var box)) continue;

            var orderedItems = boxGroup.OrderBy(i => i.ItemSeq).ToList();
            for (int i = 0; i < orderedItems.Count; i++)
            {
                var item = orderedItems[i];
                var isFirstRowOfBox = i == 0;

                var col = 1;
                var displayName = string.IsNullOrWhiteSpace(item.InvoiceDisplayName) ? item.ItemName : item.InvoiceDisplayName;
                sheet.Cells[row, col++].Value = displayName;
                sheet.Cells[row, col++].Value = item.Asin;
                sheet.Cells[row, col++].Value = item.CommodityDescription;
                sheet.Cells[row, col++].Value = item.HsCode;
                sheet.Cells[row, col++].Value = item.BoxSeq;
                if (isFirstRowOfBox) sheet.Cells[row, col].Value = 1; // 대표행만 "1", 나머지는 값 자체를 쓰지 않아 빈 셀로 남는다.
                col++;
                sheet.Cells[row, col++].Value = item.Qty; // unit
                sheet.Cells[row, col++].Value = item.Qty; // qty = unit × carton(=1) = unit과 동일
                sheet.Cells[row, col++].Value = Math.Round(item.ItemPrice, 2, MidpointRounding.AwayFromZero);

                var itemWeightKg = Math.Round(item.UnitWeightG * item.Qty / 1000.0, 1, MidpointRounding.AwayFromZero);
                var itemWeightLb = Math.Round(itemWeightKg * 2.20462, 2, MidpointRounding.AwayFromZero);
                sheet.Cells[row, col].Value = itemWeightKg;
                sheet.Cells[row, col + 1].Value = itemWeightLb;
                if (isFirstRowOfBox)
                {
                    sheet.Cells[row, col + 2].Value = FormatSize(box.WidthMm, box.DepthMm, box.HeightMm, cm: true);
                    sheet.Cells[row, col + 3].Value = FormatSize(box.WidthMm, box.DepthMm, box.HeightMm, cm: false);
                }
                col += 4;

                sheet.Cells[row, col++].Value = FormatExpiryDate(item.ExpiryDate);
                sheet.Cells[row, col].Value = mocraByCsku.GetValueOrDefault(item.Csku, string.Empty);

                row++;
            }
        }

        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        ExportHelper.SaveExcel(package, filePath);
    }

    /// <summary>그리드 힌트는 "YYYYMMDD"지만 실제로는 사람이 "yyyy.MM.dd."처럼 점을 찍어 입력하는
    /// 경우가 흔해서(끝에 점이 붙는 경우 포함) 여러 형식을 순서대로 시도한다.</summary>
    private static readonly string[] ExpiryDateFormats =
    [
        "yyyyMMdd", "yyyy.MM.dd", "yyyy.M.d", "yyyy-MM-dd", "yyyy/MM/dd",
    ];

    /// <summary>발주 시 입력한 유효기간 문자열을 "MMM.DD.YYYY.EXP." 형식으로 바꾼다(예:
    /// "20260805" 또는 "2026.08.05." → "AUG.05.2026.EXP."). 비어있거나 어떤 형식으로도 파싱되지
    /// 않으면 빈 문자열을 돌려준다.</summary>
    private static string FormatExpiryDate(string? expiryDate)
    {
        if (string.IsNullOrWhiteSpace(expiryDate)) return string.Empty;

        var trimmed = expiryDate.Trim().TrimEnd('.');
        if (!DateTime.TryParseExact(trimmed, ExpiryDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return string.Empty;
        }

        var month = date.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        return $"{month}.{date:dd}.{date:yyyy}.EXP.";
    }

    /// <summary>size (cm)는 mm÷10을 소수 1자리로, size (inch)는 cm÷2.54를 소수 2자리로 반올림한 뒤
    /// "가로*세로*높이" 형태로 결합한다(§6.1). 예: 500×280×280mm → "50.0*28.0*28.0"(cm),
    /// "19.69*11.02*11.02"(inch).</summary>
    private static string FormatSize(double widthMm, double depthMm, double heightMm, bool cm)
    {
        var wCm = Math.Round(widthMm / 10.0, 1, MidpointRounding.AwayFromZero);
        var dCm = Math.Round(depthMm / 10.0, 1, MidpointRounding.AwayFromZero);
        var hCm = Math.Round(heightMm / 10.0, 1, MidpointRounding.AwayFromZero);
        if (cm)
        {
            return string.Join("*", wCm.ToString("0.0", CultureInfo.InvariantCulture),
                dCm.ToString("0.0", CultureInfo.InvariantCulture), hCm.ToString("0.0", CultureInfo.InvariantCulture));
        }

        var wIn = Math.Round(wCm / 2.54, 2, MidpointRounding.AwayFromZero);
        var dIn = Math.Round(dCm / 2.54, 2, MidpointRounding.AwayFromZero);
        var hIn = Math.Round(hCm / 2.54, 2, MidpointRounding.AwayFromZero);
        return string.Join("*", wIn.ToString("0.00", CultureInfo.InvariantCulture),
            dIn.ToString("0.00", CultureInfo.InvariantCulture), hIn.ToString("0.00", CultureInfo.InvariantCulture));
    }
}

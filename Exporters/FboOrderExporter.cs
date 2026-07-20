using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Exporters;

/// <summary>
/// FBO(네이버 풀필먼트) 발주를 풀필먼트사(CJ대한통운) 제출용 "하배출고이서" 양식으로 내보낸다
/// (기획서 §3.1/§8.1). 행 = FboBoxItem 그대로(그룹핑 없음) — CourierExporter와 달리 이미 박스
/// 단위로 정리되어 있어 합포장 판단을 여기서 다시 할 필요가 없다.
/// </summary>
public static class FboOrderExporter
{
    private const string QuantityTemplate = "▶[##개]";

    private static readonly string[] Headers =
    [
        "반품부성명", "반품부전화번호", "반품부기타연락처", "반품부주소", "배송메세지1",
        "품목명", "반입수량", "이송구분", "박스타입", "기타1", "고객주문번호",
    ];

    public static void Export(FboOrder order, FboChannelConfigModel channel, List<FboBox> boxes,
        List<FboBoxItem> items, string filePath)
    {
        var boxesBySeq = boxes.ToDictionary(b => b.BoxSeq);
        // "고객주문번호"는 CJ 결과파일에서 박스를 되찾는 매칭키로 쓰지 않는다(사람이 직접 보고
        // 판단하는 칸이라는 사용자 결정 — FboTrackingImporter는 반품부명으로 매칭한다). 그래서
        // 박스마다 고유한 값 대신 채널을 사람이 알아볼 수 있는 라벨을 그대로 적는다.
        var customerOrderLabel = string.IsNullOrWhiteSpace(channel.ChannelLabel) ? channel.ChannelName : channel.ChannelLabel;

        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("하배출고이서");

        for (int col = 0; col < Headers.Length; col++)
        {
            sheet.Cells[1, col + 1].Value = Headers[col];
        }

        var row = 2;
        foreach (var item in items.OrderBy(i => i.BoxSeq).ThenBy(i => i.ItemSeq))
        {
            if (!boxesBySeq.TryGetValue(item.BoxSeq, out var box)) continue;

            var col = 1;
            sheet.Cells[row, col++].Value = box.ReceiverDisplayName;
            sheet.Cells[row, col++].Value = channel.Phone;
            sheet.Cells[row, col++].Value = string.Empty;
            sheet.Cells[row, col++].Value = channel.Address;
            sheet.Cells[row, col++].Value = string.Empty;
            // 택배시스템에 업로드되는 칸이라 내부 관리용 ItemName이 아니라 송장출력용 표시명을
            // 우선 쓴다(FboCskuMaster.InvoiceDisplayName, OFS의 ChannelSkuTable.InvoiceDisplayName과
            // 같은 목적). 등록해두지 않았으면 기존처럼 ItemName으로 대체된다. CSKU 코드는 붙이지
            // 않는다 — 채널코드_CSKU_상품명 형식으로 나오면 택배송장에 불필요하게 길어진다는
            // 지적에 따라 표시명+수량 조합만 남긴다.
            var displayName = string.IsNullOrWhiteSpace(item.InvoiceDisplayName) ? item.ItemName : item.InvoiceDisplayName;
            sheet.Cells[row, col++].Value = $"{displayName} {FormatQuantityTag(item.Qty)}";
            sheet.Cells[row, col++].Value = string.Empty;
            sheet.Cells[row, col++].Value = string.Empty;
            sheet.Cells[row, col++].Value = box.BoxType;
            sheet.Cells[row, col++].Value = channel.ChannelLabel;
            sheet.Cells[row, col++].Value = customerOrderLabel;
            row++;
        }

        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        ExportHelper.SaveExcel(package, filePath);
    }

    /// <summary>수량이 2개 이상이면 대괄호 바로 앞에 강조 표시를 붙여 합포장임을 한눈에 알아볼 수
    /// 있게 한다. 원래 수량만큼 '*'를 반복했으나(OFS와 같은 방식) 21개처럼 큰 수량에서 표시가
    /// 지나치게 길어진다는 지적에 따라 소문자 로마숫자(i=1, v=5, x=10, ...)로 축약했다.
    /// 예: 7개 → vii, 20개 → xx, 21개 → xxi.</summary>
    private static string FormatQuantityTag(int qty)
    {
        var effective = QuantityTemplate;
        if (qty >= 2)
        {
            var insertAt = effective.IndexOf('[');
            if (insertAt >= 0) effective = effective.Insert(insertAt, ToLowerRomanNumeral(qty));
        }
        return effective.Replace("##", qty.ToString());
    }

    private static readonly (int Value, string Numeral)[] RomanNumeralMap =
    [
        (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"),
        (100, "c"), (90, "xc"), (50, "l"), (40, "xl"),
        (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i"),
    ];

    private static string ToLowerRomanNumeral(int number)
    {
        var result = new System.Text.StringBuilder();
        foreach (var (value, numeral) in RomanNumeralMap)
        {
            while (number >= value)
            {
                result.Append(numeral);
                number -= value;
            }
        }
        return result.ToString();
    }
}

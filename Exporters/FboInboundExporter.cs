using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Exporters;

/// <summary>
/// FBO(네이버 풀필먼트) 발주의 이송장 확정 결과를 풀필먼트 시스템 "입고등록" 업로드 양식으로
/// 내보낸다(기획서 §3.2/§8.2). 헤더는 실제 업로드 양식 파일(입고등록_2.xlsx 1번 시트)과 문자 그대로
/// 맞춘다 — 이전엔 기억에 의존해 "이송장"/"검수단계"로 살짝 다르게 적었던 걸 실제 파일과 대조해
/// "운송장"/"검품단계"로 정정. 행 = FboBoxItem 그대로 — 합포장 박스는 같은 이송장번호(TrackingNo)가
/// 여러 행에 반복되는 게 의도된 동작이다(풀필먼트 재고 시스템이 이송장 기준으로 합포장을 인식).
/// TrackingNo가 없는 박스가 하나라도 있으면 호출 측(FboOrderForm)에서 먼저 차단해야 한다.
/// </summary>
public static class FboInboundExporter
{
    private static readonly string[] Headers =
    [
        "입고유형", "판매채널", "LOT속성7", "운송장(박스번호)", "고객LOT번호", "품목코드", "품목명",
        "입고예정수량", "유통기한", "생산일자", "비고", "검품단계", "옵션",
    ];

    public static void Export(FboChannelConfigModel channel, List<FboBox> boxes, List<FboBoxItem> items, string filePath)
    {
        var boxesBySeq = boxes.ToDictionary(b => b.BoxSeq);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("재고입고이서");

        for (int col = 0; col < Headers.Length; col++)
        {
            sheet.Cells[1, col + 1].Value = Headers[col];
        }

        var row = 2;
        foreach (var item in items.OrderBy(i => i.BoxSeq).ThenBy(i => i.ItemSeq))
        {
            if (!boxesBySeq.TryGetValue(item.BoxSeq, out var box)) continue;

            var col = 1;
            sheet.Cells[row, col++].Value = channel.InboundType;
            sheet.Cells[row, col++].Value = string.Empty;
            sheet.Cells[row, col++].Value = string.Empty;
            sheet.Cells[row, col++].Value = box.TrackingNo ?? string.Empty;
            sheet.Cells[row, col++].Value = string.Empty;
            sheet.Cells[row, col++].Value = item.FboItemCode;
            // 풀필먼트 시스템이 알아보기 쉽도록 CSKU 코드는 붙이지 않고 FBO 품목관리에 입력된
            // 품목명(ItemName)만 그대로 적는다(하배출고이서의 "품목명"과 달리 이쪽은 InvoiceDisplayName도
            // 쓰지 않는다 — 사용자 결정).
            sheet.Cells[row, col++].Value = item.ItemName;
            sheet.Cells[row, col++].Value = item.Qty;
            sheet.Cells[row, col++].Value = long.TryParse(item.ExpiryDate, out var expiry) ? expiry : (object?)item.ExpiryDate ?? string.Empty;
            sheet.Cells[row, col++].Value = string.Empty;
            sheet.Cells[row, col++].Value = string.Empty;
            sheet.Cells[row, col++].Value = string.Empty;
            sheet.Cells[row, col++].Value = string.Empty;
            row++;
        }

        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        ExportHelper.SaveExcel(package, filePath);
    }
}

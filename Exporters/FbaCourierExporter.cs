using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Exporters;

/// <summary>
/// 아마존 FBA 발주를 택배사(CJ) 제출용 "하배출고이서" 양식으로 내보낸다(기획서 §5).
/// FboOrderExporter와 동일한 11열 고정양식이지만, FBA는 수취지가 1곳 고정이라 반품부 관련 값은
/// 박스가 아니라 발주 헤더(저장 시점 FbaConfig 스냅샷)에서 오고, 고객주문번호는 사람이 읽는 라벨이
/// 아니라 §7.1의 박스 단위 매칭키(FbaBox.MatchKey) 그대로 나간다.
/// 반품부성명(수령인명)은 박스별로 번호를 붙여 구분한다(예: "수령인1", "수령인2") — 수취지가 전
/// 박스 공통 1곳이라 이름을 그대로 두면 택배시스템이 전 박스를 하나로 합포장해버린다. 전화번호/
/// 주소는 그대로 두고 이름만 박스마다 다르게 해야, 같은 박스 안 여러 품목 줄끼리는(이름·전화·주소
/// 모두 동일) 자동 합포장되면서 박스와 박스 사이는 섞이지 않는다.
/// </summary>
public static class FbaCourierExporter
{
    private static readonly string[] Headers =
    [
        "반품부성명", "반품부전화번호", "반품부기타연락처", "반품부주소", "배송메세지1",
        "품목명", "반입수량", "이송구분", "박스타입", "기타1", "고객주문번호",
    ];

    public static void Export(FbaOrder order, FbaConfigModel config, List<FbaBox> boxes, List<FbaBoxItem> items, string filePath)
    {
        var boxesBySeq = boxes.ToDictionary(b => b.BoxSeq);

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
            // 반품부성명/전화번호/주소는 저장 시점 스냅샷(FbaOrder)을 쓴다 — 발주지설정이 나중에
            // 바뀌어도 과거 발주 재출력값은 불변이어야 하기 때문이다(§3.4). 기타연락처/배송메세지/
            // 이송구분/박스타입/기타1은 스냅샷 컬럼이 없어 현재 FbaConfig 값을 그대로 쓴다.
            // 성명만 박스번호를 붙여 박스별로 다르게 만든다(위 클래스 설명 참고 — 박스 단위 합포장).
            sheet.Cells[row, col++].Value = $"{order.ReceiverName}{item.BoxSeq}";
            sheet.Cells[row, col++].Value = order.Phone;
            sheet.Cells[row, col++].Value = config.Phone2;
            sheet.Cells[row, col++].Value = order.Address;
            sheet.Cells[row, col++].Value = config.DeliveryMessage;
            var displayName = string.IsNullOrWhiteSpace(item.InvoiceDisplayName) ? item.ItemName : item.InvoiceDisplayName;
            sheet.Cells[row, col++].Value = $"{displayName} {QuantityTagFormatter.FormatQuantityTag(item.Qty)}";
            sheet.Cells[row, col++].Value = string.Empty;
            sheet.Cells[row, col++].Value = config.TransferType;
            sheet.Cells[row, col++].Value = config.BoxTypeLabel;
            sheet.Cells[row, col++].Value = config.Etc1;
            sheet.Cells[row, col++].Value = box.MatchKey;
            row++;
        }

        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        ExportHelper.SaveExcel(package, filePath);
    }
}

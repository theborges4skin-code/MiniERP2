using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Services;

/// <summary>
/// 마감/이익분석에서 내보낸 결과파일("…_이익분석_YYYYMMDD.xlsx")을 읽어 ProfitFact 행으로 바꾼다.
/// _META 시트의 channel_code/channel_name과 "분석요약(상품그룹별)" 시트(상품그룹·수량·매출액·배송비·순이익)를 쓴다.
/// 보고서 저장을 빠뜨린 채널이나, 배송비 컬럼이 생기기 전에 저장한 달을 결과파일로 다시 채우는 용도.
/// </summary>
public static class ProfitResultFileImporter
{
    public const string SummarySheetName = "분석요약(상품그룹별)";

    public record Result(string ChannelCode, string ChannelName, IReadOnlyList<ProfitFactRow> Facts, string FileName);

    public static Result Read(string filePath)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(filePath));
        var fileName = Path.GetFileName(filePath);

        var meta = package.Workbook.Worksheets["_META"]
            ?? throw new InvalidOperationException($"{fileName}: _META 시트가 없습니다. 마감/이익분석에서 내보낸 결과파일인지 확인하세요.");
        string? channelCode = null, channelName = null;
        for (int row = 1; row <= (meta.Dimension?.End.Row ?? 0); row++)
        {
            var key = meta.Cells[row, 1].Text?.Trim();
            var value = meta.Cells[row, 2].Text?.Trim();
            if (key == "channel_code") channelCode = value;
            if (key == "channel_name") channelName = value;
        }
        if (string.IsNullOrWhiteSpace(channelCode))
            throw new InvalidOperationException($"{fileName}: _META에 channel_code가 없습니다.");

        var sheet = package.Workbook.Worksheets[SummarySheetName]
            ?? throw new InvalidOperationException($"{fileName}: '{SummarySheetName}' 시트가 없습니다.");
        if (sheet.Cells[1, 1].Text?.Trim() != "상품그룹")
            throw new InvalidOperationException($"{fileName}: '{SummarySheetName}' 시트 A1이 '상품그룹'이 아닙니다.");

        int Find(string header)
        {
            for (int col = 1; col <= (sheet.Dimension?.End.Column ?? 0); col++)
                if (sheet.Cells[1, col].Text?.Trim() == header) return col;
            return 0;
        }
        var qtyCol = Find("수량");
        var revenueCol = Find("매출액");
        var shippingCol = Find("배송비");
        var profitCol = Find("순이익");
        if (qtyCol == 0 || revenueCol == 0 || profitCol == 0)
            throw new InvalidOperationException($"{fileName}: 수량/매출액/순이익 열을 찾지 못했습니다.");

        var facts = new List<ProfitFactRow>();
        for (int row = 2; row <= (sheet.Dimension?.End.Row ?? 1); row++)
        {
            var group = sheet.Cells[row, 1].Text?.Trim();
            if (string.IsNullOrEmpty(group)) break;
            if (group == "합계") break; // 아래는 "실제발송송장수" 같은 참고 행

            facts.Add(new ProfitFactRow
            {
                ProductGroup = group,
                Qty = (int)Math.Round(ReadDecimal(sheet.Cells[row, qtyCol])),
                Revenue = ReadDecimal(sheet.Cells[row, revenueCol]),
                GrossProfit = ReadDecimal(sheet.Cells[row, profitCol]),
                ShippingFee = shippingCol > 0 ? ReadDecimal(sheet.Cells[row, shippingCol]) : 0m,
            });
        }

        return new Result(channelCode!, string.IsNullOrWhiteSpace(channelName) ? channelCode! : channelName!, facts, fileName);
    }

    private static decimal ReadDecimal(ExcelRange cell) => cell.Value switch
    {
        double d => (decimal)d,
        decimal m => m,
        int i => i,
        long l => l,
        _ => decimal.TryParse(cell.Text?.Replace(",", "").Trim(), out var parsed) ? parsed : 0m,
    };
}

using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Services;

/// <summary>
/// 택배사에서 익월초에 내려받는 운송장이력 조회 파일(CJ "운송장이력 조회_*.xlsx": 순번/운송장번호/…/운임구분/운임)을
/// 읽어 운임별 출고 건수를 센다. 운송장번호가 빈 행(접수만 되고 송장이 나오지 않은 건)은 뺀다 — 이 규칙으로
/// 2026년 9월 파일 2개(당산 346행·포천 1,720행)를 읽으면 기존 수동 엑셀의 2,057건과 정확히 맞는다.
/// 운송장 파일 누락건 점검(TrackingBackfillViewer)의 라벨 필터와 무관하게 파일 전체를 센다.
/// </summary>
public static class FreightFileAggregator
{
    public record FileSummary(string FileName, int Counted, int SkippedNoTrackingNo, int SkippedNoFreight, decimal Amount);

    public record Result(IReadOnlyList<OnlineReportFreightTier> Tiers, IReadOnlyList<FileSummary> Files)
    {
        public int TotalCount => Tiers.Sum(t => t.Count);
        public decimal TotalAmount => Tiers.Sum(t => t.Rate * t.Count);
    }

    public static Result Aggregate(IEnumerable<string> filePaths)
    {
        ExcelLicense.Ensure();
        var counts = new Dictionary<decimal, int>();
        var files = new List<FileSummary>();
        foreach (var path in filePaths)
        {
            using var package = new ExcelPackage(new FileInfo(path));
            var sheet = package.Workbook.Worksheets.FirstOrDefault()
                ?? throw new InvalidOperationException($"{Path.GetFileName(path)}: 시트가 없습니다.");
            files.Add(AggregateSheet(sheet, Path.GetFileName(path), counts));
        }

        var tiers = counts.OrderBy(kv => kv.Key)
            .Select(kv => new OnlineReportFreightTier { Rate = kv.Key, Count = kv.Value })
            .ToList();
        return new Result(tiers, files);
    }

    public static FileSummary AggregateSheet(ExcelWorksheet sheet, string fileName, Dictionary<decimal, int> counts)
    {
        if (sheet.Dimension is null) throw new InvalidOperationException($"{fileName}: 데이터가 없습니다.");

        int headerRow = 0, trackingCol = 0, freightCol = 0;
        var lastScanRow = Math.Min(sheet.Dimension.End.Row, 10);
        for (int row = sheet.Dimension.Start.Row; row <= lastScanRow && headerRow == 0; row++)
        {
            int tracking = 0, freight = 0;
            for (int col = 1; col <= sheet.Dimension.End.Column; col++)
            {
                var text = sheet.Cells[row, col].Text?.Trim();
                if (text == "운송장번호" && tracking == 0) tracking = col;
                if (text == "운임" && freight == 0) freight = col;
            }
            if (tracking > 0 && freight > 0) (headerRow, trackingCol, freightCol) = (row, tracking, freight);
        }
        if (headerRow == 0)
            throw new InvalidOperationException($"{fileName}: '운송장번호'와 '운임' 머리글을 찾지 못했습니다. 택배사 운송장이력 조회 파일인지 확인하세요.");

        int counted = 0, noTracking = 0, noFreight = 0;
        decimal amount = 0m;
        for (int row = headerRow + 1; row <= sheet.Dimension.End.Row; row++)
        {
            var trackingNo = sheet.Cells[row, trackingCol].Text?.Trim();
            if (string.IsNullOrEmpty(trackingNo))
            {
                // 완전히 빈 행(파일 끝 공백)은 건너뛰기만 하고 세지 않는다.
                if (!string.IsNullOrWhiteSpace(sheet.Cells[row, freightCol].Text)) noTracking++;
                continue;
            }

            var rate = ReadDecimal(sheet.Cells[row, freightCol]);
            if (rate <= 0)
            {
                noFreight++;
                continue;
            }

            counts[rate] = counts.TryGetValue(rate, out var c) ? c + 1 : 1;
            counted++;
            amount += rate;
        }

        return new FileSummary(fileName, counted, noTracking, noFreight, amount);
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

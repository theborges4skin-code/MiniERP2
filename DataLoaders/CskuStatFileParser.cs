using MiniERP2.Models;
using OfficeOpenXml;

namespace MiniERP2.DataLoaders;

/// <summary>
/// "분석결과상세" 시트 파싱 결과. 시트/헤더 자체가 없으면 <see cref="ErrorMessage"/>만 채워지고
/// 파일 전체가 배치에서 제외된다(§9). 개별 행의 수치 파싱 실패는 <see cref="Warnings"/>에 쌓이고
/// 해당 행은 미매핑으로 강제 분류된다.
/// </summary>
public class CskuStatFileParseResult
{
    public bool Success => ErrorMessage == null;
    public string? ErrorMessage { get; set; }
    public List<CskuStatSourceRow> Rows { get; } = [];
    public List<string> Warnings { get; } = [];
}

/// <summary>
/// 마감/이익분석이 내보낸 엑셀의 "분석결과상세" 시트를 읽어 CSKU별 통계 집계 입력으로 변환한다
/// (CSKU별통계_개발기획서.md §1, §9 — S1).
/// </summary>
public static class CskuStatFileParser
{
    private const string SheetName = "분석결과상세";

    private static readonly string[] RequiredHeaders =
        ["채널", "상품그룹", "상품명", "옵션명", "매핑SKU", "수량", "매출액", "정산액", "배송비", "입출고비", "이익액", "상태"];

    public static CskuStatFileParseResult Parse(ExcelPackage package, string fileName, CskuFileKind fileKind)
    {
        var result = new CskuStatFileParseResult();

        var worksheet = package.Workbook.Worksheets[SheetName];
        if (worksheet?.Dimension == null)
        {
            result.ErrorMessage = $"'{SheetName}' 시트를 찾을 수 없습니다.";
            return result;
        }

        var columns = new Dictionary<string, int>();
        for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
        {
            var header = NormalizeHeader(worksheet.Cells[1, col].Value?.ToString());
            if (header.Length == 0 || columns.ContainsKey(header)) continue;
            columns[header] = col;
        }

        var headerCols = new Dictionary<string, int>();
        var missing = new List<string>();
        foreach (var h in RequiredHeaders)
        {
            if (columns.TryGetValue(h, out var col)) headerCols[h] = col;
            else missing.Add(h);
        }
        if (missing.Count > 0)
        {
            result.ErrorMessage = $"필수 헤더 누락: {string.Join(", ", missing)}";
            return result;
        }

        for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
        {
            var channelCode = CellText(worksheet, row, headerCols["채널"]);
            var status = CellText(worksheet, row, headerCols["상태"]);
            var cskuCode = CellText(worksheet, row, headerCols["매핑SKU"]);
            var productGroup = CellText(worksheet, row, headerCols["상품그룹"]);
            var productName = CellText(worksheet, row, headerCols["상품명"]);
            var optionName = CellText(worksheet, row, headerCols["옵션명"]);

            // 완전 공백 행(빈 줄) 건너뛰기.
            if (channelCode.Length == 0 && status.Length == 0 && cskuCode.Length == 0 &&
                productGroup.Length == 0 && productName.Length == 0)
            {
                continue;
            }

            var parseOk = true;
            var qty = TryParseInt(worksheet, row, headerCols["수량"], ref parseOk);
            var revenue = TryParseDecimal(worksheet, row, headerCols["매출액"], ref parseOk);
            var settlement = TryParseDecimal(worksheet, row, headerCols["정산액"], ref parseOk);
            var shipping = TryParseDecimal(worksheet, row, headerCols["배송비"], ref parseOk);
            var fee = TryParseDecimal(worksheet, row, headerCols["입출고비"], ref parseOk);
            var profit = TryParseDecimal(worksheet, row, headerCols["이익액"], ref parseOk);

            var sourceRow = new CskuStatSourceRow
            {
                FileName = fileName,
                FileKind = fileKind,
                ChannelCode = channelCode,
                ProductGroup = productGroup,
                ProductName = productName,
                OptionName = optionName,
                CskuCode = cskuCode,
                Qty = qty,
                Revenue = revenue,
                Settlement = settlement,
                Shipping = shipping,
                Fee = fee,
                Profit = profit,
                Status = status,
                RowClass = ClassifyStatus(status),
            };

            if (!parseOk)
            {
                sourceRow.RowClass = CskuStatRowClass.Unmapped;
                result.Warnings.Add($"{fileName} {row}행: 수치 열 파싱 실패 → 미매핑으로 처리");
            }

            result.Rows.Add(sourceRow);
        }

        return result;
    }

    /// <summary>§1.3 — 상태 문자열만으로 판정한다(매핑SKU 공백 여부로 판정하지 않음).</summary>
    private static CskuStatRowClass ClassifyStatus(string status) => status switch
    {
        "매핑(1:1)" or "매핑(조건)" or "매핑(임시)" or "매핑(예외)" => CskuStatRowClass.Normal,
        "제외(배송비 등)" => CskuStatRowClass.Excluded,
        "매핑 키 없음" or "매핑 실패" or "원가 정보 없음" => CskuStatRowClass.Unmapped,
        _ => CskuStatRowClass.Unmapped,
    };

    private static string CellText(ExcelWorksheet worksheet, int row, int col) =>
        worksheet.Cells[row, col].Value?.ToString()?.Trim() ?? string.Empty;

    private static int TryParseInt(ExcelWorksheet worksheet, int row, int col, ref bool parseOk)
    {
        var text = CellText(worksheet, row, col);
        if (text.Length == 0) return 0;
        if (decimal.TryParse(text, out var value)) return (int)value;
        parseOk = false;
        return 0;
    }

    private static decimal TryParseDecimal(ExcelWorksheet worksheet, int row, int col, ref bool parseOk)
    {
        var text = CellText(worksheet, row, col);
        if (text.Length == 0) return 0m;
        if (decimal.TryParse(text, out var value)) return value;
        parseOk = false;
        return 0m;
    }

    private static string NormalizeHeader(string? header) =>
        (header ?? string.Empty).Replace("\r\n", string.Empty).Replace("\n", string.Empty).Trim();
}

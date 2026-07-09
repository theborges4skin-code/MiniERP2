using System.Text.RegularExpressions;
using OfficeOpenXml;

namespace MiniERP2.Migration;

/// <summary>
/// 레거시 엑셀 거래명세표 시트를 좌표 하드코딩 없이 라벨 앵커로 파싱한다(거래명세표_DB이식_개발스펙.md §3).
/// DB에는 아무것도 쓰지 않는 순수 파싱 레이어 — 커밋(거래처 식별/중복처리)은 다음 단계.
/// </summary>
public static class TradeStatementSheetParser
{
    /// <summary>DataLoaders/SettlementLoader.cs와 동일한 가드 — 서식만 깔려있고 데이터가 없는 구간을 조기 종료.</summary>
    private const int MaxConsecutiveBlankRows = 200;

    private const int HeaderScanRowLimit = 20;
    private const int PartyBlockScanRowBelowAnchor = 8;
    private const int ValueScanColumnLimit = 15;

    public static ParsedStatementSheet Parse(ExcelWorksheet sheet, string sourceFileName)
    {
        var result = new ParsedStatementSheet { SourceFileName = sourceFileName, SourceSheetName = sheet.Name };
        ApplySheetNameFlags(sheet.Name, result.Flags);

        var dim = sheet.Dimension;
        if (dim == null)
        {
            result.Flags.Add("NOISE_EMPTY_SHEET");
            return result;
        }

        int maxCol = Math.Min(dim.End.Column, 70); // 실측상 최대 BQ(69)열까지 나오는 시트가 있어 넉넉히 잡음

        int? headerRow = FindHeaderRow(sheet, dim, maxCol);
        if (headerRow == null)
        {
            result.Flags.Add("NOISE_NO_HEADER");
            return result;
        }

        var cols = MapColumns(sheet, headerRow.Value, maxCol);
        result.TemplateSignature = BuildSignature(cols);

        result.Buyer = ExtractParty(sheet, headerRow.Value, maxCol, "공급받는자");
        if (result.Buyer == null)
            result.Flags.Add("NO_BUYER_BLOCK");
        else if (string.IsNullOrWhiteSpace(result.Buyer.CompanyName) && string.IsNullOrWhiteSpace(result.Buyer.RegNo))
            result.Flags.Add("NO_BUYER_IDENTITY"); // 예: "미국손님" — 등록번호/상호 모두 공란, 성명만 있는 경우

        ExtractLines(sheet, headerRow.Value, dim.End.Row, maxCol, cols, result);
        result.IssueDate = ResolveIssueDate(result.Lines, sheet.Name);

        return result;
    }

    // ── 헤더 행 탐지(§3.3/§1.4) ─────────────────────────────────────────
    private static int? FindHeaderRow(ExcelWorksheet sheet, ExcelRangeBase dim, int maxCol)
    {
        int maxRow = Math.Min(dim.End.Row, HeaderScanRowLimit);
        for (int r = 1; r <= maxRow; r++)
            for (int c = 1; c <= maxCol; c++)
                if (Norm(sheet.Cells[r, c].Text) == "품목")
                    return r;
        return null;
    }

    // ── 라벨 별칭 → 표준 필드 맵(§3.4) ───────────────────────────────────
    private sealed class ColumnMap
    {
        public int? Year, Month, Day, Item, Spec, Qty, UnitPrice, Supply, Tax, Total, Note;
        public bool UnitPriceVatIncluded;
    }

    private static ColumnMap MapColumns(ExcelWorksheet sheet, int headerRow, int maxCol)
    {
        var map = new ColumnMap();
        for (int c = 1; c <= maxCol; c++)
        {
            var norm = Norm(sheet.Cells[headerRow, c].Text);
            switch (norm)
            {
                case "연": map.Year ??= c; break;
                case "월": map.Month ??= c; break;
                case "일": map.Day ??= c; break;
                case "품목": map.Item ??= c; break;
                case "규격": map.Spec ??= c; break;
                case "수량": map.Qty ??= c; break;
                case "단가": map.UnitPrice ??= c; break;
                case "단가(VAT포함)": map.UnitPrice ??= c; map.UnitPriceVatIncluded = true; break;
                case "공급가액": map.Supply ??= c; break;
                case "세액": map.Tax ??= c; break;
                case "금액(VAT포함)":
                case "합계금액":
                case "금액": map.Total ??= c; break;
                case "비고": map.Note ??= c; break;
            }
        }
        return map;
    }

    /// <summary>연도 컬럼 유무 + 공급가액/세액 분리 여부로 템플릿유형 코드 결정(예 "Y-SEP", "N-INC").</summary>
    private static string BuildSignature(ColumnMap m)
    {
        string year = m.Year.HasValue ? "Y" : "N";
        string vat = m.Supply.HasValue && m.Tax.HasValue ? "SEP" : "INC";
        return $"{year}-{vat}";
    }

    // ── 거래처(공급받는자) 블록 탐지(§3.3) ────────────────────────────────
    private static ParsedPartyInfo? ExtractParty(ExcelWorksheet sheet, int headerRow, int maxCol, string anchorLabel)
    {
        int maxAnchorRow = Math.Min(headerRow - 1, HeaderScanRowLimit);
        (int row, int col)? anchor = null;
        for (int r = 1; r <= maxAnchorRow && anchor == null; r++)
        {
            for (int c = 1; c <= maxCol; c++)
            {
                if (Norm(sheet.Cells[r, c].Text) == anchorLabel) { anchor = (r, c); break; }
            }
        }
        if (anchor == null) return null;

        var party = new ParsedPartyInfo();
        int fromCol = anchor.Value.col; // 이보다 왼쪽은 공급자(자사) 쪽이므로 제외한다.
        int toRow = Math.Min(anchor.Value.row + PartyBlockScanRowBelowAnchor, headerRow - 1);

        for (int r = anchor.Value.row; r <= toRow; r++)
        {
            for (int c = fromCol; c <= maxCol; c++)
            {
                var norm = Norm(sheet.Cells[r, c].Text);
                if (norm.Length == 0) continue;

                if (norm == "등록번호") party.RegNo = FindValueRightOf(sheet, r, c, maxCol);
                else if (norm.StartsWith("상호")) party.CompanyName = FindValueRightOf(sheet, r, c, maxCol);
                else if (norm == "성명" || norm == "대표자") party.CeoName = FindValueRightOf(sheet, r, c, maxCol);
                else if (norm.StartsWith("사업장")) party.Address = FindValueRightOf(sheet, r, c, maxCol);
                else if (norm.StartsWith("업태")) party.BizType = FindValueRightOf(sheet, r, c, maxCol);
                else if (norm.StartsWith("종목")) party.BizItem = FindValueRightOf(sheet, r, c, maxCol);
            }
        }
        return party;
    }

    private static readonly string[] ReservedPartyLabelPrefixes =
        { "등록번호", "상호", "성명", "대표자", "사업장", "업태", "종목", "공급받는자", "공급자" };

    /// <summary>
    /// 라벨 셀 기준 오른쪽으로 스캔해 처음 나오는 비어있지 않은 셀 값을 반환한다(병합 폭이 라벨마다 달라 고정 오프셋 불가).
    /// 값 없이 바로 다음 라벨(예: 미국손님처럼 상호는 비어있고 성명만 있는 경우)을 만나면 값이 없는 것으로 본다.
    /// </summary>
    private static string FindValueRightOf(ExcelWorksheet sheet, int row, int labelCol, int maxCol)
    {
        int limit = Math.Min(labelCol + ValueScanColumnLimit, maxCol);
        for (int c = labelCol + 1; c <= limit; c++)
        {
            var text = sheet.Cells[row, c].Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            var norm = Norm(text);
            if (ReservedPartyLabelPrefixes.Any(p => norm.StartsWith(p))) return "";
            return text.Trim();
        }
        return "";
    }

    // ── 라인 추출(§3.5) + 총계행 대조(§3.8) ───────────────────────────────
    private static void ExtractLines(ExcelWorksheet sheet, int headerRow, int sheetMaxRow, int maxCol, ColumnMap cols, ParsedStatementSheet result)
    {
        int consecutiveBlank = 0;

        for (int r = headerRow + 1; r <= sheetMaxRow; r++)
        {
            bool isTotalsRow = false;
            for (int c = 1; c <= Math.Min(3, maxCol); c++)
            {
                var n = Norm(sheet.Cells[r, c].Text);
                if (n == "총계" || n == "합계" || n == "계") { isTotalsRow = true; break; }
            }

            if (isTotalsRow)
            {
                if (cols.Qty.HasValue) result.TotalsRowQty = ParseDecimal(sheet.Cells[r, cols.Qty.Value].Text);
                if (cols.Supply.HasValue) result.TotalsRowSupply = ParseDecimal(sheet.Cells[r, cols.Supply.Value].Text);
                if (cols.Tax.HasValue) result.TotalsRowTax = ParseDecimal(sheet.Cells[r, cols.Tax.Value].Text);
                if (cols.Total.HasValue) result.TotalsRowTotal = ParseDecimal(sheet.Cells[r, cols.Total.Value].Text);
                ReconcileTotals(result);
                return;
            }

            string itemName = cols.Item.HasValue ? sheet.Cells[r, cols.Item.Value].Text.Trim() : "";
            decimal rawSupply = cols.Supply.HasValue ? ParseDecimal(sheet.Cells[r, cols.Supply.Value].Text) : 0;
            decimal rawTotal = cols.Total.HasValue ? ParseDecimal(sheet.Cells[r, cols.Total.Value].Text) : 0;

            bool validLine = itemName.Length > 0 || rawSupply != 0 || rawTotal != 0;
            if (!validLine)
            {
                if (++consecutiveBlank >= MaxConsecutiveBlankRows) break;
                continue;
            }
            consecutiveBlank = 0;

            var line = new ParsedStatementLine
            {
                Year = cols.Year.HasValue ? (int)ParseDecimal(sheet.Cells[r, cols.Year.Value].Text) : 0,
                Month = cols.Month.HasValue ? (int)ParseDecimal(sheet.Cells[r, cols.Month.Value].Text) : 0,
                Day = cols.Day.HasValue ? (int)ParseDecimal(sheet.Cells[r, cols.Day.Value].Text) : 0,
                ItemName = itemName,
                Spec = cols.Spec.HasValue ? sheet.Cells[r, cols.Spec.Value].Text.Trim() : "",
                Qty = cols.Qty.HasValue ? ParseDecimal(sheet.Cells[r, cols.Qty.Value].Text) : 0,
                UnitPrice = cols.UnitPrice.HasValue ? ParseDecimal(sheet.Cells[r, cols.UnitPrice.Value].Text) : 0,
                UnitPriceVatIncluded = cols.UnitPriceVatIncluded,
                Note = cols.Note.HasValue ? sheet.Cells[r, cols.Note.Value].Text.Trim() : "",
            };

            decimal rawTax = cols.Tax.HasValue ? ParseDecimal(sheet.Cells[r, cols.Tax.Value].Text) : 0;
            ApplyVatDerivation(line, cols, rawSupply, rawTotal, rawTax);

            result.Lines.Add(line);
        }

        if (!result.TotalsRowTotal.HasValue && !result.TotalsRowSupply.HasValue)
            result.Flags.Add("NO_TOTALS_ROW");
    }

    /// <summary>스펙 §2.3 VAT 파생. 분리형은 그대로, 포함단일형(금액만 포함)은 10% 가정으로 역산.</summary>
    private static void ApplyVatDerivation(ParsedStatementLine line, ColumnMap cols, decimal rawSupply, decimal rawTotal, decimal rawTax)
    {
        if (cols.Supply.HasValue && cols.Tax.HasValue)
        {
            line.SupplyAmount = rawSupply;
            line.Tax = rawTax;
            line.Total = rawSupply + rawTax;
        }
        else
        {
            line.Total = rawTotal;
            line.SupplyAmount = Math.Round(rawTotal / 1.1m, 0, MidpointRounding.AwayFromZero);
            line.Tax = rawTotal - line.SupplyAmount;
        }
    }

    private static void ReconcileTotals(ParsedStatementSheet result)
    {
        decimal lineTotal = result.Lines.Sum(l => l.Total);
        decimal lineSupply = result.Lines.Sum(l => l.SupplyAmount);

        bool mismatch = result.TotalsRowTotal.HasValue
            ? Math.Abs(result.TotalsRowTotal.Value - lineTotal) > 1
            : result.TotalsRowSupply.HasValue && Math.Abs(result.TotalsRowSupply.Value - lineSupply) > 1;

        if (mismatch) result.Flags.Add("TOTALS_MISMATCH");
    }

    // ── 노이즈/사본/폐기 시트 플래그(§3.6, 차단 아님 표기만) ────────────────
    private static void ApplySheetNameFlags(string sheetName, List<string> flags)
    {
        if (sheetName == "Sheet1") flags.Add("NOISE_BLANK_SHEET_NAME");
        if (sheetName.Contains("안씀") || sheetName.Contains("가라") || sheetName.Contains("예정")) flags.Add("DISCARDED");
        if (Regex.IsMatch(sheetName, @"\(\d+\)$")) flags.Add("COPY_SUSPECTED");
    }

    // ── 발행일 보완(§3.7) ────────────────────────────────────────────────
    private static DateTime? ResolveIssueDate(List<ParsedStatementLine> lines, string sheetName)
    {
        var withYear = lines.FirstOrDefault(l => l.Year > 0 && l.Month > 0 && l.Day > 0);
        if (withYear != null)
        {
            try { return new DateTime(2000 + withYear.Year, withYear.Month, withYear.Day); }
            catch (ArgumentOutOfRangeException) { /* 잘못된 날짜(예 2/30) — 시트명 fallback으로 진행 */ }
        }

        var m = Regex.Match(sheetName, @"(?<!\d)(\d{6}|\d{4})(?!\d)");
        if (m.Success)
        {
            string digits = m.Value;
            try
            {
                return digits.Length == 6
                    ? new DateTime(2000 + int.Parse(digits[..2]), int.Parse(digits[2..4]), int.Parse(digits[4..6]))
                    : new DateTime(2000 + int.Parse(digits[..2]), int.Parse(digits[2..4]), 1); // YYMM만 있으면 일(日)은 1일로 보완
            }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        return null;
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────
    private static string Norm(string? text) =>
        text == null ? "" : new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static decimal ParseDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var cleaned = text.Replace(",", "").Replace("₩", "").Replace("원", "").Trim();
        return decimal.TryParse(cleaned, out var v) ? v : 0;
    }
}

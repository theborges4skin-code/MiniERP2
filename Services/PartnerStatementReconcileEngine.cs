using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MiniERP2.Models;
using OfficeOpenXml;

namespace MiniERP2.Services;

/// <summary>거래처 마감자료 1줄(VAT포함 단가로 환산해 둔다).</summary>
public class PartnerStatementLine
{
    public int RowNo { get; set; }
    public DateTime Date { get; set; }
    public string DateText { get; set; } = "";
    public string ItemName { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal UnitPriceVatIncluded { get; set; }
    public decimal AmountVatIncluded { get; set; }
    public bool IsShipping { get; set; }
    public string Memo { get; set; } = "";

    /// <summary>품명 → CSKU 해석 결과(저장된 연결 또는 자동 추정). 비어 있으면 품명 미연결.</summary>
    public string CskuCode { get; set; } = "";
    public bool CskuFromSavedMap { get; set; }
}

public class ReconcileRow
{
    public string Kind { get; set; } = "";
    public string PartnerDateText { get; set; } = "";
    public string OurDateText { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string PartnerItemName { get; set; } = "";
    public string OurItemName { get; set; } = "";
    public string CskuCode { get; set; } = "";
    public decimal Qty { get; set; }
    public decimal? PartnerUnitPrice { get; set; }
    public decimal? OurUnitPrice { get; set; }
    public decimal PartnerAmount { get; set; }
    public decimal OurAmount { get; set; }
    public long? OutboundDetailId { get; set; }
    public bool IsDifference => Kind != KindMatched;

    public const string KindMatched = "일치";
    public const string KindPriceDiff = "단가차이";
    public const string KindPartnerOnly = "거래처만";
    public const string KindOursOnly = "우리만";
    public const string KindUnmapped = "품명 미연결";
}

public class ReconcileResult
{
    public List<ReconcileRow> Rows { get; } = [];
    public decimal PartnerQty { get; set; }
    public decimal PartnerAmount { get; set; }
    public decimal OurQty { get; set; }
    public decimal OurAmount { get; set; }
    public decimal PartnerShippingCount { get; set; }
    public decimal PartnerShippingAmount { get; set; }
    public int OurShipmentCount { get; set; }
    public decimal OurShippingLineQty { get; set; }
}

/// <summary>
/// 거래처 마감자료 대조. 거래처가 보낸 마감자료(예: 투유 '구매현황내역' — 일자/품명/수량/단가/공급가액/
/// 부가세/합계, 날마다 '배송비(3000)' 행)를 읽어, 같은 마감월 출고이력과 CSKU × 수량 단위로 짝짓는다.
/// 거래처 일자는 주문일이고 우리 출고일은 며칠 늦을 수 있어(몰아서 출고확정 등) 출고일이 거래처 일자
/// −1일 ~ +10일이면 같은 건으로 본다. 금액은 모두 VAT포함으로 비교한다(출고이력 단가 기준).
/// </summary>
public static class PartnerStatementReconcileEngine
{
    private static readonly string[] DateHeaders = ["일자", "날짜", "주문일", "주문일자", "출고일"];
    private static readonly string[] NameHeaders = ["품명 및 규격", "품명", "품목명", "품목", "상품명"];
    private static readonly string[] QtyHeaders = ["수량"];
    private static readonly string[] UnitPriceHeaders = ["단가"];
    private static readonly string[] SupplyHeaders = ["공급가액", "공급가"];
    private static readonly string[] VatHeaders = ["부가세", "세액"];
    private static readonly string[] TotalHeaders = ["합 계", "합계", "합계금액", "금액"];
    private static readonly string[] MemoHeaders = ["적요", "비고"];

    public const int DaysBefore = 1;
    public const int DaysAfter = 10;

    public sealed record ParseResult(List<PartnerStatementLine> Lines, string? Error);

    /// <summary>첫 시트에서 '수량'과 품명 헤더가 함께 있는 행을 헤더로 찾아 읽는다(앞쪽 20행 안).</summary>
    public static ParseResult Parse(ExcelWorksheet sheet, string period)
    {
        if (sheet.Dimension == null) return new([], "빈 시트입니다.");
        var year = int.Parse(period[..4], CultureInfo.InvariantCulture);
        var month = int.Parse(period[5..7], CultureInfo.InvariantCulture);

        int headerRow = 0;
        Dictionary<string, int> cols = [];
        for (int r = 1; r <= Math.Min(20, sheet.Dimension.End.Row) && headerRow == 0; r++)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int c = 1; c <= sheet.Dimension.End.Column; c++)
            {
                var t = Regex.Replace(sheet.Cells[r, c].Text ?? "", @"\s+", " ").Trim();
                if (t.Length > 0 && !map.ContainsKey(t)) map[t] = c;
            }
            if (Find(map, QtyHeaders) != null && Find(map, NameHeaders) != null) { headerRow = r; cols = map; }
        }
        if (headerRow == 0) return new([], "헤더 행('수량'과 '품명/품목' 열)을 찾지 못했습니다.");

        int? dateCol = Find(cols, DateHeaders), nameCol = Find(cols, NameHeaders), qtyCol = Find(cols, QtyHeaders),
            unitCol = Find(cols, UnitPriceHeaders), supplyCol = Find(cols, SupplyHeaders), vatCol = Find(cols, VatHeaders),
            totalCol = Find(cols, TotalHeaders), memoCol = Find(cols, MemoHeaders);

        var lines = new List<PartnerStatementLine>();
        DateTime? lastDate = null;
        for (int r = headerRow + 1; r <= sheet.Dimension.End.Row; r++)
        {
            var name = nameCol is { } nc ? sheet.Cells[r, nc].Text.Trim() : "";
            var dateText = dateCol is { } dc ? sheet.Cells[r, dc].Text.Trim() : "";
            if (name.Length == 0 || dateText.Contains('계') || name.Contains("합계")) continue;

            var qty = Number(sheet, r, qtyCol);
            if (qty == 0) continue;

            var date = ParseDate(sheet, r, dateCol, year, month) ?? lastDate;
            if (date == null) continue;
            lastDate = date;

            var total = Number(sheet, r, totalCol);
            if (total == 0) total = Number(sheet, r, supplyCol) + Number(sheet, r, vatCol);
            if (total == 0) total = Number(sheet, r, unitCol) * qty * 1.1m;

            lines.Add(new PartnerStatementLine
            {
                RowNo = r, Date = date.Value, DateText = dateText, ItemName = name, Qty = qty,
                AmountVatIncluded = total, UnitPriceVatIncluded = Math.Round(total / qty, 2),
                IsShipping = Regex.IsMatch(name, "배송비|택배비|운임"),
                Memo = memoCol is { } mc ? sheet.Cells[r, mc].Text.Trim() : "",
            });
        }
        return new(lines, null);
    }

    private static DateTime? ParseDate(ExcelWorksheet sheet, int r, int? col, int year, int month)
    {
        if (col is not { } c) return null;
        if (sheet.Cells[r, c].Value is DateTime dt) return dt.Date;
        var m = Regex.Match(sheet.Cells[r, c].Text ?? "", @"(?:(\d{4})[./-])?(\d{1,2})[./-](\d{1,2})");
        if (!m.Success) return null;
        var y = m.Groups[1].Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : year;
        var mo = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var d = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        if (!m.Groups[1].Success && mo == 12 && month == 1) y--; // 1월 마감에 12월 일자가 섞인 경우
        try { return new DateTime(y, mo, d); } catch { return null; }
    }

    private static int? Find(Dictionary<string, int> map, string[] candidates)
    {
        foreach (var c in candidates) if (map.TryGetValue(c, out var col)) return col;
        return null;
    }

    private static decimal Number(ExcelWorksheet sheet, int r, int? col)
    {
        if (col is not { } c) return 0m;
        return sheet.Cells[r, c].Value switch
        {
            double d => (decimal)d,
            int i => i,
            decimal m => m,
            _ => decimal.TryParse(sheet.Cells[r, c].Text?.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m,
        };
    }

    // ── 품명 → CSKU 자동 추정 ───────────────────────────────────────────────

    /// <summary>
    /// 거래처 품명을 우리 품명들(CSKU 송장출력명·출고이력 품목명)과 비교해 CSKU를 추정한다. 숫자(1 제외)와
    /// 영문 토큰(BR/PH/ML 등) 집합이 같아야 하고, 그중 한글 글자 2-gram 유사도가 가장 높은 하나를 고른다.
    /// 최고점이 서로 다른 CSKU로 갈리거나 0.5 미만이면 판단하지 않는다(null) — 사용자가 한 번 연결해 저장한다.
    /// </summary>
    public static string? GuessCsku(string partnerName, IReadOnlyList<(string Name, string CskuCode)> known)
    {
        var target = Features(partnerName);
        var scored = known
            .Where(k => k.CskuCode.Length > 0)
            .Select(k => (k.CskuCode, F: Features(k.Name)))
            .Where(k => k.F.Numbers.SetEquals(target.Numbers) && k.F.Latin.SetEquals(target.Latin))
            .Select(k => (k.CskuCode, Score: Dice(target.Bigrams, k.F.Bigrams)))
            .Where(k => k.Score >= 0.5)
            .ToList();
        if (scored.Count == 0) return null;
        var best = scored.Max(s => s.Score);
        var top = scored.Where(s => s.Score >= best - 0.0001).Select(s => s.CskuCode).Distinct().ToList();
        return top.Count == 1 ? top[0] : null;
    }

    private sealed record NameFeatures(HashSet<string> Numbers, HashSet<string> Latin, List<string> Bigrams);

    private static NameFeatures Features(string name)
    {
        var upper = (name ?? "").ToUpperInvariant();
        var numbers = Regex.Matches(upper, @"\d+").Select(m => m.Value.TrimStart('0')).Where(n => n.Length > 0 && n != "1").ToHashSet();
        var latin = Regex.Matches(upper, "[A-Z]+").Select(m => m.Value).ToHashSet();
        var sb = new StringBuilder();
        foreach (var ch in upper) if (ch >= '가' && ch <= '힣') sb.Append(ch);
        var hangul = sb.ToString().Replace("향", "").Replace("타입", "");
        var bigrams = new List<string>();
        for (int i = 0; i + 1 < hangul.Length; i++) bigrams.Add(hangul.Substring(i, 2));
        return new NameFeatures(numbers, latin, bigrams);
    }

    private static double Dice(List<string> a, List<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var pool = b.ToList();
        int common = 0;
        foreach (var x in a) { var i = pool.IndexOf(x); if (i >= 0) { common++; pool.RemoveAt(i); } }
        return 2.0 * common / (a.Count + b.Count);
    }

    // ── 대조 ──────────────────────────────────────────────────────────────

    /// <param name="ourLines">마감월 출고이력(출고확정). 배송비 CSKU 라인은 isShippingCsku로 구분한다.</param>
    public static ReconcileResult Reconcile(IReadOnlyList<PartnerStatementLine> partnerLines, IReadOnlyList<OutboundDetail> ourLines,
        Func<string, bool> isShippingCsku)
    {
        var result = new ReconcileResult();
        static string CskuOf(OutboundDetail d) => string.IsNullOrWhiteSpace(d.CskuCode) ? d.MskuCode : d.CskuCode!;

        var partnerItems = partnerLines.Where(l => !l.IsShipping).ToList();
        var ourItems = ourLines.Where(d => !isShippingCsku(CskuOf(d))).ToList();

        result.PartnerQty = partnerItems.Sum(l => l.Qty);
        result.PartnerAmount = partnerItems.Sum(l => l.AmountVatIncluded);
        result.PartnerShippingCount = partnerLines.Where(l => l.IsShipping).Sum(l => l.Qty);
        result.PartnerShippingAmount = partnerLines.Where(l => l.IsShipping).Sum(l => l.AmountVatIncluded);
        result.OurQty = ourItems.Sum(d => d.Qty);
        result.OurAmount = ourItems.Sum(d => d.Qty * d.SupplyPrice);
        result.OurShipmentCount = ourItems.Select(d => string.IsNullOrWhiteSpace(d.ShipmentGroupKey) ? $"#{d.Id}" : d.ShipmentGroupKey).Distinct().Count();
        result.OurShippingLineQty = ourLines.Where(d => isShippingCsku(CskuOf(d))).Sum(d => d.Qty);

        // 우리 쪽 남은 수량(Id별).
        var ourRemaining = ourItems.ToDictionary(d => d.Id, d => (decimal)d.Qty);
        var ourByCsku = ourItems.GroupBy(CskuOf).ToDictionary(g => g.Key, g => g.OrderBy(d => d.ConfirmedAt).ThenBy(d => d.Id).ToList());

        // 날짜 차이가 가장 작은 (거래처 줄, 우리 줄) 쌍부터 수량을 배정한다 — 같은 품목이 여러 날에 걸쳐
        // 있을 때 "먼저 나온 거래처 줄이 가까운 우리 줄을 가로채는" 밀림을 막기 위함.
        var mapped = partnerItems.Where(p => p.CskuCode.Length > 0).ToList();
        var partnerRemaining = mapped.ToDictionary(p => p, p => p.Qty);
        var pairs = new List<(PartnerStatementLine P, OutboundDetail D, int Distance)>();
        foreach (var p in mapped)
        {
            if (!ourByCsku.TryGetValue(p.CskuCode, out var candidates)) continue;
            foreach (var d in candidates)
            {
                if (d.ConfirmedAt?.Date is not { } shipped) continue;
                if (shipped < p.Date.AddDays(-DaysBefore) || shipped > p.Date.AddDays(DaysAfter)) continue;
                pairs.Add((p, d, Math.Abs((shipped - p.Date).Days)));
            }
        }

        var matchedRows = new List<(PartnerStatementLine P, ReconcileRow Row)>();
        foreach (var (p, d, _) in pairs.OrderBy(x => x.Distance).ThenBy(x => x.P.Date).ThenBy(x => x.P.RowNo).ThenBy(x => x.D.Id))
        {
            var take = Math.Min(partnerRemaining[p], ourRemaining[d.Id]);
            if (take <= 0) continue;
            partnerRemaining[p] -= take;
            ourRemaining[d.Id] -= take;

            var priceDiff = Math.Abs(p.UnitPriceVatIncluded - d.SupplyPrice) > Math.Max(1m, d.SupplyPrice * 0.01m);
            matchedRows.Add((p, new ReconcileRow
            {
                Kind = priceDiff ? ReconcileRow.KindPriceDiff : ReconcileRow.KindMatched,
                PartnerDateText = p.Date.ToString("MM-dd"), OurDateText = d.ConfirmedAt!.Value.ToString("MM-dd"),
                Recipient = d.Recipient, PartnerItemName = p.ItemName, OurItemName = d.ProductName, CskuCode = p.CskuCode,
                Qty = take, PartnerUnitPrice = p.UnitPriceVatIncluded, OurUnitPrice = d.SupplyPrice,
                PartnerAmount = take * p.UnitPriceVatIncluded, OurAmount = take * d.SupplyPrice, OutboundDetailId = d.Id,
            }));
        }

        foreach (var p in partnerItems.OrderBy(l => l.Date).ThenBy(l => l.RowNo))
        {
            if (p.CskuCode.Length == 0)
            {
                result.Rows.Add(new ReconcileRow
                {
                    Kind = ReconcileRow.KindUnmapped, PartnerDateText = p.Date.ToString("MM-dd"), PartnerItemName = p.ItemName,
                    Qty = p.Qty, PartnerUnitPrice = p.UnitPriceVatIncluded, PartnerAmount = p.AmountVatIncluded,
                });
                continue;
            }

            result.Rows.AddRange(matchedRows.Where(m => m.P == p).Select(m => m.Row));
            if (partnerRemaining[p] > 0)
                result.Rows.Add(new ReconcileRow
                {
                    Kind = ReconcileRow.KindPartnerOnly, PartnerDateText = p.Date.ToString("MM-dd"), PartnerItemName = p.ItemName,
                    CskuCode = p.CskuCode, Qty = partnerRemaining[p], PartnerUnitPrice = p.UnitPriceVatIncluded,
                    PartnerAmount = partnerRemaining[p] * p.UnitPriceVatIncluded,
                });
        }

        foreach (var d in ourItems.Where(d => ourRemaining[d.Id] > 0).OrderBy(d => d.ConfirmedAt))
        {
            var left = ourRemaining[d.Id];
            result.Rows.Add(new ReconcileRow
            {
                Kind = ReconcileRow.KindOursOnly, OurDateText = d.ConfirmedAt?.ToString("MM-dd") ?? "", Recipient = d.Recipient,
                OurItemName = d.ProductName, CskuCode = CskuOf(d), Qty = left, OurUnitPrice = d.SupplyPrice,
                OurAmount = left * d.SupplyPrice, OutboundDetailId = d.Id,
            });
        }

        return result;
    }
}

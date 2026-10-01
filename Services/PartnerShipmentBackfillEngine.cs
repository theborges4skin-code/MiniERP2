using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MiniERP2.Models;

namespace MiniERP2.Services;

/// <summary>발주서 파일에서 읽은 주문 1줄(주문일은 파일명 앞 yyyyMMdd — 거래처 마감자료의 일자와 같은 기준).</summary>
public sealed record BackfillOrderLine(
    DateTime OrderDate, string Recipient, string ProductName, string OptionName, int Qty,
    string? MappedCsku, string Address, string Phone, string SourceFileName);

/// <summary>보충(출고 등록) 후보 1줄. 운송장 1건에 발주서 품목이 여럿이면 같은 송장으로 여러 줄이 된다.</summary>
public class BackfillCandidate
{
    public bool Selected { get; set; } = true;
    public DateTime? ReceivedAt { get; set; }
    public string TrackingNo { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string CourierProductName { get; set; } = "";
    public DateTime? OrderDate { get; set; }
    public string OrderProductName { get; set; } = "";
    public int Qty { get; set; } = 1;
    public string CskuCode { get; set; } = "";
    public decimal SupplyPrice { get; set; }

    /// <summary>판정 — 발주서 일치 / 운송장만(품목명 추정) / 이력 있음(미출고) / 주문 이미 출고됨(중복 송장?) 등.</summary>
    public string MatchKind { get; set; } = "";
    public string SourceFile { get; set; } = "";

    /// <summary>송장이 이미 이 채널 이력에 미출고(발주확정 등) 상태로 있으면 그 행 Id — 신규 등록 대신 출고확정 처리.</summary>
    public List<long> ExistingDetailIds { get; set; } = [];
    public bool IsConfirmExisting => ExistingDetailIds.Count > 0;

    public string ReceivedAtText => ReceivedAt?.ToString("yyyy-MM-dd") ?? "";
    public string OrderDateText => OrderDate?.ToString("yyyy-MM-dd") ?? "";
    public bool NeedsCsku => string.IsNullOrWhiteSpace(CskuCode);
}

public class BackfillAnalysis
{
    public List<BackfillCandidate> Candidates { get; } = [];

    /// <summary>발주서에는 있는데 출고이력에도 운송장에도 없는 주문(출고 확인 필요).</summary>
    public List<BackfillOrderLine> UnshippedOrders { get; } = [];

    public int CourierRowsInPeriod { get; set; }
    public int UnregisteredInPeriod { get; set; }
}

/// <summary>
/// 거래처 출고 보충(후처리) — OFS를 거치지 않고 처리된 거래처 발주를 운송장 결과 파일 + 발주서 폴더로
/// 찾아 출고이력에 보충한다. 거래처 마감자료 없이도 돌릴 수 있다.
/// 1) 마감월에 접수된 운송장 중 출고이력에 없는 송장을 고르고,
/// 2) 수령인으로 발주서 주문과 연결해(주문일 ≤ 접수일+1, 14일 이내 가장 가까운 날) 품목·수량·CSKU(채널
///    매핑규칙 결과)를 가져온다. 발주서가 없으면 운송장 메모에 채널명이 있는 건만 채널 건으로 보고,
///    운송장 품목명을 CSKU 송장출력명/과거 출고 품목명과 대조해 CSKU를 추정한다.
/// 3) 반대로 발주서에는 있는데 출고이력·운송장 어디에도 수령인이 없는 주문은 따로 알려준다.
/// </summary>
public static class PartnerShipmentBackfillEngine
{
    private const int MaxOrderLagDays = 14;

    /// <param name="registeredByTracking">운송장번호로 찾은 기존 이력 행(채널·상태 무관).</param>
    /// <param name="channelHistory">이 채널의 마감월 전후 이력(중복 송장·발주서만 있음 판정용).</param>
    /// <param name="knownProductNames">CSKU 추정용 (품목명, CSKU) 목록 — 채널 CSKU의 송장출력명과 과거 출고이력 품목명.</param>
    public static BackfillAnalysis Analyze(
        string period, string channelCode, string channelName,
        IReadOnlyList<TrackingBackfillRow> courierRows,
        IReadOnlyList<BackfillOrderLine> orders,
        IReadOnlyList<OutboundDetail> registeredByTracking,
        IReadOnlyList<OutboundDetail> channelHistory,
        IReadOnlyList<(string Name, string CskuCode)> knownProductNames,
        Func<string, decimal> supplyPriceOf)
    {
        var result = new BackfillAnalysis();
        var (from, to) = PeriodRange(period);
        var hint = ChannelHint(channelName);

        var ordersByRecipient = orders
            .Where(o => o.Recipient.Length > 0)
            .GroupBy(o => NormalizeName(o.Recipient))
            .ToDictionary(g => g.Key, g => g.ToList());

        var inPeriod = courierRows.Where(r => r.ReceivedAt is { } d && d >= from && d < to).ToList();
        result.CourierRowsInPeriod = inPeriod.Count;


        var registered = registeredByTracking
            .GroupBy(d => d.TrackingNo.Trim())
            .ToDictionary(g => g.Key, g => g.ToList());

        var unregistered = new List<TrackingBackfillRow>();
        foreach (var row in inPeriod.GroupBy(r => r.TrackingNo).Select(g => g.First()).OrderBy(r => r.ReceivedAt))
        {
            if (!registered.TryGetValue(row.TrackingNo, out var existing)) { unregistered.Add(row); continue; }

            // 이 채널 이력에 있으나 아직 출고확정 전(발주확정 등) — 운송장 접수일로 출고확정 처리 대상.
            var pending = existing.Where(d => d.ChannelCode == channelCode && d.Status != "출고확정").ToList();
            if (pending.Count == 0) continue;
            result.Candidates.Add(new BackfillCandidate
            {
                ReceivedAt = row.ReceivedAt!.Value.Date, TrackingNo = row.TrackingNo, Recipient = row.Recipient,
                CourierProductName = row.ProductName,
                OrderProductName = string.Join(", ", pending.Select(d => d.ProductName).Distinct()),
                Qty = pending.Sum(d => d.Qty), CskuCode = string.Join(", ", pending.Select(d => string.IsNullOrWhiteSpace(d.CskuCode) ? d.MskuCode : d.CskuCode).Distinct()),
                SupplyPrice = pending[0].SupplyPrice, ExistingDetailIds = pending.Select(d => d.Id).ToList(),
                MatchKind = $"이력 있음({pending[0].Status}) → 출고확정", SourceFile = row.SourceFileName,
            });
        }
        result.UnregisteredInPeriod = unregistered.Count;

        var shippedHistory = channelHistory.Where(d => d.Status == "출고확정" && d.ConfirmedAt != null).ToList();

        foreach (var row in unregistered)
        {
            var received = row.ReceivedAt!.Value.Date;
            var matchedOrders = ordersByRecipient.TryGetValue(NormalizeName(row.Recipient), out var list)
                ? NearestOrderDay(list, received)
                : [];

            if (matchedOrders.Count > 0)
            {
                // 같은 수령인·주문일 이후 3주 안에 이미 출고확정된 건이 있으면, 이 송장은 재발행/중복일 가능성이 크다.
                var orderDate = matchedOrders[0].OrderDate;
                var alreadyShipped = shippedHistory.Any(d => NormalizeName(d.Recipient) == NormalizeName(row.Recipient)
                    && d.ConfirmedAt!.Value.Date >= orderDate && d.ConfirmedAt.Value.Date <= orderDate.AddDays(21));

                foreach (var o in matchedOrders)
                {
                    var csku = o.MappedCsku
                        ?? GuessCsku($"{o.ProductName} {o.OptionName}", knownProductNames)
                        ?? GuessCsku(o.ProductName, knownProductNames)
                        ?? "";
                    var kind = alreadyShipped ? "주문 이미 출고됨(중복 송장?)"
                        : o.MappedCsku != null ? "발주서 일치"
                        : csku.Length > 0 ? "발주서 일치(품목명 추정)" : "발주서 일치(CSKU 지정 필요)";
                    result.Candidates.Add(new BackfillCandidate
                    {
                        Selected = !alreadyShipped,
                        ReceivedAt = received, TrackingNo = row.TrackingNo, Recipient = row.Recipient,
                        Address = string.IsNullOrWhiteSpace(o.Address) ? row.Address : o.Address, Phone = o.Phone,
                        CourierProductName = row.ProductName, OrderDate = o.OrderDate,
                        OrderProductName = string.IsNullOrWhiteSpace(o.OptionName) ? o.ProductName : $"{o.ProductName} / {o.OptionName}",
                        Qty = o.Qty <= 0 ? 1 : o.Qty, CskuCode = csku, SupplyPrice = csku.Length > 0 ? supplyPriceOf(csku) : 0m,
                        MatchKind = kind, SourceFile = o.SourceFileName,
                    });
                }
                continue;
            }

            // 발주서가 없는 건은 운송장 메모에 채널명이 있을 때만 이 채널 건으로 본다(다른 채널 미등록 송장 배제).
            if (hint.Length == 0 || !NormalizeName(row.OrderNoMemo).Contains(hint)) continue;

            var guessed = GuessCsku(row.ProductName, knownProductNames);
            result.Candidates.Add(new BackfillCandidate
            {
                ReceivedAt = received, TrackingNo = row.TrackingNo, Recipient = row.Recipient, Address = row.Address,
                CourierProductName = row.ProductName, Qty = ParseCourierQty(row.ProductName),
                CskuCode = guessed ?? "", SupplyPrice = guessed != null ? supplyPriceOf(guessed) : 0m,
                MatchKind = guessed != null ? "운송장만(품목명 추정)" : "운송장만(CSKU 지정 필요)", SourceFile = row.SourceFileName,
            });
        }

        // 발주서에만 있는 주문: 출고이력·운송장(기간 무관) 어디에도 수령인이 없음.
        var shippedNames = new HashSet<string>(channelHistory.Select(d => NormalizeName(d.Recipient)));
        foreach (var r in courierRows) shippedNames.Add(NormalizeName(r.Recipient));
        foreach (var o in orders.Where(o => o.OrderDate >= from && o.OrderDate < to && o.Recipient.Length > 0))
        {
            if (!shippedNames.Contains(NormalizeName(o.Recipient))) result.UnshippedOrders.Add(o);
        }

        return result;
    }

    /// <summary>수령인의 주문 중 접수일 기준 가장 가까운(이전 또는 다음날까지) 주문일의 주문 줄 전부.</summary>
    private static List<BackfillOrderLine> NearestOrderDay(List<BackfillOrderLine> orders, DateTime received)
    {
        var eligible = orders
            .Where(o => o.OrderDate <= received.AddDays(1) && o.OrderDate >= received.AddDays(-MaxOrderLagDays))
            .ToList();
        if (eligible.Count == 0) return [];
        var best = eligible.MinBy(o => Math.Abs((received - o.OrderDate).TotalDays))!.OrderDate;
        return eligible.Where(o => o.OrderDate == best).ToList();
    }

    /// <summary>
    /// 운송장 품목명은 송장출력명의 괄호·+ 등이 "_"로 바뀌고 끝에 "___N개_"가 붙은 형태다. 글자·숫자만
    /// 남겨 비교하며, 후보 이름이 운송장 품목명의 부분수열(순서 유지, 사이 글자 허용 — 예: 사은품 "컵")이면
    /// 일치로 보고 가장 긴 후보를 고른다. 같은 길이에 서로 다른 CSKU가 걸리면 판단하지 않는다(null).
    /// </summary>
    public static string? GuessCsku(string courierProductName, IReadOnlyList<(string Name, string CskuCode)> known)
    {
        var target = Compact(StripQtySuffix(courierProductName));
        if (target.Length == 0) return null;

        var matches = known
            .Select(k => (Key: Compact(k.Name), k.CskuCode))
            .Where(k => k.Key.Length >= 4 && k.CskuCode.Length > 0 && IsSubsequence(k.Key, target))
            .ToList();
        if (matches.Count == 0) return null;

        var longest = matches.Max(m => m.Key.Length);
        var top = matches.Where(m => m.Key.Length == longest).Select(m => m.CskuCode).Distinct().ToList();
        return top.Count == 1 ? top[0] : null;
    }

    public static int ParseCourierQty(string courierProductName)
    {
        var m = Regex.Match(courierProductName ?? "", @"_{2,}\s*(\d+)\s*개_*\s*$");
        return m.Success && int.TryParse(m.Groups[1].Value, out var q) && q > 0 ? q : 1;
    }

    private static string StripQtySuffix(string name) => Regex.Replace(name ?? "", @"_{2,}\s*\d+\s*개_*\s*$", "");

    private static string Compact(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToUpperInvariant())
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    private static bool IsSubsequence(string needle, string haystack)
    {
        int i = 0;
        foreach (var ch in haystack)
            if (i < needle.Length && needle[i] == ch) i++;
        return i == needle.Length;
    }

    public static string NormalizeName(string? s) => Regex.Replace(s ?? "", @"\s+", "").Trim();

    /// <summary>채널명에서 "(주)/주식회사" 등을 빼고 공백 제거 — 운송장 메모("(주)투유", "투유")와 대조용.</summary>
    public static string ChannelHint(string channelName) =>
        NormalizeName(Regex.Replace(channelName ?? "", @"\(주\)|주식회사|㈜", ""));

    public static (DateTime From, DateTime To) PeriodRange(string period)
    {
        var from = DateTime.ParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture);
        return (from, from.AddMonths(1));
    }

    /// <summary>발주서 파일명 앞 8자리 yyyyMMdd. 없으면 null.</summary>
    public static DateTime? FileOrderDate(string fileName)
    {
        var m = Regex.Match(Path.GetFileName(fileName), @"^(\d{8})");
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}

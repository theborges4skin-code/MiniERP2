using MiniERP2.Models;

namespace MiniERP2.Services;

/// <summary>CSKU별 통계의 "MSKU별 합산" 보기 1행 — 채널·거래처 구분 없이 같은 MSKU를 합친 결과(원화).</summary>
public class CskuStatMskuRow
{
    public string Msku { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int OnlineQty { get; init; }
    public int PartnerQty { get; init; }
    public int Qty => OnlineQty + PartnerQty;
    public decimal OnlineRevenue { get; init; }
    public decimal PartnerRevenue { get; init; }
    public decimal Revenue => OnlineRevenue + PartnerRevenue;
    public decimal OnlineProfit { get; init; }
    public decimal PartnerProfit { get; init; }
    public decimal Profit => OnlineProfit + PartnerProfit;
    public decimal? MarginRate => Revenue == 0 ? null : Profit / Revenue;
    public int ChannelCount { get; init; }
    public string Channels { get; init; } = string.Empty;
    public string CskuCodes { get; init; } = string.Empty;
}

/// <summary>
/// 채널·CSKU 단위 집계 결과를 MSKU 단위로 다시 합친다 — 같은 선물세트가 채널마다 CSKU가 달라도
/// 한 줄로 모아 온라인(일반·로켓그로스·아마존)과 거래처 판매를 함께 보기 위함이다. 아마존 금액은
/// 환율로 원화 환산한다. MSKU가 비어 있는 행은 "(MSKU없음) CSKU"로 따로 남겨 조용히 섞이지 않게 한다.
/// </summary>
public static class CskuStatMskuSummarizer
{
    public static List<CskuStatMskuRow> Summarize(IEnumerable<CskuStatLine> lines, decimal amazonExchangeRate)
    {
        decimal Krw(CskuStatLine l, decimal v) => l.FileKind == CskuFileKind.Amazon ? v * amazonExchangeRate : v;

        return lines
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Msku) ? "(MSKU없음) " + l.CskuCode : l.Msku.Trim(), StringComparer.Ordinal)
            .Select(g =>
            {
                var items = g.ToList();
                var online = items.Where(l => l.FileKind != CskuFileKind.Partner).ToList();
                var partner = items.Where(l => l.FileKind == CskuFileKind.Partner).ToList();
                var representative = items.OrderByDescending(l => Krw(l, l.Revenue)).First();
                var channels = items.Select(l => l.FileKind == CskuFileKind.Partner ? $"{l.ChannelName}(거래처)" : l.ChannelName)
                    .Distinct(StringComparer.Ordinal).ToList();
                return new CskuStatMskuRow
                {
                    Msku = g.Key,
                    ProductName = string.IsNullOrWhiteSpace(representative.InvoiceDisplayName) ? representative.ProductName : representative.InvoiceDisplayName,
                    OnlineQty = online.Sum(l => l.Qty),
                    PartnerQty = partner.Sum(l => l.Qty),
                    OnlineRevenue = online.Sum(l => Krw(l, l.Revenue)),
                    PartnerRevenue = partner.Sum(l => l.Revenue),
                    OnlineProfit = online.Sum(l => Krw(l, l.Profit)),
                    PartnerProfit = partner.Sum(l => l.Profit),
                    ChannelCount = channels.Count,
                    Channels = string.Join(", ", channels),
                    CskuCodes = string.Join(", ", items.Select(l => l.CskuCode).Distinct(StringComparer.Ordinal)),
                };
            })
            .OrderByDescending(r => r.Profit)
            .ThenBy(r => r.Msku, StringComparer.Ordinal)
            .ToList();
    }
}

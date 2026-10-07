using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Services;

/// <summary>
/// 거래처에 따로 청구하는 배송비를 출고이력(OutboundDetailTable)의 "배송비 라인"으로 다루는 공용 규칙.
/// 배송비 라인 = 그 채널의 배송비 CSKU(마스터SKU 'shipping')로 넣은 출고 라인이다. 거래처 마감보드는
/// 출고이력을 그대로 집계하므로 이 라인이 있으면 마감금액에 자동으로 들어가고, 원가는 매입가
/// (PurchasePrice) = 청구액으로 넣어 이익 0으로 잡는다(배송비는 그대로 비용으로 나갔다고 본다).
/// OFS 발주확정(송장별 청구 토글), 마감보드 [배송비 추가], 운송장 누락건 점검의 채널별 송장 대조가
/// 모두 이 규칙을 공유한다.
/// </summary>
public class ShippingFeeLineService
{
    public const string ShippingMsku = "shipping";

    private readonly ChannelSkuRepository _channelSkuRepo;

    public ShippingFeeLineService(ChannelSkuRepository? channelSkuRepo = null)
    {
        _channelSkuRepo = channelSkuRepo ?? new ChannelSkuRepository();
    }

    /// <summary>채널의 배송비 CSKU 코드 목록.</summary>
    public HashSet<string> GetShippingCskuCodes(string channelCode) =>
        _channelSkuRepo.GetAllByChannel(channelCode)
            .Where(c => c.Msku == ShippingMsku)
            .Select(c => c.CskuCode)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>전 채널의 배송비 CSKU 키("채널코드\u0001CSKU") — 여러 채널 라인을 한 번에 가를 때.</summary>
    public HashSet<string> GetAllShippingKeys() =>
        _channelSkuRepo.GetAllByMsku(ShippingMsku)
            .Select(c => Key(c.ChannelCode, c.CskuCode))
            .ToHashSet(StringComparer.Ordinal);

    public static string Key(string channelCode, string cskuCode) => $"{channelCode}\u0001{cskuCode}";

    /// <summary>출고 라인이 배송비 라인인지(<see cref="GetAllShippingKeys"/> 결과로 판정).</summary>
    public static bool IsShippingLine(OutboundDetail d, HashSet<string> shippingKeys) =>
        shippingKeys.Contains(Key(d.ChannelCode, string.IsNullOrWhiteSpace(d.CskuCode) ? d.MskuCode : d.CskuCode));

    /// <summary>
    /// 채널의 배송비 CSKU를 돌려준다. 없으면 "{이름}_ship"(마스터SKU 'shipping', 송장표시명 '배송비')으로
    /// 새로 만든다 — 마감보드 [배송비 추가]가 쓰던 규칙 그대로.
    /// </summary>
    public ChannelSkuModel EnsureShippingCsku(string channelCode, string nameForNewCode, decimal defaultPrice, string reason)
    {
        var existing = _channelSkuRepo.GetAllByChannel(channelCode).FirstOrDefault(c => c.Msku == ShippingMsku);
        if (existing != null) return existing;

        var csku = new ChannelSkuModel
        {
            ChannelCode = channelCode, CskuCode = $"{nameForNewCode}_ship", Msku = ShippingMsku,
            SupplyPrice = defaultPrice, InvoiceDisplayName = "배송비",
        };
        _channelSkuRepo.Upsert(csku, reason);
        return csku;
    }

    public static string DisplayName(ChannelSkuModel csku) =>
        string.IsNullOrWhiteSpace(csku.InvoiceDisplayName) ? "배송비" : csku.InvoiceDisplayName!;

    /// <summary>"3000,4500" 형식의 청구액 선택지를 읽는다(숫자 아닌 항목·0 이하는 버림, 중복 제거).</summary>
    public static List<decimal> ParsePresets(string? presets) =>
        (presets ?? string.Empty)
            .Split([',', '/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => decimal.TryParse(s.Replace("원", ""), out var v) ? v : 0m)
            .Where(v => v > 0)
            .Distinct()
            .ToList();
}

using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// "실제발송송장수" 계산. 정산파일에 송장번호(TrackingNo)가 매핑되어 있으면 중복을 제거한 개수를
/// 쓰고, 송장번호가 전혀 없는 채널(쿠팡 등 정산파일에 송장번호 열이 없는 경우)은 배송비 총계를
/// 3,000원으로 나눈 값으로 추정한다.
/// </summary>
public static class ShipmentCountEstimator
{
    public const decimal AssumedCostPerShipment = 3000m;

    public static (int Count, bool IsEstimated) Compute(IEnumerable<SettlementData> rows)
    {
        var list = rows as IReadOnlyCollection<SettlementData> ?? rows.ToList();

        var distinctTrackingCount = list
            .Select(d => d.TrackingNo)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (distinctTrackingCount > 0) return (distinctTrackingCount, false);

        var totalShipping = list.Sum(d => d.Shipping);
        var estimated = totalShipping == 0 ? 0 : (int)Math.Round(totalShipping / AssumedCostPerShipment, MidpointRounding.AwayFromZero);
        return (estimated, true);
    }
}

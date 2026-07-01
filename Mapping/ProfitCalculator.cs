using MiniERP2.Models;

namespace MiniERP2.Mapping;

/// <summary>
/// 기획서 5.6절 '채널별 손익 공식'에 따라 정산 1건의 이익액을 계산합니다.
/// </summary>
public static class ProfitCalculator
{
    /// <summary>
    /// 채널 유형별 공식에 따라 이익액을 계산합니다.
    /// </summary>
    /// <param name="channelType">채널 유형</param>
    /// <param name="settlement">정산액</param>
    /// <param name="costPrice">제조원가(마스터SKU 기준, VAT 포함)</param>
    /// <param name="qty">수량</param>
    /// <param name="shipping">배송비(쿠팡그로스의 그로스배송비 등, VAT 별도금액)</param>
    /// <param name="fee">입출고비 등 부가 수수료(VAT 별도금액)</param>
    /// <param name="exchangeRate">환율(아마존 등 외화 채널에만 적용, 그 외 1)</param>
    public static decimal Calculate(ChannelType channelType, decimal settlement, decimal costPrice, int qty, decimal shipping, decimal fee, decimal exchangeRate = 1m)
    {
        const decimal vatRate = 1.1m;

        return channelType switch
        {
            ChannelType.CoupangGrowth =>
                settlement - (costPrice * qty) - (shipping * vatRate) - (fee * vatRate),

            ChannelType.AmazonUs or ChannelType.AmazonJp =>
                (settlement - (costPrice / vatRate * qty)) * exchangeRate,

            _ => settlement - (costPrice * qty),
        };
    }

    /// <summary>
    /// 11번가 채널의 특수 규칙: 수량=0인 행(배송비 전용 행)을 제거하고 매출액을 보정합니다.
    /// <para>
    /// 11번가 정산파일은 매출액(Revenue)에 배송비가 중복 포함되므로, 상품 행마다
    /// Revenue -= Shipping 보정을 적용합니다. 수량=0인 순수 배송비 행은 목록에서 제거합니다.
    /// </para>
    /// 채널 유형이 11번가가 아니면 아무 동작도 하지 않습니다.
    /// </summary>
    public static void ApplyElevenStreetFilter(ChannelType channelType, List<SettlementData> rows)
    {
        if (channelType != ChannelType.ElevenStreet || rows.Count == 0) return;

        rows.RemoveAll(r => r.Qty == 0);

        foreach (var row in rows)
            row.Revenue -= row.Shipping;
    }

    /// <summary>
    /// 쿠팡일반 채널의 특수 규칙: 수량=0인 행(배송비 전용 행)을 처리합니다.
    /// <para>
    /// OrderNo가 매핑된 경우 — 주문번호 단위로 처리합니다.
    /// 수량=0인 행의 매출액(Revenue)을 해당 주문의 총 배송비로 간주하고,
    /// 같은 주문의 상품 행 수로 나눠 균등 분배한 뒤 수량=0 행을 목록에서 제거합니다.
    /// </para>
    /// <para>
    /// OrderNo 미매핑인 경우 — 기존 동작 유지: 전체 Shipping을 합산하여 첫 행에만 표기합니다.
    /// </para>
    /// 채널 유형이 쿠팡일반이 아니면 아무 동작도 하지 않습니다.
    /// </summary>
    public static void ApplyCoupangGeneralShippingAggregation(ChannelType channelType, List<SettlementData> rows)
    {
        if (channelType != ChannelType.CoupangGeneral || rows.Count == 0) return;

        bool hasOrderNo = rows.Any(r => !string.IsNullOrEmpty(r.OrderNo));
        if (hasOrderNo)
        {
            var shippingRows = rows.Where(r => r.Qty == 0).ToList();
            var productRows = rows.Where(r => r.Qty != 0).ToList();

            // 주문번호별 배송비 합산 (Qty=0 행의 매출액이 배송비)
            var shippingByOrder = shippingRows
                .Where(r => !string.IsNullOrEmpty(r.OrderNo))
                .GroupBy(r => r.OrderNo!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Revenue), StringComparer.OrdinalIgnoreCase);

            // 주문번호별 상품 행 수
            var productCountByOrder = productRows
                .Where(r => !string.IsNullOrEmpty(r.OrderNo))
                .GroupBy(r => r.OrderNo!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var row in productRows)
            {
                if (!string.IsNullOrEmpty(row.OrderNo) &&
                    shippingByOrder.TryGetValue(row.OrderNo, out var totalShipping) &&
                    productCountByOrder.TryGetValue(row.OrderNo, out var count) && count > 0)
                {
                    row.Shipping = totalShipping / count;
                }
            }

            rows.Clear();
            rows.AddRange(productRows);
            return;
        }

        // OrderNo 미매핑: 기존 동작 (전체 합산 → 첫 행)
        var total = rows.Sum(r => r.Shipping);
        for (int i = 1; i < rows.Count; i++)
            rows[i].Shipping = 0m;
        rows[0].Shipping = total;
    }
}

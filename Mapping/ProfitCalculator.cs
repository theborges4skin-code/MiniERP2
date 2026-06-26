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
    /// 쿠팡일반 채널의 특수 규칙: 매핑성공/미매핑/예외처리 전체 행의 배송비를 합산하여
    /// 결과의 첫 행에만 표기하고, 나머지 행은 0으로 둡니다(배송비 이중집계 방지).
    /// 채널 유형이 쿠팡일반이 아니면 아무 동작도 하지 않습니다.
    /// </summary>
    public static void ApplyCoupangGeneralShippingAggregation(ChannelType channelType, List<SettlementData> rows)
    {
        if (channelType != ChannelType.CoupangGeneral || rows.Count == 0) return;

        var totalShipping = rows.Sum(r => r.Shipping);
        for (int i = 1; i < rows.Count; i++)
        {
            rows[i].Shipping = 0m;
        }
        rows[0].Shipping = totalShipping;
    }
}

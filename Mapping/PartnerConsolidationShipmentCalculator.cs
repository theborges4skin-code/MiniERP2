using MiniERP2.Models;

namespace MiniERP2.Mapping;

/// <summary>
/// 온라인 거래처 취합(OnlinePartnerConsolidation_Spec.md §6.3) — 채널별 배송건수 산정 및
/// 거래처 단위 배송비 청구액 계산. 마감/이익분석의 ShipmentCountEstimator(반올림)는 건드리지
/// 않는다(D12) — 이 화면 전용 계산이며 소수점은 내림(D11)이다.
/// </summary>
public static class PartnerConsolidationShipmentCalculator
{
    /// <summary>
    /// 채널 1개의 배송건수. 송장번호(TrackingNo)가 1건 이상 있으면 공백 제외·대소문자 무시
    /// Distinct 개수를 쓰고, 전무하면 배송비 총액 ÷ shippingFeePerShipment를 내림한다.
    /// </summary>
    public static PartnerConsolidationChannelShipment ComputeChannel(
        string companyName, string channelCode, string channelName,
        IReadOnlyList<string> trackingNumbers, decimal shippingTotal, decimal shippingFeePerShipment)
    {
        var distinctCount = trackingNumbers
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToUpperInvariant())
            .Distinct()
            .Count();

        var isEstimated = distinctCount == 0;
        var shipmentCount = isEstimated
            ? (shippingFeePerShipment > 0 ? (int)Math.Floor(shippingTotal / shippingFeePerShipment) : 0)
            : distinctCount;

        return new PartnerConsolidationChannelShipment
        {
            CompanyName = companyName,
            ChannelCode = channelCode,
            ChannelName = channelName,
            ShipmentCount = shipmentCount,
            IsEstimated = isEstimated,
            ShippingTotal = shippingTotal,
        };
    }

    /// <summary>거래처 배송건수 = 소속 채널 건수의 단순 합. 배송비 청구액 = 배송건수 × billingRatePerShipment.</summary>
    public static (int ShipmentCount, decimal ShippingFeeTotal) ComputeCompanyBilling(
        IReadOnlyList<PartnerConsolidationChannelShipment> channelResults, decimal billingRatePerShipment)
    {
        var shipmentCount = channelResults.Sum(c => c.ShipmentCount);
        return (shipmentCount, shipmentCount * billingRatePerShipment);
    }
}

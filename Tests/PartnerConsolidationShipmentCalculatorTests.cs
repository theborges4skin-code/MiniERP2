using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerConsolidationShipmentCalculatorTests
{
    [TestMethod]
    public void ComputeChannel_TrackingNumbersPresent_UsesDistinctCaseInsensitiveCount()
    {
        var tracking = new List<string> { "abc123", "ABC123", " def456 ", "def456", "" };

        var result = PartnerConsolidationShipmentCalculator.ComputeChannel(
            "펩투나", "CH1", "쿠팡일반", tracking, shippingTotal: 999999m, shippingFeePerShipment: 3000m);

        Assert.AreEqual(2, result.ShipmentCount);
        Assert.IsFalse(result.IsEstimated);
    }

    [TestMethod]
    public void ComputeChannel_NoTrackingNumbers_FallsBackToFlooredEstimate()
    {
        var result = PartnerConsolidationShipmentCalculator.ComputeChannel(
            "펩투나", "CH1", "쿠팡일반", [], shippingTotal: 9999m, shippingFeePerShipment: 3000m);

        // 9999 / 3000 = 3.33 -> 내림 3
        Assert.AreEqual(3, result.ShipmentCount);
        Assert.IsTrue(result.IsEstimated);
    }

    [TestMethod]
    public void ComputeChannel_EmptyTrackingList_BlankStringsIgnored_TreatedAsNone()
    {
        var result = PartnerConsolidationShipmentCalculator.ComputeChannel(
            "펩투나", "CH1", "쿠팡일반", ["", "   "], shippingTotal: 6000m, shippingFeePerShipment: 3000m);

        Assert.AreEqual(2, result.ShipmentCount);
        Assert.IsTrue(result.IsEstimated);
    }

    [TestMethod]
    public void ComputeChannel_ZeroShippingTotal_ReturnsZeroShipments_W3()
    {
        var result = PartnerConsolidationShipmentCalculator.ComputeChannel(
            "펩투나", "CH1", "쿠팡일반", [], shippingTotal: 0m, shippingFeePerShipment: 3000m);

        Assert.AreEqual(0, result.ShipmentCount);
        Assert.IsTrue(result.IsEstimated);
    }

    [TestMethod]
    public void ComputeCompanyBilling_SumsChannelShipmentsAndMultipliesByRate()
    {
        var channels = new List<PartnerConsolidationChannelShipment>
        {
            new() { CompanyName = "펩투나", ChannelCode = "CH1", ShipmentCount = 10 },
            new() { CompanyName = "펩투나", ChannelCode = "CH2", ShipmentCount = 7 },
        };

        var (shipmentCount, feeTotal) = PartnerConsolidationShipmentCalculator.ComputeCompanyBilling(channels, billingRatePerShipment: 3000m);

        Assert.AreEqual(17, shipmentCount);
        Assert.AreEqual(51000m, feeTotal);
    }
}

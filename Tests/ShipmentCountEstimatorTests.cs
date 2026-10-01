using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class ShipmentCountEstimatorTests
{
    [TestMethod]
    public void Compute_WithTrackingNumbers_CountsDistinctNonBlankValues()
    {
        var rows = new List<SettlementData>
        {
            new() { TrackingNo = "T-001", Shipping = 3000m },
            new() { TrackingNo = "T-001", Shipping = 3000m }, // 같은 묶음(합포장) — 중복 제거
            new() { TrackingNo = "t-002", Shipping = 3000m }, // 대소문자 달라도 같은 송장이면 1건
            new() { TrackingNo = "T-002", Shipping = 3000m },
            new() { TrackingNo = "", Shipping = 0m },
        };

        var (count, isEstimated) = ShipmentCountEstimator.Compute(rows);

        Assert.AreEqual(2, count);
        Assert.IsFalse(isEstimated);
    }

    [TestMethod]
    public void Compute_NoTrackingNumbers_FallsBackToShippingDividedBy3000()
    {
        var rows = new List<SettlementData>
        {
            new() { TrackingNo = null, Shipping = 6000m },
            new() { TrackingNo = null, Shipping = 3000m },
        };

        var (count, isEstimated) = ShipmentCountEstimator.Compute(rows);

        Assert.AreEqual(3, count); // 9000 / 3000
        Assert.IsTrue(isEstimated);
    }

    [TestMethod]
    public void Compute_NoTrackingNumbers_TruncatesFraction()
    {
        // 쿠팡: 배송비 163,000원 ÷ 3,000 = 54.33 → 54건(소수점 이하 버림). 8,500원(2.83)도 반올림하지 않고 2건.
        var rows = new List<SettlementData> { new() { Shipping = 163000m } };

        Assert.AreEqual(54, ShipmentCountEstimator.Compute(rows).Count);
        Assert.AreEqual(2, ShipmentCountEstimator.Compute(new List<SettlementData> { new() { Shipping = 8500m } }).Count);
    }

    [TestMethod]
    public void Compute_NoRowsAndNoShipping_ReturnsZeroEstimated()
    {
        var (count, isEstimated) = ShipmentCountEstimator.Compute(new List<SettlementData>());

        Assert.AreEqual(0, count);
        Assert.IsTrue(isEstimated);
    }
}

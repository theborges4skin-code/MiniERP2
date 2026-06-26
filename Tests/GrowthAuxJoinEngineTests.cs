using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class GrowthAuxJoinEngineTests
{
    [TestMethod]
    public void BuildValueMap_SumsDuplicateKeys()
    {
        var map = GrowthAuxJoinEngine.BuildValueMap(new[]
        {
            ("OPT1", 100m),
            ("OPT1", 50m),
            ("OPT2", 200m),
        });

        Assert.AreEqual(150m, map["OPT1"]);
        Assert.AreEqual(200m, map["OPT2"]);
    }

    [TestMethod]
    public void BuildValueMap_IgnoresBlankKeys()
    {
        var map = GrowthAuxJoinEngine.BuildValueMap(new[]
        {
            ("", 100m),
            ("OPT1", 50m),
        });

        Assert.HasCount(1, map);
        Assert.AreEqual(50m, map["OPT1"]);
    }

    [TestMethod]
    public void Apply_ShippingFee_SetsShippingColumn()
    {
        var data = new SettlementData();
        GrowthAuxJoinEngine.Apply(data, StdField.ShippingFee, 777m);

        Assert.AreEqual(777m, data.Shipping);
    }

    [TestMethod]
    public void Apply_HandlingFee_SetsFeeColumn()
    {
        var data = new SettlementData();
        GrowthAuxJoinEngine.Apply(data, StdField.HandlingFee, 333m);

        Assert.AreEqual(333m, data.Fee);
    }

    [TestMethod]
    public void Apply_NonFinancialField_IsIgnored()
    {
        var data = new SettlementData { Shipping = 1m, Fee = 2m, Settlement = 3m };
        GrowthAuxJoinEngine.Apply(data, StdField.ProductName, 999m);

        Assert.AreEqual(1m, data.Shipping);
        Assert.AreEqual(2m, data.Fee);
        Assert.AreEqual(3m, data.Settlement);
    }
}

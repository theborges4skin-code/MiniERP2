using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class TempSkuGeneratorTests
{
    [TestMethod]
    public void GenerateNext_EmptyList_ReturnsTemp001()
    {
        Assert.AreEqual("TEMP001", TempSkuGenerator.GenerateNext([]));
    }

    [TestMethod]
    public void GenerateNext_WithExistingTempSkus_ReturnsNextNumber()
    {
        Assert.AreEqual("TEMP003", TempSkuGenerator.GenerateNext(["TEMP001", "TEMP002"]));
    }

    [TestMethod]
    public void GenerateNext_IgnoresNonTempSkus()
    {
        Assert.AreEqual("TEMP002", TempSkuGenerator.GenerateNext(["SKU-A", "TEMP001", "MSKU-123"]));
    }
}

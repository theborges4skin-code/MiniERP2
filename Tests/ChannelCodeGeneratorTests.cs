using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class ChannelCodeGeneratorTests
{
    [TestMethod]
    public void GenerateNext_EmptyList_ReturnsCH001()
    {
        var code = ChannelCodeGenerator.GenerateNext([]);

        Assert.AreEqual("CH001", code);
    }

    [TestMethod]
    public void GenerateNext_WithExistingCode_ReturnsNextNumber()
    {
        var code = ChannelCodeGenerator.GenerateNext(["CH001"]);

        Assert.AreEqual("CH002", code);
    }

    [TestMethod]
    public void GenerateNext_IgnoresNonMatchingCodes()
    {
        var code = ChannelCodeGenerator.GenerateNext(["COUPANG", "11ST", "CH002"]);

        Assert.AreEqual("CH003", code);
    }

    [TestMethod]
    public void GenerateNext_UsesHighestExistingNumber_RegardlessOfOrder()
    {
        var code = ChannelCodeGenerator.GenerateNext(["CH003", "CH001", "CH002"]);

        Assert.AreEqual("CH004", code);
    }

    [TestMethod]
    public void GenerateNext_BeyondThreeDigits_DoesNotTruncate()
    {
        var code = ChannelCodeGenerator.GenerateNext(["CH100"]);

        Assert.AreEqual("CH101", code);
    }
}

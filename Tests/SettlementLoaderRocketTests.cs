using MiniERP2.DataLoaders;

namespace MiniERP2.Tests;

[TestClass]
public class SettlementLoaderRocketTests
{
    [TestMethod]
    [DataRow("29855741", "29855741")]
    [DataRow("29855741.0", "29855741")]
    [DataRow("29,855,741", "29855741")]
    [DataRow("29,855,741.0", "29855741")]
    [DataRow(" 29855741 ", "29855741")]
    [DataRow("", "")]
    [DataRow(null, "")]
    public void NormalizeInvoiceKey_VariousFormats_ReturnsDigitsOnly(string? raw, string expected)
    {
        Assert.AreEqual(expected, SettlementLoader.NormalizeInvoiceKey(raw));
    }
}

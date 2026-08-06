using MiniERP2.Utils;

namespace MiniERP2.Tests;

/// <summary>간이마진계산기_개발기획서.md §5.1 Reserve1 파싱 규칙 검증.</summary>
[TestClass]
public class ReserveTextPriceParserTests
{
    [TestMethod]
    public void Parse_PlainNumber_ReturnsValue()
    {
        Assert.AreEqual(15000m, ReserveTextPriceParser.Parse("15000"));
    }

    [TestMethod]
    public void Parse_WithCommaWonSymbolAndSpaces_StripsAndParses()
    {
        Assert.AreEqual(15000m, ReserveTextPriceParser.Parse("₩ 15,000 원"));
    }

    [TestMethod]
    public void Parse_NonNumericSpecString_ReturnsNull()
    {
        // 기획서 §5.1: "규격A" 같은 비숫자 값은 오류 없이 공란으로 처리한다.
        Assert.IsNull(ReserveTextPriceParser.Parse("규격A"));
    }

    [TestMethod]
    public void Parse_Empty_ReturnsNull()
    {
        Assert.IsNull(ReserveTextPriceParser.Parse(""));
        Assert.IsNull(ReserveTextPriceParser.Parse(null));
        Assert.IsNull(ReserveTextPriceParser.Parse("   "));
    }

    [TestMethod]
    public void Parse_ZeroOrNegative_ReturnsNull()
    {
        Assert.IsNull(ReserveTextPriceParser.Parse("0"));
        Assert.IsNull(ReserveTextPriceParser.Parse("-100"));
    }
}

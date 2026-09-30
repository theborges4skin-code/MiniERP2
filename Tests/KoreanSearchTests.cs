using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class KoreanSearchTests
{
    [TestMethod]
    [DataRow("(주)맘씨생활건강", "ㅁㅆ")]
    [DataRow("(주)맘씨생활건강", "ㅅㅎㄱㄱ")]
    [DataRow("(주)맘씨생활건강", "ㅁ씨ㅅ")]
    [DataRow("(주)맘씨생활건강", "맘씨")]
    [DataRow("쿠팡그로스_풀필입고용", "ㄱㄹㅅ_ㅍ")]
    [DataRow("CH025", "ch0")]
    [DataRow("ㅁㅆ메모", "ㅁㅆ")]
    public void Matches_True(string text, string query) => Assert.IsTrue(KoreanSearch.Matches(text, query));

    [TestMethod]
    [DataRow("(주)맘씨생활건강", "ㅁㄱ")]
    [DataRow("투유", "ㅌㅇㅇ")]
    [DataRow("CH025", "ㅊ")]
    [DataRow(null, "ㄱ")]
    public void Matches_False(string? text, string query) => Assert.IsFalse(KoreanSearch.Matches(text, query));

    [TestMethod]
    public void Matches_EmptyQuery_ReturnsTrue() => Assert.IsTrue(KoreanSearch.Matches("아무거나", ""));

    [TestMethod]
    public void ToChoseong_ConvertsOnlyHangulSyllables() =>
        Assert.AreEqual("(ㅈ)ㅁㅆ A1", KoreanSearch.ToChoseong("(주)맘씨 A1"));
}

using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class ExpiryDateParserTests
{
    [TestMethod]
    [DataRow("20280907")]
    [DataRow("2028.09.07")]
    [DataRow("2028.09.07.")]
    [DataRow("2028.9.7")]
    [DataRow("2028. 9. 7.")]
    [DataRow("2028-09-07")]
    [DataRow("2028-9-7")]
    [DataRow("2028/09/07")]
    [DataRow("280907")]
    // 실제 DB에 저장돼 있던 값들 — 아마존 출력 형식을 그대로 입력(끝 점 유무, 엑셀 붙여넣기로 딸려온 탭)
    [DataRow("SEP.07.2028.EXP")]
    [DataRow("SEP.07.2028.EXP.")]
    [DataRow("SEP.07.2028.EXP.\t")]
    [DataRow("sep.7.2028")]
    [DataRow("SEP 07, 2028")]
    [DataRow("EXP.SEP.07.2028")]
    public void TryParse_RecognizesCommonInputs(string input)
    {
        Assert.IsTrue(ExpiryDateParser.TryParse(input, out var date), input);
        Assert.AreEqual(new DateTime(2028, 9, 7), date, input);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("2028.09")]
    [DataRow("20281340")]
    [DataRow("미정")]
    public void TryParse_RejectsUnparseableInputs(string? input)
    {
        Assert.IsFalse(ExpiryDateParser.TryParse(input, out _));
    }
}

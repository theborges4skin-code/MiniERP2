using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class FbaKeyGeneratorTests
{
    [TestMethod]
    public void BuildMatchKey_WithShipmentId_FormatsSendPrefixAndBoxPosition()
    {
        var result = FbaKeyGenerator.BuildMatchKey("FBA15ABCDEFG", 6, 3);
        Assert.AreEqual("[SEND] FBA15ABCDEFG 총 6박스중 3번째", result);
    }

    [TestMethod]
    public void BuildMatchKey_WithoutShipmentId_LeavesDoubleSpaceInPlaceOfId()
    {
        // 기획서 §7.1 / §12 테스트 필수 항목 3 — ShipmentId 미입력 시 그 자리만 공란으로 남아
        // "[SEND] " 뒤에 이중 공백이 생겨야 한다.
        var result = FbaKeyGenerator.BuildMatchKey(null, 6, 3);
        Assert.AreEqual("[SEND]  총 6박스중 3번째", result);
    }

    [TestMethod]
    public void NormalizeMatchKey_TrimsWhitespace()
    {
        Assert.AreEqual("[SEND] X 총 1박스중 1번째", FbaKeyGenerator.NormalizeMatchKey("  [SEND] X 총 1박스중 1번째  "));
    }

    [TestMethod]
    public void NormalizeMatchKey_NullInput_ReturnsEmptyString()
    {
        Assert.AreEqual(string.Empty, FbaKeyGenerator.NormalizeMatchKey(null));
    }

    /// <summary>택배사 결과 파일의 고객주문번호 칸을 (Shipment ID, 박스순번)으로 분해할 수 있어야
    /// 한다 — 이게 운송장을 어떤 박스에 넣을지 판단하는 유일한 단서다.</summary>
    [TestMethod]
    public void TryParseMatchKey_RealCourierFileValue_ExtractsShipmentIdAndBoxSeq()
    {
        Assert.IsTrue(FbaKeyGenerator.TryParseMatchKey("[SEND] FBA19PS3XW2J 총 4박스중 2번째", out var parts));
        Assert.AreEqual("FBA19PS3XW2J", parts.ShipmentId);
        Assert.AreEqual(4, parts.TotalBoxes);
        Assert.AreEqual(2, parts.BoxSeq);
    }

    [TestMethod]
    public void TryParseMatchKey_RoundTripsBuildMatchKey()
    {
        Assert.IsTrue(FbaKeyGenerator.TryParseMatchKey(FbaKeyGenerator.BuildMatchKey("SHIP9", 12, 7), out var parts));
        Assert.AreEqual(new FbaMatchKeyParts("SHIP9", 12, 7), parts);
    }

    /// <summary>ShipmentId 미입력 건은 이중 공백이 남는데, 그래도 박스순번은 읽어야 한다.</summary>
    [TestMethod]
    public void TryParseMatchKey_MissingShipmentId_ReturnsEmptyIdAndParsesBoxSeq()
    {
        Assert.IsTrue(FbaKeyGenerator.TryParseMatchKey("[SEND]  총 6박스중 3번째", out var parts));
        Assert.AreEqual(string.Empty, parts.ShipmentId);
        Assert.AreEqual(3, parts.BoxSeq);
    }

    /// <summary>엑셀/수기 입력에서 끼어드는 표기 차이(전각공백·NBSP·줄바꿈·따옴표·대소문자)는 흡수한다.</summary>
    [TestMethod]
    public void TryParseMatchKey_ToleratesWhitespaceQuotesAndCaseVariations()
    {
        var raw = "[send]\t'SHIP1'　총  2박스 중 1번째\r\n";
        Assert.IsTrue(FbaKeyGenerator.TryParseMatchKey(raw, out var parts));
        Assert.AreEqual("SHIP1", parts.ShipmentId);
        Assert.AreEqual(2, parts.TotalBoxes);
        Assert.AreEqual(1, parts.BoxSeq);
    }

    /// <summary>FBO 발주나 일반 택배 건의 고객주문번호는 FBA 형식이 아니므로 걸러져야 한다.</summary>
    [TestMethod]
    [DataRow("#FBO26071301-03")]
    [DataRow("설레는01")]
    [DataRow("")]
    [DataRow(null)]
    [DataRow("[SEND] SHIP1 총 4박스중")]
    public void TryParseMatchKey_NonFbaValues_ReturnFalse(string? raw)
    {
        Assert.IsFalse(FbaKeyGenerator.TryParseMatchKey(raw, out _));
    }

    /// <summary>총박스수는 조회키에서 빠져야 한다 — 박스 추가·삭제로 재채번되어도 매칭이 유지되도록.</summary>
    [TestMethod]
    public void BuildLookupKey_IgnoresTotalBoxCount_ButSeparatesShipmentAndBoxSeq()
    {
        Assert.AreEqual(
            FbaKeyGenerator.BuildLookupKey("[SEND] SHIP1 총 5박스중 2번째"),
            FbaKeyGenerator.BuildLookupKey("[SEND] ship1 총 4박스중 2번째"));
        Assert.AreNotEqual(
            FbaKeyGenerator.BuildLookupKey("[SEND] SHIP1 총 4박스중 2번째"),
            FbaKeyGenerator.BuildLookupKey("[SEND] SHIP1 총 4박스중 3번째"));
        Assert.AreNotEqual(
            FbaKeyGenerator.BuildLookupKey("[SEND] SHIP1 총 4박스중 2번째"),
            FbaKeyGenerator.BuildLookupKey("[SEND] SHIP2 총 4박스중 2번째"));
    }

    /// <summary>FBA 형식이 아닌 매칭키(수기 입력 등)는 원문 전체로 조회키를 만들어 계속 매칭된다.</summary>
    [TestMethod]
    public void BuildLookupKey_NonFbaFormat_FallsBackToNormalizedRawText()
    {
        Assert.AreEqual(FbaKeyGenerator.BuildLookupKey("직접입력-01"), FbaKeyGenerator.BuildLookupKey("  직접입력-01 "));
        Assert.AreNotEqual(FbaKeyGenerator.BuildLookupKey("직접입력-01"), FbaKeyGenerator.BuildLookupKey("직접입력-02"));
    }
}

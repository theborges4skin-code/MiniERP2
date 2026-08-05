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
}

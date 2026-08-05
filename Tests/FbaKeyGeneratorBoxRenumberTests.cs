using MiniERP2.Utils;

namespace MiniERP2.Tests;

/// <summary>
/// 기획서 §12 테스트 필수 항목 4 — 박스 삭제 후 carton no. 재채번 및 총박스수 갱신이 전 박스에
/// 반영되는지. FbaOrderForm.RecomputeBoxIdentifiers가 쓰는 순수 함수만 뽑아 검증한다.
/// </summary>
[TestClass]
public class FbaKeyGeneratorBoxRenumberTests
{
    [TestMethod]
    public void BuildBoxSeqRenumberMap_ContiguousSequence_MapsToSameNumbers()
    {
        var map = FbaKeyGenerator.BuildBoxSeqRenumberMap([1, 2, 3]);
        Assert.AreEqual(1, map[1]);
        Assert.AreEqual(2, map[2]);
        Assert.AreEqual(3, map[3]);
    }

    [TestMethod]
    public void BuildBoxSeqRenumberMap_AfterDeletingMiddleBox_ClosesTheGap()
    {
        // 박스 1,2,3 중 2번을 삭제하면 남은 박스는 [1,3] — 재채번 결과는 1,2가 되어 총박스수도 2로 줄어야 한다.
        var map = FbaKeyGenerator.BuildBoxSeqRenumberMap([1, 3]);
        Assert.AreEqual(2, map.Count);
        Assert.AreEqual(1, map[1]);
        Assert.AreEqual(2, map[3]);
    }

    [TestMethod]
    public void BuildBoxSeqRenumberMap_AfterDeletingFirstBox_ShiftsAllDown()
    {
        var map = FbaKeyGenerator.BuildBoxSeqRenumberMap([2, 3, 4]);
        Assert.AreEqual(1, map[2]);
        Assert.AreEqual(2, map[3]);
        Assert.AreEqual(3, map[4]);
    }

    [TestMethod]
    public void BuildBoxSeqRenumberMap_DuplicateBoxSeqsFromMultipleItemRows_CollapseToOneEntry()
    {
        // 한 박스 안에 CSKU 여러 줄이 있으면 같은 BoxSeq가 여러 번 들어온다 — 맵은 박스 단위로만 존재해야 한다.
        var map = FbaKeyGenerator.BuildBoxSeqRenumberMap([1, 1, 1, 2, 2]);
        Assert.AreEqual(2, map.Count);
    }

    [TestMethod]
    public void BuildBoxSeqRenumberMap_EmptyInput_ReturnsEmptyMap()
    {
        var map = FbaKeyGenerator.BuildBoxSeqRenumberMap([]);
        Assert.IsEmpty(map);
    }

    [TestMethod]
    public void RenumberedTotalBoxes_FeedsIntoMatchKeyWithUpdatedCount()
    {
        // 3박스 중 가운데 1개를 삭제한 뒤 재채번하면 총박스수는 2가 되고, 남은 박스들의 매칭키도
        // "총 2박스중"으로 갱신되어야 한다(§7.1 — 박스 추가·삭제 시 총박스수가 바뀌므로 재계산).
        var map = FbaKeyGenerator.BuildBoxSeqRenumberMap([1, 3]);
        var totalBoxes = map.Count;

        var keyForFormerBox1 = FbaKeyGenerator.BuildMatchKey("SHIP1", totalBoxes, map[1]);
        var keyForFormerBox3 = FbaKeyGenerator.BuildMatchKey("SHIP1", totalBoxes, map[3]);

        Assert.AreEqual("[SEND] SHIP1 총 2박스중 1번째", keyForFormerBox1);
        Assert.AreEqual("[SEND] SHIP1 총 2박스중 2번째", keyForFormerBox3);
    }
}

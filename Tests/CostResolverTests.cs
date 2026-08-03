using MiniERP2.Utils;

namespace MiniERP2.Tests;

/// <summary>
/// CSKU제조원가_개별관리_개발기획서.md §4.4 우선순위 규칙(매입처 매입가 > CSKU 개별원가 > 마스터
/// 대표원가) 검증.
/// </summary>
[TestClass]
public class CostResolverTests
{
    [TestMethod]
    public void Resolve_PurchasePriceGiven_TakesPriorityOverEverything()
    {
        var result = CostResolver.Resolve(purchasePrice: 100m, costPriceOverride: 200m, masterCostPrice: 300m);
        Assert.AreEqual(100m, result);
    }

    [TestMethod]
    public void Resolve_NoPurchasePrice_UsesOverrideOverMaster()
    {
        var result = CostResolver.Resolve(purchasePrice: null, costPriceOverride: 200m, masterCostPrice: 300m);
        Assert.AreEqual(200m, result);
    }

    [TestMethod]
    public void Resolve_NoPurchasePriceOrOverride_FallsBackToMaster()
    {
        var result = CostResolver.Resolve(purchasePrice: null, costPriceOverride: null, masterCostPrice: 300m);
        Assert.AreEqual(300m, result);
    }

    [TestMethod]
    public void Resolve_OverrideIsZero_IsRespectedAsExplicitValue()
    {
        // 0은 "개별관리 상태에서 원가 0원"이라는 명시적 값이지 NULL(연동)이 아니다(§4.1).
        var result = CostResolver.Resolve(purchasePrice: null, costPriceOverride: 0m, masterCostPrice: 300m);
        Assert.AreEqual(0m, result);
    }
}

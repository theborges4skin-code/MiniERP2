using MiniERP2.Utils;

namespace MiniERP2.Tests;

/// <summary>
/// 기획서 §12 테스트 필수 항목 6 — 1단 미만·비배수 수량 입력이 경고 없이 통과하는지.
/// FbaOrderSaveValidator는 §3.4에서 명시적으로 허용한 이 케이스를 절대 걸러내면 안 된다.
/// </summary>
[TestClass]
public class FbaOrderSaveValidatorTests
{
    [TestMethod]
    public void Validate_NoRows_ReturnsInvalidWithAddBoxMessage()
    {
        var result = FbaOrderSaveValidator.Validate([]);
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("박스를 하나 이상 추가하세요.", result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_PlaceholderBoxPresent_ReturnsInvalidNamingTheBox()
    {
        var rows = new List<FbaOrderSaveValidator.Row> { new(BoxSeq: 2, IsPlaceholder: true, Qty: 0) };
        var result = FbaOrderSaveValidator.Validate(rows);
        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.ErrorMessage, "박스 2");
    }

    [TestMethod]
    public void Validate_UnassignedPoolItemPresent_ReturnsInvalid()
    {
        var rows = new List<FbaOrderSaveValidator.Row> { new(BoxSeq: 0, IsPlaceholder: false, Qty: 5) };
        var result = FbaOrderSaveValidator.Validate(rows);
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("미배정 품목이 있습니다. 박스에 담은 뒤 저장하세요.", result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_QtyLessThanOne_ReturnsInvalid()
    {
        var rows = new List<FbaOrderSaveValidator.Row> { new(BoxSeq: 1, IsPlaceholder: false, Qty: 0) };
        var result = FbaOrderSaveValidator.Validate(rows);
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("수량이 1 미만인 행이 있습니다.", result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_QtyBelowOneLayerAmount_PassesWithoutWarning()
    {
        // 1단수량(QtyPerLayer)은 검증기가 아예 모르는 값이다 — 7개처럼 1단(예: 10개)에 못 미쳐도
        // 통과해야 한다(§3.4 "1단 미만·비배수 허용").
        var rows = new List<FbaOrderSaveValidator.Row> { new(BoxSeq: 1, IsPlaceholder: false, Qty: 7) };
        var result = FbaOrderSaveValidator.Validate(rows);
        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_QtyNotMultipleOfLayerAmount_PassesWithoutWarning()
    {
        // 예: 1단수량 10개인데 23개를 담아도(2.3배, 비배수) 경고 없이 통과해야 한다.
        var rows = new List<FbaOrderSaveValidator.Row> { new(BoxSeq: 1, IsPlaceholder: false, Qty: 23) };
        var result = FbaOrderSaveValidator.Validate(rows);
        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void Validate_AllRowsValid_ReturnsValidResult()
    {
        var rows = new List<FbaOrderSaveValidator.Row>
        {
            new(BoxSeq: 1, IsPlaceholder: false, Qty: 5),
            new(BoxSeq: 2, IsPlaceholder: false, Qty: 1),
        };
        var result = FbaOrderSaveValidator.Validate(rows);
        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }
}

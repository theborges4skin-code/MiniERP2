using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class ShipmentGroupingTests
{
    [TestMethod]
    public void GetEffectiveGroupId_WithExplicitGroupId_ReturnsThatValue()
    {
        var item = new OfsOrderItem { OrderNo = "ORDER-1", ShipmentGroupId = "ORDER-1-2" };

        Assert.AreEqual("ORDER-1-2", ShipmentGrouping.GetEffectiveGroupId(item));
    }

    [TestMethod]
    public void GetEffectiveGroupId_WithoutExplicitGroupId_IsUniquePerInstanceEvenWithSameOrderNo()
    {
        // 합포장은 택배사 프로그램이 다운스트림에서 자동으로 처리하므로, MiniERP2는 같은 주문번호라도
        // 명시적으로 합포장을 지정하지 않으면 임의로 합치지 않는다(기본값 = 줄마다 별도 송장).
        var item1 = new OfsOrderItem { OrderNo = "ORDER-1", ProductName = "상품A" };
        var item2 = new OfsOrderItem { OrderNo = "ORDER-1", ProductName = "상품B" };

        Assert.AreNotEqual(ShipmentGrouping.GetEffectiveGroupId(item1), ShipmentGrouping.GetEffectiveGroupId(item2));
    }

    [TestMethod]
    public void GetEffectiveGroupId_WithoutOrderNo_IsUniquePerInstanceButStable()
    {
        var item1 = new OfsOrderItem();
        var item2 = new OfsOrderItem();

        var first = ShipmentGrouping.GetEffectiveGroupId(item1);
        var second = ShipmentGrouping.GetEffectiveGroupId(item2);

        Assert.AreNotEqual(first, second);
        Assert.AreEqual(first, ShipmentGrouping.GetEffectiveGroupId(item1), "같은 인스턴스는 항상 같은 그룹 키를 반환해야 한다.");
    }

    [TestMethod]
    public void GetEffectiveGroupId_TwoItemsWithSameExplicitGroupId_ShareSameGroup()
    {
        // 합포장으로 명시적으로 같은 그룹ID를 부여한 경우에만 합쳐져야 한다.
        var item1 = new OfsOrderItem { OrderNo = "ORDER-1", ShipmentGroupId = "BOX-1" };
        var item2 = new OfsOrderItem { OrderNo = "ORDER-2", ShipmentGroupId = "BOX-1" };

        Assert.AreEqual(ShipmentGrouping.GetEffectiveGroupId(item1), ShipmentGrouping.GetEffectiveGroupId(item2));
    }

    [TestMethod]
    public void GetEffectiveGroupId_WithSourceRowKey_IsDeterministicAcrossDifferentInstances()
    {
        // 발주서를 재로드하면 새 OfsOrderItem 인스턴스가 생기지만(객체 해시는 바뀜), 같은 파일의
        // 같은 행이면 SourceRowKey가 같으므로 그룹 키도 같아야 한다(재저장 시 같은 이력 레코드로
        // 매칭되게 하기 위함 — 버그2 근본원인 수정).
        var firstLoad = new OfsOrderItem { ChannelCode = "CH005", SourceRowKey = "order.xlsx#7" };
        var reloaded = new OfsOrderItem { ChannelCode = "CH005", SourceRowKey = "order.xlsx#7" };

        Assert.AreEqual(ShipmentGrouping.GetEffectiveGroupId(firstLoad), ShipmentGrouping.GetEffectiveGroupId(reloaded));
    }

    [TestMethod]
    public void GetEffectiveGroupId_WithSourceRowKey_DiffersAcrossDifferentRows()
    {
        var row7 = new OfsOrderItem { ChannelCode = "CH005", SourceRowKey = "order.xlsx#7" };
        var row8 = new OfsOrderItem { ChannelCode = "CH005", SourceRowKey = "order.xlsx#8" };

        Assert.AreNotEqual(ShipmentGrouping.GetEffectiveGroupId(row7), ShipmentGrouping.GetEffectiveGroupId(row8));
    }

    [TestMethod]
    public void GetEffectiveGroupId_WithoutSourceRowKey_FallsBackToObjectHash()
    {
        // 수동 추가 등 SourceRowKey가 없는 항목은 기존처럼 객체 식별 해시로 폴백해야 한다.
        var manual1 = new OfsOrderItem { ChannelCode = "CH005" };
        var manual2 = new OfsOrderItem { ChannelCode = "CH005" };

        Assert.AreNotEqual(ShipmentGrouping.GetEffectiveGroupId(manual1), ShipmentGrouping.GetEffectiveGroupId(manual2));
    }

    [TestMethod]
    public void BuildCombinedItemDescription_SingleLine_ReturnsPlainTextWithoutBrackets()
    {
        var items = new[] { new OfsOrderItem { ProductName = "A품목", Quantity = 2 } };

        Assert.AreEqual("A품목 2개", ShipmentGrouping.BuildCombinedItemDescription(items));
    }

    [TestMethod]
    public void BuildCombinedItemDescription_MultipleLines_WrapsEachInBracketsJoinedByPlus()
    {
        // 합포장 시 줄바꿈만으로는 송장에서 품목 구분이 어렵다는 피드백을 반영한 표시 형식. 묶음에
        // 품목이 2건 이상이면(다중건 포장) 작업자가 알아보기 쉽도록 수량 표기 앞뒤에 xx가 붙는다.
        var items = new[]
        {
            new OfsOrderItem { ProductName = "A품목", Quantity = 2 },
            new OfsOrderItem { ProductName = "B품목", Quantity = 3 },
        };

        Assert.AreEqual("((A품목xx 2개xx))   +   ((B품목xx 3개xx))", ShipmentGrouping.BuildCombinedItemDescription(items));
    }

    [TestMethod]
    public void BuildCombinedItemDescription_CustomQuantityFormat_ReplacesPlaceholderWithQuantity()
    {
        var items = new[] { new OfsOrderItem { ProductName = "A상품", Quantity = 2 } };

        var result = ShipmentGrouping.BuildCombinedItemDescription(items, "   ▶[##개]");

        Assert.AreEqual("A상품   ▶ii[2개]", result);
    }

    [TestMethod]
    public void BuildCombinedItemDescription_QuantityOfOne_NoStarsAdded()
    {
        var items = new[] { new OfsOrderItem { ProductName = "A상품", Quantity = 1 } };

        var result = ShipmentGrouping.BuildCombinedItemDescription(items, "   ▶[##개]");

        Assert.AreEqual("A상품   ▶[1개]", result);
    }

    [TestMethod]
    public void BuildCombinedItemDescription_QuantityOfFive_AddsRomanNumeralBeforeBracket()
    {
        var items = new[] { new OfsOrderItem { ProductName = "A상품", Quantity = 5 } };

        var result = ShipmentGrouping.BuildCombinedItemDescription(items, "▶[##개]");

        Assert.AreEqual("A상품▶v[5개]", result);
    }

    [TestMethod]
    public void BuildCombinedItemDescription_CustomQuantityFormatWithMultipleItems_WrapsFormattedTagWithXx()
    {
        var items = new[]
        {
            new OfsOrderItem { ProductName = "A상품", Quantity = 2 },
            new OfsOrderItem { ProductName = "B상품", Quantity = 1 },
        };

        var result = ShipmentGrouping.BuildCombinedItemDescription(items, "▶[##개]");

        Assert.AreEqual("((A상품xx▶ii[2개]xx))   +   ((B상품xx▶[1개]xx))", result);
    }

    [TestMethod]
    public void BuildCombinedItemDescription_UsesInvoiceDisplayNameOverProductNameWhenSet()
    {
        var items = new[] { new OfsOrderItem { ProductName = "원본상품명", InvoiceDisplayName = "샴푸 500ml", Quantity = 2 } };

        Assert.AreEqual("샴푸 500ml 2개", ShipmentGrouping.BuildCombinedItemDescription(items));
    }

    [TestMethod]
    public void CountDescriptionLines_CountsNonBlankLinesRegardlessOfDisplayFormat()
    {
        var items = new[]
        {
            new OfsOrderItem { ProductName = "A품목", Quantity = 1 },
            new OfsOrderItem { ProductName = "B품목", Quantity = 1 },
            new OfsOrderItem { InvoiceLabel = "" }, // 미리보기에서 합쳐서 덮어쓴 줄(빈 InvoiceLabel)은 줄 수에서 제외
        };

        Assert.AreEqual(2, ShipmentGrouping.CountDescriptionLines(items));
    }

    [TestMethod]
    public void RenumberSplitRecipients_TwoShipments_AppendsSequentialNumbers()
    {
        var shipment1 = new List<OfsOrderItem> { new() { Recipient = "홍길동" } };
        var shipment2 = new List<OfsOrderItem> { new() { Recipient = "홍길동" } };

        ShipmentGrouping.RenumberSplitRecipients([shipment1, shipment2]);

        Assert.AreEqual("홍길동1", shipment1[0].Recipient);
        Assert.AreEqual("홍길동2", shipment2[0].Recipient);
    }

    [TestMethod]
    public void RenumberSplitRecipients_AppliesSameNumberToAllItemsInOneShipment()
    {
        // 한 송장(=한 묶음)에 여러 품목 줄이 있으면 그 줄들 전부 같은 수취인 번호를 가져야 한다.
        var shipment1 = new List<OfsOrderItem>
        {
            new() { Recipient = "홍길동", ProductName = "A품목" },
            new() { Recipient = "홍길동", ProductName = "B품목" },
        };
        var shipment2 = new List<OfsOrderItem> { new() { Recipient = "홍길동" } };

        ShipmentGrouping.RenumberSplitRecipients([shipment1, shipment2]);

        Assert.AreEqual("홍길동1", shipment1[0].Recipient);
        Assert.AreEqual("홍길동1", shipment1[1].Recipient);
        Assert.AreEqual("홍길동2", shipment2[0].Recipient);
    }

    [TestMethod]
    public void RenumberSplitRecipients_SingleShipment_StripsExistingSuffix()
    {
        // 분리배송했던 송장들을 다시 합포장으로 합치면(=송장이 1개로 줄어들면), 예전에 붙였던
        // 수취인명 번호(1,2,3...)를 떼어 원래 이름으로 되돌려야 한다.
        var merged = new List<OfsOrderItem>
        {
            new() { Recipient = "홍길동1" },
            new() { Recipient = "홍길동1" },
        };

        ShipmentGrouping.RenumberSplitRecipients([merged]);

        Assert.AreEqual("홍길동", merged[0].Recipient);
        Assert.AreEqual("홍길동", merged[1].Recipient);
    }

    [TestMethod]
    public void RenumberSplitRecipients_ReSplittingAlreadyNumberedRecipient_DoesNotCompoundSuffix()
    {
        // 이미 "홍길동1"처럼 번호가 붙은 상태에서 다시 분리하면 "홍길동11", "홍길동12"처럼 번호가
        // 누적되면 안 되고, 기존 번호를 떼어낸 원래 이름 기준으로 다시 매겨야 한다.
        var shipment1 = new List<OfsOrderItem> { new() { Recipient = "홍길동1" } };
        var shipment2 = new List<OfsOrderItem> { new() { Recipient = "홍길동1" } };
        var shipment3 = new List<OfsOrderItem> { new() { Recipient = "홍길동1" } };

        ShipmentGrouping.RenumberSplitRecipients([shipment1, shipment2, shipment3]);

        Assert.AreEqual("홍길동1", shipment1[0].Recipient);
        Assert.AreEqual("홍길동2", shipment2[0].Recipient);
        Assert.AreEqual("홍길동3", shipment3[0].Recipient);
    }

    [TestMethod]
    public void RenumberSplitRecipients_EmptyRecipient_DoesNothing()
    {
        var shipment1 = new List<OfsOrderItem> { new() { Recipient = null } };
        var shipment2 = new List<OfsOrderItem> { new() { Recipient = null } };

        ShipmentGrouping.RenumberSplitRecipients([shipment1, shipment2]);

        Assert.IsNull(shipment1[0].Recipient);
        Assert.IsNull(shipment2[0].Recipient);
    }
}

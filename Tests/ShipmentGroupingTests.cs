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
}

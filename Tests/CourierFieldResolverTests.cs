using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

/// <summary>
/// OFS의 택배사 출력 미리보기와 CourierExporter가 공유하는 헤더별 값 계산/수정 로직을 검증한다.
/// 미리보기에서 박스타입/내품수량/운임 등 매핑된 속성이 없는 헤더를 직접 입력할 수 있게 한
/// 기능의 핵심 로직이다.
/// </summary>
[TestClass]
public class CourierFieldResolverTests
{
    private static CourierMaster BuildCourier(string headerMappingJson) => new()
    {
        CourierName = "테스트택배",
        HeaderMappingJson = headerMappingJson,
    };

    [TestMethod]
    public void Resolve_PropertyNameEmpty_ReturnsManualFieldValue()
    {
        var entry = new HeaderMappingEntry("박스타입", "");
        var item = new OfsOrderItem { ManualFieldValues = new Dictionary<string, string> { ["박스타입"] = "소형" } };

        var value = CourierFieldResolver.Resolve(entry, [item], BuildCourier("[]"), null);

        Assert.AreEqual("소형", value);
    }

    [TestMethod]
    public void Resolve_PropertyNameEmptyAndNoManualValue_ReturnsNull()
    {
        var entry = new HeaderMappingEntry("내품수량", "");
        var item = new OfsOrderItem();

        var value = CourierFieldResolver.Resolve(entry, [item], BuildCourier("[]"), null);

        Assert.IsNull(value);
    }

    [TestMethod]
    public void Resolve_MappedToOrdinaryProperty_ReturnsRepresentativeValue()
    {
        var entry = new HeaderMappingEntry("연락처", "Phone");
        var item = new OfsOrderItem { Phone = "010-1234-5678" };

        var value = CourierFieldResolver.Resolve(entry, [item], BuildCourier("[]"), null);

        Assert.AreEqual("010-1234-5678", value);
    }

    [TestMethod]
    public void Resolve_FixedOverrideSet_TakesPriorityOverManualValueAndProperty()
    {
        var entry = new HeaderMappingEntry("도착지코드", "Phone");
        var item = new OfsOrderItem { Phone = "010-1111-2222" };
        var channelConfig = new ChannelConfig
        {
            ChannelCode = "CH1",
            ChannelName = "채널A",
            CourierHeaderOverrides = [new CourierHeaderOverride { CourierName = "테스트택배", Header = "도착지코드", FixedValue = "AAA" }],
        };

        var value = CourierFieldResolver.Resolve(entry, [item], BuildCourier("[]"), channelConfig);

        Assert.AreEqual("AAA", value);
    }

    [TestMethod]
    public void Resolve_MappedSkuWithInvoiceDisplayName_ReturnsInvoiceDisplayName()
    {
        var entry = new HeaderMappingEntry("품목코드", "MappedSku");
        var item = new OfsOrderItem { MappedSku = "CSKU1", InvoiceDisplayName = "이공이공 핸드워시" };

        var value = CourierFieldResolver.Resolve(entry, [item], BuildCourier("[]"), null);

        Assert.AreEqual("이공이공 핸드워시", value);
    }

    [TestMethod]
    public void Resolve_ItemDescriptionProperty_CombinesAllItemsInGroup()
    {
        var entry = new HeaderMappingEntry("품목", "ProductName");
        var items = new List<OfsOrderItem>
        {
            new() { ProductName = "상품A", Quantity = 2 },
            new() { ProductName = "상품B", Quantity = 1 },
        };

        var value = CourierFieldResolver.Resolve(entry, items, BuildCourier("[]"), null);

        StringAssert.Contains(value, "상품A");
        StringAssert.Contains(value, "상품B");
    }

    [TestMethod]
    public void IsEditable_EmptyPropertyName_ReturnsTrue()
    {
        Assert.IsTrue(CourierFieldResolver.IsEditable(""));
        Assert.IsTrue(CourierFieldResolver.IsEditable(null));
    }

    [TestMethod]
    public void IsEditable_MappedSku_ReturnsFalse()
    {
        Assert.IsFalse(CourierFieldResolver.IsEditable("MappedSku"));
    }

    [TestMethod]
    public void IsEditable_BroadcastProperty_ReturnsTrue()
    {
        Assert.IsTrue(CourierFieldResolver.IsEditable("Recipient"));
        Assert.IsTrue(CourierFieldResolver.IsEditable("TrackingNo"));
    }

    [TestMethod]
    public void Apply_EmptyPropertyName_StoresIntoManualFieldValuesOfFirstItem()
    {
        var entry = new HeaderMappingEntry("박스타입", "");
        var items = new List<OfsOrderItem> { new(), new() };

        CourierFieldResolver.Apply(entry, items, "대형");

        Assert.AreEqual("대형", items[0].ManualFieldValues?["박스타입"]);
        Assert.IsNull(items[1].ManualFieldValues);
    }

    [TestMethod]
    public void Apply_BroadcastProperty_SetsValueOnAllItemsInGroup()
    {
        var entry = new HeaderMappingEntry("연락처", "Phone");
        var items = new List<OfsOrderItem> { new() { Phone = "old1" }, new() { Phone = "old2" } };

        CourierFieldResolver.Apply(entry, items, "010-0000-0000");

        Assert.AreEqual("010-0000-0000", items[0].Phone);
        Assert.AreEqual("010-0000-0000", items[1].Phone);
    }

    [TestMethod]
    public void Apply_ItemDescriptionProperty_SetsInvoiceLabelOnFirstAndBlanksRest()
    {
        var entry = new HeaderMappingEntry("품목", "ProductName");
        var items = new List<OfsOrderItem> { new(), new() };

        CourierFieldResolver.Apply(entry, items, "수동입력 품목명");

        Assert.AreEqual("수동입력 품목명", items[0].InvoiceLabel);
        Assert.AreEqual(string.Empty, items[1].InvoiceLabel);
    }
}

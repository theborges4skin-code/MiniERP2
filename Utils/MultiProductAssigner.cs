using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// 발주(OFS) 행에 CSKU를 배정한다 — 한 품목이면 그 줄만 바꾸고, 주문 1건에 여러 품목을 보낼 때는
/// 그 줄을 첫 품목으로 쓰고 나머지 품목은 같은 수취인 정보를 복사한 줄로 만들어 한 묶음(합포장)으로
/// 묶는다. 택배 송장 한 줄에 품목이 최대 <see cref="MaxProductsPerShipment"/>개까지만 실리므로 그
/// 이상은 막는다 — 넘치면 사용자가 주문을 나눠(분리배송) 직접 입력해야 한다.
/// </summary>
public static class MultiProductAssigner
{
    public const int MaxProductsPerShipment = 4;

    /// <summary>배정할 품목 1개. Quantity가 null이면 대상 줄의 기존 수량을 그대로 둔다(단일 CSKU 교체용).</summary>
    public sealed record ProductSlot(string CskuCode, string ProductName, string? InvoiceDisplayName, int? Quantity);

    /// <summary>슬롯 목록이 배정 가능한지 검사한다. 문제가 없으면 null, 있으면 사용자에게 보여줄 문구.</summary>
    public static string? Validate(IReadOnlyList<ProductSlot> slots)
    {
        if (slots.Count == 0) return "배정할 CSKU를 하나 이상 선택하세요.";
        if (slots.Count > MaxProductsPerShipment)
            return $"한 송장에는 품목이 최대 {MaxProductsPerShipment}개까지만 실립니다. 그 이상은 주문을 나눠(분리배송) 직접 입력하세요.";

        var duplicate = slots.GroupBy(s => s.CskuCode, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            return $"같은 CSKU({duplicate.Key})가 두 번 선택됐습니다. 한 번만 고르고 수량으로 조정하세요.";

        if (slots.Any(s => s.Quantity is < 1)) return "수량은 1 이상이어야 합니다.";
        return null;
    }

    /// <summary>
    /// target 줄에 첫 품목을 넣고, 둘째 품목부터는 target의 주문 정보를 복사한 새 줄을 만들어 돌려준다
    /// (호출자가 target 바로 아래에 끼워 넣는다). 품목이 2개 이상이면 전부 target의 묶음으로 합친다.
    /// </summary>
    public static List<OfsOrderItem> Apply(OfsOrderItem target, IReadOnlyList<ProductSlot> slots, string status)
    {
        if (slots.Count == 0) return [];

        ApplySlot(target, slots[0], status);
        if (slots.Count == 1) return [];

        var groupId = ShipmentGrouping.GetEffectiveGroupId(target);
        target.ShipmentGroupId = groupId;
        target.InvoiceLabel = null;

        var extras = new List<OfsOrderItem>();
        foreach (var slot in slots.Skip(1))
        {
            var clone = CloneOrderInfo(target);
            ApplySlot(clone, slot, status);
            clone.ShipmentGroupId = groupId;
            extras.Add(clone);
        }
        return extras;
    }

    private static void ApplySlot(OfsOrderItem item, ProductSlot slot, string status)
    {
        item.MappedSku = slot.CskuCode;
        item.InvoiceDisplayName = slot.InvoiceDisplayName;
        item.Status = status;
        if (slot.Quantity is int qty) item.Quantity = qty;

        // 발주서에서 온 줄은 원본 상품명을 남겨 둬야(조건부 매핑 규칙 만들기 등) 하므로 비어 있을 때만
        // 채운다. 수동으로 만든 줄(SourceRowKey 없음)은 상품명이 곧 배정한 품목명이라 같이 바꾼다.
        if (string.IsNullOrWhiteSpace(item.ProductName) || item.SourceRowKey == null)
            item.ProductName = slot.ProductName;
    }

    /// <summary>같은 주문(수취인/배송지)으로 나가는 둘째 품목 이후의 줄 — 품목 고유 값(SKU·매출액 등)은 복사하지 않는다.</summary>
    private static OfsOrderItem CloneOrderInfo(OfsOrderItem source) => new()
    {
        ChannelCode = source.ChannelCode,
        OrderNo = source.OrderNo,
        OrderDate = source.OrderDate,
        Recipient = source.Recipient,
        Phone = source.Phone,
        Address = source.Address,
        DeliveryMessage = source.DeliveryMessage,
        LineKind = source.LineKind,
        ChargeShippingFee = source.ChargeShippingFee,
        ShippingFeeAmount = source.ShippingFeeAmount,
        DuplicateNote = source.DuplicateNote,
        DuplicateDetail = source.DuplicateDetail,
    };
}

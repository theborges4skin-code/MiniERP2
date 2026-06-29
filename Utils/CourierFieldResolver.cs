using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// 택배사 출력 양식의 헤더 하나(HeaderMappingEntry)에 대해, 한 묶음(=송장)의 실제 출력값을
/// 계산/수정한다. CourierExporter(실제 내보내기)와 OFS의 택배사 출력 미리보기가 똑같은 로직을
/// 공유해서, 미리보기에서 본 내용과 실제 엑셀로 나가는 내용이 항상 일치하게 한다.
/// </summary>
public static class CourierFieldResolver
{
    /// <summary>
    /// "품목" 칸에 매핑됐을 가능성이 있는 속성들 — 같은 묶음의 모든 줄을 합친 결합 표시문자열로
    /// 출력한다(나머지 속성은 묶음의 대표 줄 값을 그대로 쓴다). MappedSku도 여기 포함된다 —
    /// "매핑된 SKU"는 실제로는 CSKU의 송장표시명을 보여주는 칸이라 InvoiceLabel/ProductName과
    /// 같은 "품목 표시" 범주이고, 사용자 요청대로 수량표기형식까지 조합돼 나가야 한다(이전엔
    /// 송장표시명만 나오고 수량이 안 붙는 버그였음).
    /// </summary>
    private static readonly HashSet<string> ItemDescriptionProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(OfsOrderItem.InvoiceLabel),
        nameof(OfsOrderItem.ProductName),
        nameof(OfsOrderItem.MappedSku),
    };

    /// <summary>그룹의 모든 줄에 같은 값을 적용해야 의미가 맞는(=대표 줄 1곳만 고치면 안 되는) 속성들.</summary>
    private static readonly HashSet<string> BroadcastProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(OfsOrderItem.Recipient),
        nameof(OfsOrderItem.Phone),
        nameof(OfsOrderItem.Address),
        nameof(OfsOrderItem.DeliveryMessage),
        nameof(OfsOrderItem.TrackingNo),
    };

    /// <summary>
    /// 이 헤더를 미리보기에서 직접 고칠 수 있는지 여부. 고정값 오버라이드가 설정된 헤더는 항상
    /// 호출 쪽에서 별도로 막아야 한다(채널설정에서만 바꿀 수 있는 값이라서) — 이 메서드는 그
    /// 판단 없이 헤더 자체의 매핑 종류만 본다.
    /// </summary>
    public static bool IsEditable(string? propertyName) =>
        string.IsNullOrEmpty(propertyName)
        || ItemDescriptionProperties.Contains(propertyName)
        || BroadcastProperties.Contains(propertyName);

    /// <summary>
    /// 채널+택배사+헤더에 고정값 오버라이드가 설정돼 있으면 그 값을, 아니면 null을 반환한다.
    /// </summary>
    public static string? GetFixedOverride(ChannelConfig? channelConfig, string courierName, string header) =>
        channelConfig?.CourierHeaderOverrides
            .FirstOrDefault(o => o.CourierName == courierName && o.Header == header)
            ?.FixedValue;

    /// <param name="resolveInvoiceDisplayNameFallback">
    /// "매핑된 SKU" 헤더는 보통 SkuMapper가 채워준 InvoiceDisplayName을 쓰면 충분하지만(OFS에서
    /// 막 매핑한 직후), 발주/출고 이력에서 다시 내보낼 때처럼 그 값이 비어있을 수 있는 경로에서는
    /// CSKU를 다시 조회해야 한다. 그 DB 조회가 필요한 호출 쪽(CourierExporter)만 이 콜백을 넘기고,
    /// 매번 새로 그리는 OFS 실시간 미리보기는 생략해 불필요한 DB 조회를 피한다.
    /// </param>
    public static string? Resolve(
        HeaderMappingEntry entry,
        List<OfsOrderItem> groupItems,
        CourierMaster courier,
        ChannelConfig? channelConfig,
        Func<OfsOrderItem, string?>? resolveInvoiceDisplayNameFallback = null)
    {
        var representative = groupItems[0];

        var fixedValue = GetFixedOverride(channelConfig, courier.CourierName, entry.Header);
        if (fixedValue != null) return fixedValue;

        if (string.IsNullOrEmpty(entry.PropertyName))
        {
            // 매핑된 속성이 없는 헤더(예: 박스타입/내품수량/운임/선착불유무) — 사용자가 직접
            // 입력해 둔 값을 그대로 쓴다.
            return representative.ManualFieldValues?.GetValueOrDefault(entry.Header);
        }

        if (ItemDescriptionProperties.Contains(entry.PropertyName))
        {
            if (string.Equals(entry.PropertyName, nameof(OfsOrderItem.MappedSku), StringComparison.OrdinalIgnoreCase))
            {
                // BuildCombinedItemDescription은 InvoiceDisplayName이 비어있으면 ProductName으로
                // 대체하는데, "매핑된 SKU" 헤더는 발주/출고 이력 재출력처럼 둘 다 비어있을 수
                // 있는 경로에서도 CSKU 코드만 덜렁 보여주는 것보다는 나은 값을 보여줘야 한다.
                // 우선순위: 이미 채워진 InvoiceDisplayName > CSKU 재조회 결과 > ProductName(있으면
                // BuildAutoLabel이 알아서 씀, 그대로 둠) > 최후의 수단으로 CSKU 코드 자체.
                foreach (var item in groupItems)
                {
                    if (!string.IsNullOrWhiteSpace(item.InvoiceDisplayName)) continue;

                    var resolved = resolveInvoiceDisplayNameFallback?.Invoke(item);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        item.InvoiceDisplayName = resolved;
                    }
                    else if (string.IsNullOrWhiteSpace(item.ProductName))
                    {
                        item.InvoiceDisplayName = item.MappedSku;
                    }
                }
            }

            return ShipmentGrouping.BuildCombinedItemDescription(groupItems, courier.QuantityNotationFormat);
        }

        var property = typeof(OfsOrderItem).GetProperty(entry.PropertyName);
        return property?.GetValue(representative)?.ToString();
    }

    /// <summary>
    /// 위 <see cref="Resolve"/>와 짝을 이루는 쓰기 동작. <see cref="IsEditable"/>이 true인 헤더에
    /// 대해서만 호출해야 한다(고정값 오버라이드 여부는 호출 쪽에서 먼저 확인할 것).
    /// </summary>
    public static void Apply(HeaderMappingEntry entry, List<OfsOrderItem> groupItems, string newValue)
    {
        var representative = groupItems[0];

        if (string.IsNullOrEmpty(entry.PropertyName))
        {
            representative.ManualFieldValues ??= new Dictionary<string, string>();
            representative.ManualFieldValues[entry.Header] = newValue;
            return;
        }

        if (ItemDescriptionProperties.Contains(entry.PropertyName))
        {
            representative.InvoiceLabel = newValue;
            for (int i = 1; i < groupItems.Count; i++) groupItems[i].InvoiceLabel = string.Empty;
            return;
        }

        if (BroadcastProperties.Contains(entry.PropertyName))
        {
            var property = typeof(OfsOrderItem).GetProperty(entry.PropertyName);
            if (property == null) return;
            foreach (var item in groupItems) property.SetValue(item, newValue);
        }
    }
}

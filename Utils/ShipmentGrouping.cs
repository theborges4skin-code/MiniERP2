using System.Runtime.CompilerServices;
using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// 발주 항목이 어느 "묶음(송장 1건 단위)"에 속하는지 계산합니다. 기본값은 주문번호 단위(같은
/// 주문은 한 송장)이며, 사용자가 OFS 그리드에서 분리배송/합포장을 지정하면
/// <see cref="OfsOrderItem.ShipmentGroupId"/>에 실제 값이 채워져 이 기본값을 덮어씁니다.
/// </summary>
public static class ShipmentGrouping
{
    public static string GetEffectiveGroupId(OfsOrderItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ShipmentGroupId)) return item.ShipmentGroupId;
        if (!string.IsNullOrWhiteSpace(item.OrderNo)) return item.OrderNo!;

        // 주문번호조차 없는 경우(수동 추가 등) 그 줄 단독으로 취급한다. 내보내기 1회성 그룹 키일
        // 뿐이라 영속성은 필요 없지만, 같은 인스턴스에 대해서는 항상 같은 값을 반환해야 한다
        // (객체 식별 해시는 GC로 인스턴스가 옮겨져도 .NET이 동일하게 유지해준다).
        return $"__row_{RuntimeHelpers.GetHashCode(item)}";
    }

    /// <summary>
    /// 한 묶음(=한 송장)에 속한 모든 줄의 품목 표시문자열을 줄바꿈으로 이어붙인다. 택배사 내보내기
    /// (CourierExporter)와 OFS의 출력 미리보기 패널이 항상 같은 결과를 보여주도록 공유한다.
    /// </summary>
    public static string BuildCombinedItemDescription(IEnumerable<OfsOrderItem> items)
    {
        var lines = items
            .Select(i => i.InvoiceLabel ?? $"{i.ProductName} {i.Quantity}개")
            .Where(text => !string.IsNullOrWhiteSpace(text));
        return string.Join("\n", lines);
    }
}

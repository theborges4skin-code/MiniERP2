using System.Runtime.CompilerServices;
using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// 발주 항목이 어느 "묶음(송장 1건 단위)"에 속하는지 계산합니다. 기본값은 그룹화하지 않음(줄마다
/// 별도 송장)입니다 — 합포장은 택배사 프로그램이 다운스트림에서 주소 기준으로 자동 처리하므로,
/// MiniERP2가 주문번호 등을 기준으로 임의로 합칠 필요가 없습니다. 사용자가 OFS 그리드/미리보기에서
/// 명시적으로 합포장을 지정한 경우에만 <see cref="OfsOrderItem.ShipmentGroupId"/>에 같은 값이
/// 채워져 그 줄들이 한 송장으로 묶입니다.
/// </summary>
public static class ShipmentGrouping
{
    public static string GetEffectiveGroupId(OfsOrderItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ShipmentGroupId)) return item.ShipmentGroupId;

        // 명시적으로 합포장을 지정하지 않았으면 항상 그 줄 단독으로 취급한다(기본값 = 그룹화 없음).
        // 내보내기 1회성 그룹 키일 뿐이라 영속성은 필요 없지만, 같은 인스턴스에 대해서는 항상 같은
        // 값을 반환해야 한다(객체 식별 해시는 GC로 인스턴스가 옮겨져도 .NET이 동일하게 유지해준다).
        return $"__row_{RuntimeHelpers.GetHashCode(item)}";
    }

    /// <summary>
    /// 한 묶음(=한 송장)에 속한 모든 줄의 품목 표시문자열을 만든다. 줄이 1개면 그대로 보여주고,
    /// 합포장 등으로 2개 이상이면 "((품목A))   +   ((품목B))"처럼 괄호와 +로 구분해, 줄바꿈만으로는
    /// 송장에서 어디까지가 한 품목인지 헷갈린다는 피드백을 반영한다. 택배사 내보내기
    /// (CourierExporter)와 OFS의 출력 미리보기 패널이 항상 같은 결과를 보여주도록 공유한다.
    /// </summary>
    public static string BuildCombinedItemDescription(IEnumerable<OfsOrderItem> items)
    {
        var lines = GetDescriptionLines(items);
        if (lines.Count == 0) return string.Empty;
        if (lines.Count == 1) return lines[0];

        return string.Join("   +   ", lines.Select(line => $"(({line}))"));
    }

    /// <summary>
    /// 한 묶음에 실제로 표시될 품목 줄 수입니다(택배송장 4줄 제한 초과 여부를 판단하는 데 사용).
    /// <see cref="BuildCombinedItemDescription"/>이 괄호+플러스로 합쳐버린 뒤에는 줄바꿈으로 셀 수
    /// 없으므로, 합치기 전의 줄 목록 개수를 그대로 쓴다.
    /// </summary>
    public static int CountDescriptionLines(IEnumerable<OfsOrderItem> items) => GetDescriptionLines(items).Count;

    private static List<string> GetDescriptionLines(IEnumerable<OfsOrderItem> items)
    {
        return items
            .Select(i => i.InvoiceLabel ?? $"{i.ProductName} {i.Quantity}개")
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
    }
}

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
    /// <summary>수량 표기 형식이 설정되지 않았을 때 쓰는 기본값(과거 동작과 동일: "표시명 수량개").</summary>
    private const string DefaultQuantityFormat = " ##개";

    public static string GetEffectiveGroupId(OfsOrderItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ShipmentGroupId)) return item.ShipmentGroupId;

        // 명시적으로 합포장을 지정하지 않았으면 항상 그 줄 단독으로 취급한다(기본값 = 그룹화 없음).
        // 이 키는 OutboundDetailTable에 ShipmentGroupKey로 영속 저장되므로(발주/출고 이력), 발주서를
        // 재로드해 다시 저장해도 같은 발주 줄이면 같은 값이 나와야 한다. 파일에서 로드된 줄은
        // SourceRowKey(파일명#엑셀행번호)가 있으므로 그걸 채널코드와 묶어 결정적 키로 쓴다.
        if (!string.IsNullOrWhiteSpace(item.SourceRowKey))
            return $"{item.ChannelCode}|{item.SourceRowKey}";

        // 수동 추가 등 SourceRowKey가 없는 항목은 객체 식별 해시로 폴백한다(같은 인스턴스에 대해서는
        // 항상 같은 값 — GC로 인스턴스가 옮겨져도 .NET이 동일하게 유지해준다). 이 경우 재로드 개념이
        // 없으므로(그 세션에서 새로 만든 행) 영속 키가 세션을 넘어 안정적이지 않을 수 있다.
        return $"__row_{RuntimeHelpers.GetHashCode(item)}";
    }

    /// <summary>
    /// 한 묶음(=한 송장)에 속한 모든 줄의 품목 표시문자열을 만든다. 줄이 1개면 그대로 보여주고,
    /// 합포장 등으로 2개 이상이면 "((품목A))   +   ((품목B))"처럼 괄호와 +로 구분해, 줄바꿈만으로는
    /// 송장에서 어디까지가 한 품목인지 헷갈린다는 피드백을 반영한다. 택배사 내보내기
    /// (CourierExporter)와 OFS의 출력 미리보기 패널이 항상 같은 결과를 보여주도록 공유한다.
    /// </summary>
    /// <param name="items">한 묶음에 속한 줄들입니다.</param>
    /// <param name="quantityFormat">
    /// 택배사별 수량 표기 형식("##"이 수량으로 치환됨, 예: "   ▶[##개]"). 생략하면 기본 형식을 쓴다.
    /// CourierExporter가 선택된 택배사의 설정을 전달하고, OFS 미리보기처럼 특정 택배사가 아직
    /// 정해지지 않은 화면은 기본 형식을 그대로 쓴다.
    /// </param>
    public static string BuildCombinedItemDescription(IEnumerable<OfsOrderItem> items, string? quantityFormat = null)
    {
        var lines = GetDescriptionLines(items, quantityFormat);
        if (lines.Count == 0) return string.Empty;
        if (lines.Count == 1) return lines[0];

        return string.Join("   +   ", lines.Select(line => $"(({line}))"));
    }

    /// <summary>
    /// 한 묶음에 실제로 표시될 품목 줄 수입니다(택배송장 4줄 제한 초과 여부를 판단하는 데 사용).
    /// <see cref="BuildCombinedItemDescription"/>이 괄호+플러스로 합쳐버린 뒤에는 줄바꿈으로 셀 수
    /// 없으므로, 합치기 전의 줄 목록 개수를 그대로 쓴다.
    /// </summary>
    public static int CountDescriptionLines(IEnumerable<OfsOrderItem> items) => GetDescriptionLines(items, null).Count;

    private static List<string> GetDescriptionLines(IEnumerable<OfsOrderItem> items, string? quantityFormat)
    {
        var itemList = items.ToList();
        // 합포장(한 묶음에 품목이 2건 이상)이면 작업자가 다중건 포장임을 한눈에 알아챌 수 있도록,
        // 각 줄의 수량 표기 앞뒤에 "xx"를 붙인다(사용자 요청 — 일렬로만 나오면 구분이 어렵다는 피드백).
        var isMultiItemGroup = itemList.Count >= 2;

        return itemList
            .Select(i => i.InvoiceLabel ?? BuildAutoLabel(i, quantityFormat, isMultiItemGroup))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
    }

    private static string BuildAutoLabel(OfsOrderItem item, string? quantityFormat, bool isMultiItemGroup)
    {
        var name = !string.IsNullOrWhiteSpace(item.InvoiceDisplayName) ? item.InvoiceDisplayName : item.ProductName;
        var quantityTag = FormatQuantityTag(quantityFormat, item.Quantity);
        if (isMultiItemGroup) quantityTag = $"xx{quantityTag}xx";

        return $"{name}{quantityTag}";
    }

    private static readonly Regex TrailingDigits = new(@"\d+$", RegexOptions.Compiled);

    /// <summary>
    /// 한 주문을 여러 송장으로 분리배송할 때(수량 분리/묶음 해제/줄 복사 등) 각 송장의 수취인명 뒤에
    /// 1,2,3...을 붙인다. 택배사 시스템에 업로드할 때 수취인명(+주소)이 같으면 서로 다른 송장이라도
    /// 자동으로 합포장 처리해버리는 문제가 있어(사용자 피드백), 분리배송 결과 송장이 2건 이상이면
    /// 수취인명을 구분되게 만들어야 한다. 반대로 송장이 다시 1건으로 합쳐지면(묶음 1개만 전달됨)
    /// 번호를 떼어 원래 이름으로 되돌린다.
    /// </summary>
    /// <param name="shipments">
    /// 분리/재구성 직후의 송장(묶음)들 — 각 묶음은 그 송장에 속한 줄들의 목록이다. 순서대로 1,2,3...
    /// 번호가 매겨지므로 호출 쪽에서 원하는 표시 순서로 전달해야 한다.
    /// </param>
    public static void RenumberSplitRecipients(IReadOnlyList<IReadOnlyList<OfsOrderItem>> shipments)
    {
        if (shipments.Count == 0) return;

        var baseName = StripRecipientSuffix(shipments[0].FirstOrDefault(i => !string.IsNullOrEmpty(i.Recipient))?.Recipient);
        if (string.IsNullOrEmpty(baseName)) return;

        if (shipments.Count == 1)
        {
            foreach (var item in shipments[0]) item.Recipient = baseName;
            return;
        }

        for (int i = 0; i < shipments.Count; i++)
        {
            var numbered = $"{baseName}{i + 1}";
            foreach (var item in shipments[i]) item.Recipient = numbered;
        }
    }

    private static string? StripRecipientSuffix(string? recipient) =>
        string.IsNullOrEmpty(recipient) ? recipient : TrailingDigits.Replace(recipient, "");

    private static string FormatQuantityTag(string? template, int quantity)
    {
        var effective = string.IsNullOrEmpty(template) ? DefaultQuantityFormat : template;

        // 수량이 2개 이상이면 작업자가 한눈에 다건임을 알아볼 수 있도록, 수량만큼 '*'를 대괄호
        // 바로 앞에 붙인다(예: "▶[##개]" + 수량 2 → "▶**[2개]", 수량 5 → "▶*****[5개]"). 대괄호가
        // 없는 형식(기본형식 등)은 대상이 아니다(기존 표시 유지).
        if (quantity >= 2)
        {
            var insertAt = effective.IndexOf('[');
            if (insertAt >= 0) effective = effective.Insert(insertAt, new string('*', quantity));
        }

        return effective.Replace("##", quantity.ToString());
    }
}

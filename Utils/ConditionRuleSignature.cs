using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// 조건부 매핑 규칙의 "중복 여부"를 판단하기 위한 시그니처를 만든다. 대상SKU가 같고 상세조건
/// 집합이 완전히 같으면(순서가 달라도, 예: A AND B와 B AND A) 같은 시그니처를 갖는다. Operator/
/// Logic까지 포함하므로 비교연산자나 AND/OR가 다르면 다른 규칙으로 본다.
/// </summary>
public static class ConditionRuleSignature
{
    public static string Build(string? targetSku, IEnumerable<MappingConditionDetail> details)
    {
        var targetSkuPart = (targetSku ?? string.Empty).Trim().ToUpperInvariant();
        var detailParts = details
            .Select(d => $"{d.HeaderField}:{(d.RawFieldName ?? string.Empty).Trim().ToUpperInvariant()}:{d.Operator}:{(d.TargetValue ?? string.Empty).Trim().ToUpperInvariant()}:{d.Logic}")
            .OrderBy(s => s, StringComparer.Ordinal);

        return targetSkuPart + "|" + string.Join(";", detailParts);
    }
}

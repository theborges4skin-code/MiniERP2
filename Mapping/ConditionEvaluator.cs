using MiniERP2.Models;

namespace MiniERP2.Mapping;

/// <summary>
/// 조건부 매핑 규칙 하나에 속한 여러 상세조건(AND/OR)을 주문 항목에 대해 평가합니다.
/// 레거시(구버전 MiniERP) RuleConditionDetailTable과 동일한 의미를 재현한다(임의 단순화 금지).
/// </summary>
public static class ConditionEvaluator
{
    public static bool Matches(List<MappingConditionDetail> details, OfsOrderItem item) =>
        Matches(details, field => GetFieldValue(item, field));

    /// <summary>
    /// 마감/이익분석창에서 "예상 매칭 건수"를 정산파일 기준으로도 미리볼 수 있도록 SettlementData에
    /// 대해서도 같은 규칙을 평가한다(발주서가 없어도 정산파일만으로 미리보기가 가능해진다).
    /// </summary>
    public static bool Matches(List<MappingConditionDetail> details, SettlementData data) =>
        Matches(details, field => GetFieldValue(data, field));

    private static bool Matches(List<MappingConditionDetail> details, Func<StdField, string?> getFieldValue)
    {
        if (details.Count == 0) return false;

        // 레거시 데이터는 한 규칙 내 모든 상세조건이 같은 Logic(전부 AND 또는 전부 OR)인 경우가 기본이다.
        var firstLogic = details[0].Logic;
        if (details.All(d => d.Logic == firstLogic))
        {
            return firstLogic == ConditionLogic.And
                ? details.All(d => EvaluateSingle(d, getFieldValue))
                : details.Any(d => EvaluateSingle(d, getFieldValue));
        }

        // Logic이 섞여 있으면 왼쪽부터 순서대로 결합한다(가장 직관적인 기본 동작).
        var result = EvaluateSingle(details[0], getFieldValue);
        for (int i = 1; i < details.Count; i++)
        {
            var current = EvaluateSingle(details[i], getFieldValue);
            result = details[i].Logic == ConditionLogic.And ? result && current : result || current;
        }
        return result;
    }

    private static bool EvaluateSingle(MappingConditionDetail detail, Func<StdField, string?> getFieldValue)
    {
        var value = getFieldValue(detail.HeaderField) ?? string.Empty;
        return detail.Operator switch
        {
            ConditionOperator.Contains => value.Contains(detail.TargetValue, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.NotContains => !value.Contains(detail.TargetValue, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.Equals => string.Equals(value, detail.TargetValue, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static string? GetFieldValue(OfsOrderItem item, StdField field) => field switch
    {
        StdField.ProductName => item.ProductName,
        StdField.OptionName => item.OptionName,
        StdField.Quantity => item.Quantity.ToString(),
        StdField.ProductNo => item.OrderNo,
        StdField.Recipient => item.Recipient,
        StdField.Phone => item.Phone,
        StdField.Address => item.Address,
        _ => null,
    };

    private static string? GetFieldValue(SettlementData data, StdField field) => field switch
    {
        StdField.ProductName => data.ProductName,
        StdField.OptionName => data.OptionName,
        StdField.Quantity => data.Qty.ToString(),
        StdField.TrackingNo => data.TrackingNo,
        _ => null,
    };
}

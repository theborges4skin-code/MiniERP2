using MiniERP2.Models;

namespace MiniERP2.Mapping;

/// <summary>
/// 광고매핑 조건부 규칙의 다중 상세조건(AND/OR)을 광고비 행에 대해 평가합니다. 일반 매핑의
/// ConditionEvaluator와 같은 결합 규칙(전부 같은 Logic이면 AND-all/OR-any, 섞여있으면 왼쪽부터
/// 순서대로 결합)을 따르되, 비교 대상이 AdSpendItem/AdStdField이고 연산자도 더 많습니다
/// (레거시 ad_engine.py가 광고비 수치 비교/0원 제외까지 지원했기 때문).
/// </summary>
public static class AdConditionEvaluator
{
    public static bool Matches(List<AdConditionDetail> details, AdSpendItem item)
    {
        if (details.Count == 0) return false;

        var firstLogic = details[0].Logic;
        if (details.All(d => d.Logic == firstLogic))
        {
            return firstLogic == ConditionLogic.And
                ? details.All(d => EvaluateSingle(d, item))
                : details.Any(d => EvaluateSingle(d, item));
        }

        var result = EvaluateSingle(details[0], item);
        for (int i = 1; i < details.Count; i++)
        {
            var current = EvaluateSingle(details[i], item);
            result = details[i].Logic == ConditionLogic.And ? result && current : result || current;
        }
        return result;
    }

    /// <summary>예외 규칙(행 필터) 1건이 이 행에 매치되는지 확인합니다. 조건 평가 로직은 동일합니다.</summary>
    public static bool MatchesException(AdExceptionRule rule, AdSpendItem item) =>
        EvaluateSingle(new AdConditionDetail { HeaderField = rule.HeaderField, Operator = rule.Operator, TargetValue = rule.TargetValue }, item);

    private static bool EvaluateSingle(AdConditionDetail detail, AdSpendItem item)
    {
        var rawValue = GetFieldValue(item, detail.HeaderField) ?? string.Empty;

        if (detail.Operator == AdConditionOperator.IsZero)
        {
            return decimal.TryParse(rawValue.Replace(",", ""), out var numeric) ? numeric == 0 : rawValue.Trim() == "0";
        }

        if (TryGetNumericOperator(detail.Operator, out _) && decimal.TryParse(rawValue.Replace(",", ""), out var leftNum) && decimal.TryParse(detail.TargetValue, out var rightNum))
        {
            return detail.Operator switch
            {
                AdConditionOperator.GreaterThan => leftNum > rightNum,
                AdConditionOperator.LessThan => leftNum < rightNum,
                AdConditionOperator.GreaterOrEqual => leftNum >= rightNum,
                AdConditionOperator.LessOrEqual => leftNum <= rightNum,
                _ => false,
            };
        }

        return detail.Operator switch
        {
            AdConditionOperator.Contains => rawValue.Contains(detail.TargetValue, StringComparison.OrdinalIgnoreCase),
            AdConditionOperator.NotContains => !rawValue.Contains(detail.TargetValue, StringComparison.OrdinalIgnoreCase),
            AdConditionOperator.Equals => string.Equals(rawValue, detail.TargetValue, StringComparison.OrdinalIgnoreCase),
            AdConditionOperator.NotEquals => !string.Equals(rawValue, detail.TargetValue, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static bool TryGetNumericOperator(AdConditionOperator op, out bool isNumeric)
    {
        isNumeric = op is AdConditionOperator.GreaterThan or AdConditionOperator.LessThan or AdConditionOperator.GreaterOrEqual or AdConditionOperator.LessOrEqual;
        return isNumeric;
    }

    private static string? GetFieldValue(AdSpendItem item, AdStdField field) => field switch
    {
        AdStdField.ProductName => item.ProductName,
        AdStdField.ProductId => item.ProductId,
        AdStdField.OptionName => item.OptionName,
        AdStdField.Cost => item.Cost.ToString(),
        AdStdField.Extra1 => item.Extra1,
        AdStdField.Extra2 => item.Extra2,
        _ => null,
    };
}

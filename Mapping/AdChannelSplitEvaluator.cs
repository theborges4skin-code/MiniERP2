using MiniERP2.Models;

namespace MiniERP2.Mapping;

/// <summary>
/// 광고비 채널 분리 선판정 규칙(prerules, AdChannelSplit_Spec.md §4.2)의 다중 상세조건을
/// 평가합니다. AdConditionEvaluator와 결합 규칙(AND-all/OR-any, 섞이면 왼쪽부터 순서대로 결합)은
/// 같지만, 비교 대상 필드가 고정된 AdStdField가 아니라 AdSpendItem.RawValues의 원본 헤더
/// 문자열입니다 — 광고 리포트 파일마다 헤더 구성이 달라 임의 헤더(예: "판매방식")를 조건으로
/// 걸어야 하기 때문입니다.
/// <para>
/// 레거시 ad_engine.py의 not_contains 버그(§4.5)를 피하기 위해, 조건의 헤더가 이 행의
/// RawValues에 아예 없으면(=이 파일에 그 열이 없음) 연산자와 무관하게 그 조건은 불성립(false)으로
/// 처리한다. 헤더가 있지만 값이 빈 문자열인 경우는 일반적인 빈 값 비교로 그대로 평가한다.
/// </para>
/// </summary>
public static class AdChannelSplitEvaluator
{
    public static bool Matches(List<AdChannelSplitPreruleDetail> details, AdSpendItem item)
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

    private static bool EvaluateSingle(AdChannelSplitPreruleDetail detail, AdSpendItem item)
    {
        if (item.RawValues == null || !item.RawValues.TryGetValue(detail.HeaderName, out var rawValue))
        {
            return false;
        }

        if (detail.Operator == AdConditionOperator.IsZero)
        {
            return decimal.TryParse(rawValue.Replace(",", ""), out var numeric) ? numeric == 0 : rawValue.Trim() == "0";
        }

        if (IsNumericOperator(detail.Operator) && decimal.TryParse(rawValue.Replace(",", ""), out var leftNum) && decimal.TryParse(detail.TargetValue, out var rightNum))
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

    private static bool IsNumericOperator(AdConditionOperator op) =>
        op is AdConditionOperator.GreaterThan or AdConditionOperator.LessThan or AdConditionOperator.GreaterOrEqual or AdConditionOperator.LessOrEqual;
}

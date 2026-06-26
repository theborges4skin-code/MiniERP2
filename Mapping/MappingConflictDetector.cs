using MiniERP2.Models;

namespace MiniERP2.Mapping;

/// <summary>
/// 동일한 주문에 2개 이상의 매핑 규칙이 동시에 적용되어 서로 다른 SKU를 가리키는
/// 충돌 상황을 감지합니다. 기획서 5.3절 '매핑 충돌 자동 감지'.
/// </summary>
public record MappingConflict(MappingRuleType RuleType, string KeyA, string TargetSkuA, string KeyB, string TargetSkuB);

public static class MappingConflictDetector
{
    /// <summary>
    /// 같은 우선순위(룰 타입) 안에서 서로 다른 SKU로 동시에 매칭될 수 있는 규칙 쌍을 찾습니다.
    /// Exact/Temp는 완전일치 매칭이므로 동일 키일 때만 충돌이고,
    /// Exception/Condition은 부분일치(Contains) 매칭이므로 한쪽 키가 다른 쪽 키의 부분문자열이면 충돌입니다.
    /// </summary>
    public static List<MappingConflict> Detect(MappingRuleType ruleType, List<MappingRule> rules)
    {
        var conflicts = new List<MappingConflict>();
        var exactMatch = ruleType is MappingRuleType.Exact or MappingRuleType.Temp;

        for (int i = 0; i < rules.Count; i++)
        {
            for (int j = i + 1; j < rules.Count; j++)
            {
                var a = rules[i];
                var b = rules[j];
                if (string.IsNullOrWhiteSpace(a.Key) || string.IsNullOrWhiteSpace(b.Key)) continue;
                if (string.Equals(a.TargetSku, b.TargetSku, StringComparison.OrdinalIgnoreCase)) continue;

                var overlaps = exactMatch
                    ? string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase)
                    : a.Key.Contains(b.Key, StringComparison.OrdinalIgnoreCase) || b.Key.Contains(a.Key, StringComparison.OrdinalIgnoreCase);

                if (overlaps)
                {
                    conflicts.Add(new MappingConflict(ruleType, a.Key, a.TargetSku, b.Key, b.TargetSku));
                }
            }
        }

        return conflicts;
    }

    /// <summary>
    /// 충돌에 관련된 모든 키를 추출합니다(그리드 행 강조용).
    /// </summary>
    public static HashSet<string> GetConflictingKeys(List<MappingConflict> conflicts)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var conflict in conflicts)
        {
            keys.Add(conflict.KeyA);
            keys.Add(conflict.KeyB);
        }
        return keys;
    }
}

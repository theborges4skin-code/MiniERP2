using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Mapping;

/// <summary>
/// 광고비 행을 상품그룹에 매핑합니다(레거시 SalesManagerV2의 ad_engine.py와 동일한 우선순위:
/// 예외(행 제외) > 임시(정확한 키 일치) > 조건부). 마스터SKU/CSKU 매핑(SkuMapper)과 달리
/// 1:1 매핑 단계가 없습니다 — 광고매핑은 상품그룹 단위로만 분류하면 충분하기 때문입니다.
/// </summary>
public class AdMappingEngine
{
    private readonly List<AdMappingRule> _tempRules;
    private readonly List<AdExceptionRule> _exceptionRules;
    private readonly List<(AdMappingRule Rule, List<AdConditionDetail> Details)> _conditionRules;

    public AdMappingEngine(AdMappingRepository repository, string channelCode)
    {
        _tempRules = repository.GetTempRules(channelCode);
        _exceptionRules = repository.GetExceptionRules(channelCode);
        _conditionRules = repository.GetConditionRules(channelCode)
            .Select(r => (r, repository.GetConditionDetails(r.Id)))
            .ToList();
    }

    public void ApplyMapping(AdSpendItem item)
    {
        // 1) 예외(합계/소계 행 등 계산에서 제외)
        if (_exceptionRules.Any(rule => AdConditionEvaluator.MatchesException(rule, item)))
        {
            item.MappedGroup = null;
            item.MatchType = "예외처리";
            item.Status = "제외(합계 등)";
            return;
        }

        // 2) 임시 매핑(정확한 키 일치 — 상품명_옵션_상품번호)
        var key = BuildKey(item);
        var tempMatch = _tempRules.FirstOrDefault(r => r.Key == key);
        if (tempMatch != null)
        {
            item.MappedGroup = tempMatch.TargetGroup;
            item.MatchType = "임시";
            item.Status = "매핑(임시)";
            return;
        }

        // 3) 조건부 매핑
        foreach (var (rule, details) in _conditionRules)
        {
            if (details.Count == 0) continue;
            if (AdConditionEvaluator.Matches(details, item))
            {
                item.MappedGroup = rule.TargetGroup;
                item.MatchType = "조건부";
                item.Status = "매핑(조건)";
                return;
            }
        }

        item.MappedGroup = null;
        item.MatchType = null;
        item.Status = "매핑 실패";
    }

    /// <summary>임시 매핑의 키는 상품명_옵션_상품번호 조합입니다(레거시와 동일한 키 형식).</summary>
    public static string BuildKey(AdSpendItem item) => $"{item.ProductName}_{item.OptionName}_{item.ProductId}";
}

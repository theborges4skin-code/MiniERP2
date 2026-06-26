using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Mapping;

/// <summary>
/// 기획서 5.3절의 우선순위에 따라 SKU 매핑을 수행하는 엔진입니다.
/// </summary>
public class SkuMapper
{
    /// <summary>
    /// 레거시(구버전 MiniERP) 시스템에서 "예외처리 = 매핑 대상에서 제외"를 의미하던 표시값.
    /// 배송비/수수료 등 실제 상품이 아닌 주문 행을 자동으로 걸러내기 위해 사용한다.
    /// </summary>
    public const string ExcludedTargetSku = "[EXCLUDED]";

    private readonly Dictionary<MappingRuleType, List<MappingRule>> _rules;

    /// <summary>
    /// 지정된 채널의 모든 매핑 규칙을 로드하여 SkuMapper를 초기화합니다.
    /// </summary>
    /// <param name="mappingRepository">매핑 규칙을 가져올 Repository</param>
    /// <param name="channelCode">매핑할 채널 코드</param>
    public SkuMapper(MappingRepository mappingRepository, string channelCode)
    {
        _rules = new Dictionary<MappingRuleType, List<MappingRule>>
        {
            [MappingRuleType.Exception] = mappingRepository.GetRules(MappingRuleType.Exception, channelCode),
            [MappingRuleType.Exact] = mappingRepository.GetRules(MappingRuleType.Exact, channelCode),
            [MappingRuleType.Temp] = mappingRepository.GetRules(MappingRuleType.Temp, channelCode),
            [MappingRuleType.Condition] = mappingRepository.GetRules(MappingRuleType.Condition, channelCode)
        };
    }

    /// <summary>
    /// 주문 항목에 대한 SKU를 매핑합니다.
    /// </summary>
    /// <param name="item">매핑할 주문 항목</param>
    public void ApplyMapping(OfsOrderItem item)
    {
        // 매핑을 위한 키 생성 (상품명 + 옵션명)
        var key = (item.ProductName ?? "") + (item.OptionName ?? "");
        if (string.IsNullOrWhiteSpace(key))
        {
            item.Status = "매핑 키 없음";
            return;
        }

        // 기획서 5.3절 우선순위: 예외 > 1:1 > 임시 > 조건부
        if (TryMap(key, MappingRuleType.Exception, exactMatch: false, out var sku))
        {
            if (sku == ExcludedTargetSku)
            {
                // 배송비/수수료 안내 행 등 상품이 아닌 주문 행 — SKU를 매핑하지 않고 처리 대상에서 제외한다.
                item.MappedSku = null;
                item.Status = "제외(배송비 등)";
                return;
            }

            item.MappedSku = sku;
            item.Status = "매핑(예외)";
        }
        else if (TryMap(key, MappingRuleType.Exact, exactMatch: true, out sku))
        {
            item.MappedSku = sku;
            item.Status = "매핑(1:1)";
        }
        else if (TryMap(key, MappingRuleType.Temp, exactMatch: true, out sku))
        {
            item.MappedSku = sku;
            item.Status = "매핑(임시)";
        }
        else if (TryMap(key, MappingRuleType.Condition, exactMatch: false, out sku))
        {
            item.MappedSku = sku;
            item.Status = "매핑(조건)";
        }
        else
        {
            item.Status = "매핑 실패";
        }
    }

    private bool TryMap(string key, MappingRuleType ruleType, bool exactMatch, out string? targetSku)
    {
        var match = exactMatch
            ? _rules[ruleType].FirstOrDefault(r => key.Equals(r.Key, StringComparison.OrdinalIgnoreCase))
            : _rules[ruleType].FirstOrDefault(r => key.Contains(r.Key, StringComparison.OrdinalIgnoreCase));

        targetSku = match?.TargetSku;
        return match != null;
    }
}
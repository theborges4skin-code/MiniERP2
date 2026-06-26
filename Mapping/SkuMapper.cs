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
    private readonly Dictionary<long, List<MappingConditionDetail>> _conditionDetailsByRuleId;
    private readonly Dictionary<string, ChannelSkuModel> _channelSkusByMsku;

    /// <summary>
    /// 지정된 채널의 모든 매핑 규칙을 로드하여 SkuMapper를 초기화합니다.
    /// </summary>
    /// <param name="mappingRepository">매핑 규칙을 가져올 Repository</param>
    /// <param name="channelCode">매핑할 채널 코드</param>
    /// <param name="channelSkuRepository">
    /// 채널별 SKU 설정(송장표시명 등)을 가져올 Repository. 생략하면 InvoiceLabel을 채우지 않는다
    /// (기존 호출부와의 호환을 위해 선택 인자로 둠).
    /// </param>
    public SkuMapper(MappingRepository mappingRepository, string channelCode, ChannelSkuRepository? channelSkuRepository = null)
    {
        _rules = new Dictionary<MappingRuleType, List<MappingRule>>
        {
            [MappingRuleType.Exception] = mappingRepository.GetRules(MappingRuleType.Exception, channelCode),
            [MappingRuleType.Exact] = mappingRepository.GetRules(MappingRuleType.Exact, channelCode),
            [MappingRuleType.Temp] = mappingRepository.GetRules(MappingRuleType.Temp, channelCode),
            [MappingRuleType.Condition] = mappingRepository.GetRules(MappingRuleType.Condition, channelCode)
        };
        _conditionDetailsByRuleId = mappingRepository.GetConditionDetailsByChannel(channelCode);
        _channelSkusByMsku = (channelSkuRepository ?? new ChannelSkuRepository())
            .GetAllByChannel(channelCode)
            .ToDictionary(c => c.Msku, StringComparer.OrdinalIgnoreCase);
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
        else if (TryMapCondition(item, key, out sku))
        {
            item.MappedSku = sku;
            item.Status = "매핑(조건)";
        }
        else
        {
            item.Status = "매핑 실패";
        }

        item.InvoiceLabel = BuildInvoiceLabel(item);
    }

    /// <summary>
    /// 택배사 출력양식의 "품목"란에 쓸 간결한 표시 문자열을 만든다. 채널-SKU(CSKU)에 송장표시명이
    /// 설정되어 있으면 그 이름과 수량을 조합해 반환하고, 설정이 없으면 null을 반환해 호출 측(택배사
    /// 양식 설정)이 원본 상품명 등 다른 속성을 그대로 쓸 수 있게 한다. 발주서마다 상품명/옵션명
    /// 구조가 제각각이라도, 송장에는 채널별로 정해둔 간결한 이름만 나가게 하기 위한 것이다.
    /// </summary>
    private string? BuildInvoiceLabel(OfsOrderItem item)
    {
        if (string.IsNullOrEmpty(item.MappedSku)) return null;
        if (!_channelSkusByMsku.TryGetValue(item.MappedSku, out var csku)) return null;
        if (string.IsNullOrWhiteSpace(csku.InvoiceDisplayName)) return null;

        return $"{csku.InvoiceDisplayName} {item.Quantity}개";
    }

    private bool TryMap(string key, MappingRuleType ruleType, bool exactMatch, out string? targetSku)
    {
        var match = exactMatch
            ? _rules[ruleType].FirstOrDefault(r => key.Equals(r.Key, StringComparison.OrdinalIgnoreCase))
            : _rules[ruleType].FirstOrDefault(r => key.Contains(r.Key, StringComparison.OrdinalIgnoreCase));

        targetSku = match?.TargetSku;
        return match != null;
    }

    /// <summary>
    /// 조건부 매핑 규칙을 평가한다. 다중 상세조건(AND/OR)이 있는 규칙은 ConditionEvaluator로
    /// 평가하고, 상세조건이 없는 기존 단순 규칙은 기존처럼 상품명+옵션명 키에 대한 Contains로 평가한다.
    /// </summary>
    private bool TryMapCondition(OfsOrderItem item, string key, out string? targetSku)
    {
        foreach (var rule in _rules[MappingRuleType.Condition])
        {
            if (_conditionDetailsByRuleId.TryGetValue(rule.Id, out var details) && details.Count > 0)
            {
                if (ConditionEvaluator.Matches(details, item))
                {
                    targetSku = rule.TargetSku;
                    return true;
                }
            }
            else if (!string.IsNullOrWhiteSpace(rule.Key) && key.Contains(rule.Key, StringComparison.OrdinalIgnoreCase))
            {
                targetSku = rule.TargetSku;
                return true;
            }
        }

        targetSku = null;
        return false;
    }
}
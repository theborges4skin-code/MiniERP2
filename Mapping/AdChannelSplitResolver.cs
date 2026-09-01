using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Mapping;

/// <summary>
/// 광고비 행을 캠페인 기준으로 하위채널에 분류합니다(AdChannelSplit_Spec.md 전체 — "캠페인 → 채널"
/// 축, 기존 AdMappingEngine의 "상품+옵션 → 품목그룹" 축과는 완전히 독립적입니다). 판정 우선순위는
/// §4.3과 동일합니다: 1) 선판정 규칙(priority 오름차순, 첫 매칭) 2) 캠페인 인벤토리 완전일치
/// 3) 기본값(미분류).
/// </summary>
public class AdChannelSplitResolver
{
    public const string DefaultChannel = "미분류";

    private readonly List<string> _campaignSourceHeaders;
    private readonly List<(AdChannelSplitPrerule Rule, List<AdChannelSplitPreruleDetail> Details)> _prerules;
    private readonly Dictionary<(string Header, string Value), string> _inventory;

    public AdChannelSplitResolver(AdChannelSplitRepository repository, string channelCode, List<string> campaignSourceHeaders)
    {
        _campaignSourceHeaders = campaignSourceHeaders;
        _prerules = repository.GetPrerules(channelCode)
            .Where(r => r.Enabled)
            .Select(r => (r, repository.GetPreruleDetails(r.Id)))
            .ToList();
        _inventory = repository.GetInventory(channelCode)
            .ToDictionary(e => (e.HeaderName, e.Value), e => e.TargetChannel);
    }

    public void Resolve(AdSpendItem item)
    {
        DeriveCampaignKey(item);

        // 1) 선판정 규칙
        foreach (var (rule, details) in _prerules)
        {
            if (details.Count == 0) continue;
            if (AdChannelSplitEvaluator.Matches(details, item))
            {
                item.ResolvedChannel = rule.TargetChannel;
                item.ChannelMatchType = "선규칙";
                return;
            }
        }

        // 2) 캠페인 인벤토리 완전일치: (CAMPAIGN_SRC, CAMPAIGN_KEY) == (header, value)
        if (!string.IsNullOrEmpty(item.CampaignSrc) && !string.IsNullOrEmpty(item.CampaignKey)
            && _inventory.TryGetValue((item.CampaignSrc, item.CampaignKey), out var targetChannel))
        {
            item.ResolvedChannel = targetChannel;
            item.ChannelMatchType = "인벤토리";
            return;
        }

        // 3) 기본값
        item.ResolvedChannel = DefaultChannel;
        item.ChannelMatchType = DefaultChannel;
    }

    /// <summary>우선순위대로 첫 번째 비어있지 않은 캠페인 소스 헤더 값을 채택한다(§4.1).</summary>
    private void DeriveCampaignKey(AdSpendItem item)
    {
        item.CampaignSrc = null;
        item.CampaignKey = null;
        if (item.RawValues == null) return;

        foreach (var header in _campaignSourceHeaders)
        {
            if (item.RawValues.TryGetValue(header, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                item.CampaignSrc = header;
                item.CampaignKey = value;
                return;
            }
        }
    }
}

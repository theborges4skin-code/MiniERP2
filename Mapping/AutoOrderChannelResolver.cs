using MiniERP2.Models;

namespace MiniERP2.Mapping;

/// <summary>
/// 자동발주처리(Gmail 자동화) 연동 — 표준 프리셋으로 파싱한 주문의 channel_hint 문자열을 실제
/// 채널코드로 해석한다(02_자동발주처리_MiniERP2연동_설계.md §1-2). 표준 프리셋 자체는 항상 고정
/// 레이아웃으로 파싱하되(파일 01 §5), 이후 SKU 매핑은 각 채널에 이미 쌓인 규칙을 그대로 써야
/// 하므로 이 해석 결과로 ChannelCode를 치환하고 SkuMapper를 그 채널로 다시 만들어야 한다.
/// </summary>
public static class AutoOrderChannelResolver
{
    /// <summary>
    /// 전체 채널설정 중 "자동발주(표준) 파싱 프리셋"으로 지정된 채널을 찾는다. 정확히 1개가
    /// 지정되어 있어야 정상이며, 0개면 아직 프리셋이 준비되지 않은 것이다(null 반환).
    /// 여러 개가 지정되어 있으면(설정 실수) 첫 번째 것을 사용한다.
    /// </summary>
    public static ChannelConfig? FindStandardPreset(IEnumerable<ChannelConfig> channelConfigs) =>
        channelConfigs.FirstOrDefault(c => c.IsAutoOrderStandardPreset);

    /// <summary>
    /// channel_hint 문자열을 각 채널의 AutoOrderHints(쉼표 구분 별칭 목록)와 완전일치(대소문자
    /// 무시)로 비교해 실제 채널코드를 찾는다. 일치하는 채널이 없으면 null — 호출 측이 사용자에게
    /// 수동 채널 재지정을 요청해야 한다.
    /// </summary>
    public static string? ResolveChannelCode(IEnumerable<ChannelConfig> channelConfigs, string? channelHint)
    {
        if (string.IsNullOrWhiteSpace(channelHint)) return null;
        var trimmedHint = channelHint.Trim();

        foreach (var config in channelConfigs)
        {
            if (string.IsNullOrWhiteSpace(config.AutoOrderHints)) continue;

            var hints = config.AutoOrderHints.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (hints.Any(h => string.Equals(h, trimmedHint, StringComparison.OrdinalIgnoreCase)))
            {
                return config.ChannelCode;
            }
        }

        return null;
    }
}

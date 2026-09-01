namespace MiniERP2.Models;

/// <summary>
/// 광고비 채널 분리(§4.2 "prerules")의 규칙 1건 요약 정보입니다. 조건식이 먼저 맞으면
/// 캠페인 인벤토리(완전일치)보다 우선 적용됩니다(priority 오름차순, 첫 매칭 채택).
/// </summary>
public class AdChannelSplitPrerule
{
    public long Id { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string TargetChannel { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

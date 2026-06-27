namespace MiniERP2.Models;

/// <summary>
/// 광고매핑의 임시/조건부 규칙 1건의 요약 정보입니다(Key/대상그룹). 조건부 규칙은 이 요약 외에
/// 다중 상세조건(<see cref="AdConditionDetail"/>)을 추가로 가집니다.
/// </summary>
public class AdMappingRule
{
    public long Id { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string TargetGroup { get; set; } = string.Empty;
}

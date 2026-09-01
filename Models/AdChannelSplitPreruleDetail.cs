namespace MiniERP2.Models;

/// <summary>
/// 채널 분리 선판정 규칙(<see cref="AdChannelSplitPrerule"/>) 1건에 속하는 상세 조건입니다.
/// 일반 조건부 매핑(AdConditionDetail)과 달리 HeaderField가 고정된 AdStdField가 아니라
/// 광고비 파일의 원본 헤더 문자열입니다(예: "판매방식") — AdSpendItem.RawValues에서 값을 찾습니다.
/// </summary>
public class AdChannelSplitPreruleDetail
{
    public long Id { get; set; }
    public long RuleId { get; set; }
    public string HeaderName { get; set; } = string.Empty;
    public AdConditionOperator Operator { get; set; }
    public string TargetValue { get; set; } = string.Empty;
    public ConditionLogic Logic { get; set; } = ConditionLogic.And;
}

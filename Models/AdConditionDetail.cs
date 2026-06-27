namespace MiniERP2.Models;

/// <summary>
/// 광고매핑 조건부 규칙(AdMappingRule) 하나에 속하는 상세 조건 1개입니다. 여러 상세조건을
/// AND/OR로 조합해 평가합니다(일반 매핑의 MappingConditionDetail과 같은 구조, 필드/연산자만 다름).
/// </summary>
public class AdConditionDetail
{
    public long Id { get; set; }
    public long RuleId { get; set; }
    public AdStdField HeaderField { get; set; }
    public AdConditionOperator Operator { get; set; }
    public string TargetValue { get; set; } = string.Empty;
    public ConditionLogic Logic { get; set; } = ConditionLogic.And;
}

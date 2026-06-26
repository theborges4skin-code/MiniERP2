namespace MiniERP2.Models;

/// <summary>
/// 조건부 매핑 규칙(MappingRule, RuleType=Condition) 하나에 속하는 상세 조건 1개입니다.
/// 한 규칙은 여러 상세 조건을 AND/OR로 조합해 평가합니다(레거시 RuleConditionDetailTable 이관용).
/// </summary>
public class MappingConditionDetail
{
    public long Id { get; set; }
    public long RuleId { get; set; }
    public StdField HeaderField { get; set; }
    public ConditionOperator Operator { get; set; }
    public string TargetValue { get; set; } = string.Empty;
    public ConditionLogic Logic { get; set; } = ConditionLogic.And;
}

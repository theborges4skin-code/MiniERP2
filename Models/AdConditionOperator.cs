namespace MiniERP2.Models;

/// <summary>
/// 광고매핑 조건부 규칙의 비교 연산자입니다. 일반 매핑(ConditionOperator)보다 연산자가 더 많은데,
/// 레거시 SalesManagerV2의 광고매핑(ad_engine.py)이 광고비/수치 비교(이상/이하/0 등)까지
/// 지원했기 때문입니다(광고비가 0인 캠페인 제외 등의 실무 용도).
/// </summary>
public enum AdConditionOperator
{
    Contains,
    NotContains,
    Equals,
    NotEquals,
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,
    IsZero,
}

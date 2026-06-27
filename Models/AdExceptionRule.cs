namespace MiniERP2.Models;

/// <summary>
/// 광고비 파일에서 계산 대상이 아닌 행(합계/소계 행 등)을 걸러내는 규칙입니다. 일반 매핑의
/// 예외처리(특정 상품 제외)와는 달리, 광고매핑 예외는 "어느 헤더가 어떤 값을 가진 행을
/// 제외한다"는 행 단위 필터입니다(레거시 ad_exception_rules.json과 동일한 형태:
/// header/op/val — 예: AD_PRODUCT_ID Contains "합계").
/// </summary>
public class AdExceptionRule
{
    public long Id { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public AdStdField HeaderField { get; set; }
    public AdConditionOperator Operator { get; set; }
    public string TargetValue { get; set; } = string.Empty;
}

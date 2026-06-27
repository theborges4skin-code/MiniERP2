using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// SettlementData 한 행이 "확인이 필요한 건"(미매핑/매핑실패/원가정보없음)인지 판단한다.
/// 매핑테이블의 예외규칙으로 의도적으로 제외된 행("제외(배송비 등)")은 문제가 아니므로 포함하지 않는다.
/// </summary>
public static class SettlementRowStatus
{
    public static bool IsUnresolved(SettlementData data) =>
        string.IsNullOrWhiteSpace(data.Msku) || data.Status is "매핑 실패" or "매핑 키 없음" or "원가 정보 없음";

    public static bool IsExcludedByExceptionRule(SettlementData data) =>
        data.Status == "제외(배송비 등)";
}

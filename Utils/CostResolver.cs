namespace MiniERP2.Utils;

/// <summary>
/// 발주/출고 라인의 원가(kg당) 우선순위 규칙을 한 곳에 모은다(CSKU제조원가_개별관리_개발기획서.md §4.4).
/// 우선순위: 매입처 매입가 &gt; CSKU 개별 원가(오버라이드) &gt; 마스터DB 대표원가.
/// 값 3개를 우선순위로 병합하는 순수 함수로만 두고, 리포지토리 조회는 호출부가 계속 담당한다 —
/// <see cref="MiniERP2.Forms.OutboundHistoryForm"/>은 미확정 상태에서 매입가를 라이브로 조회하고,
/// <see cref="MiniERP2.Database.PartnerClosingRepository"/>는 이미 스냅샷된 값을 그대로 쓰는 등
/// "매입가를 얻는 방법" 자체가 호출부마다 달라서, 조회까지 흡수하면 기존 동작이 바뀌기 때문이다.
/// </summary>
public static class CostResolver
{
    public static decimal Resolve(decimal? purchasePrice, decimal? costPriceOverride, decimal masterCostPrice)
        => purchasePrice ?? costPriceOverride ?? masterCostPrice;
}

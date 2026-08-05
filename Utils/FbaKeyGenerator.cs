namespace MiniERP2.Utils;

/// <summary>
/// 아마존 FBA 발주의 박스 단위 매칭키(고객주문번호) 채번 규칙을 계산한다(기획서 §7.1). FBO와 달리
/// 수취지가 1곳 고정이라 반품부성명으로는 박스를 구분할 수 없어, ShipmentId와 총박스수/박스순번을
/// 조합한 문자열을 쓴다. 박스가 추가/삭제될 때마다 호출 측(FbaOrderForm)이 전체 박스에 대해
/// 다시 계산해야 한다(총박스수가 바뀌므로).
/// </summary>
public static class FbaKeyGenerator
{
    /// <summary>박스 단위 매칭키를 만든다. ShipmentId가 비어있으면 그 자리만 공란으로 남는다.
    /// 예: ("FBA15ABCDEFG", 6, 3) → "[SEND] FBA15ABCDEFG 총 6박스중 3번째",
    ///     (null, 6, 3) → "[SEND]  총 6박스중 3번째"(이중 공백).</summary>
    public static string BuildMatchKey(string? shipmentId, int totalBoxes, int boxSeq)
        => $"[SEND] {shipmentId} 총 {totalBoxes}박스중 {boxSeq}번째";

    /// <summary>결과 파일/수동 입력에서 읽은 고객주문번호를 매칭 전에 정규화한다(앞뒤 공백 제거).</summary>
    public static string NormalizeMatchKey(string? raw) => (raw ?? string.Empty).Trim();

    /// <summary>
    /// 현재 남아있는 박스번호들(삭제로 중간이 비었을 수 있음)을 1..N으로 다시 채번하기 위한
    /// 옛번호→새번호 매핑을 만든다(기획서 §4 — 박스 추가·삭제 시 carton no. 재채번). 순서는
    /// 오름차순으로 유지한다. 호출 측(FbaOrderForm)이 이 매핑으로 각 행의 BoxSeq를 갱신한 뒤,
    /// 새 총박스수(맵의 개수)로 <see cref="BuildMatchKey"/>를 다시 호출해야 한다.
    /// </summary>
    public static Dictionary<int, int> BuildBoxSeqRenumberMap(IEnumerable<int> currentBoxSeqs)
    {
        var ordered = currentBoxSeqs.Distinct().OrderBy(x => x).ToList();
        var map = new Dictionary<int, int>();
        for (int i = 0; i < ordered.Count; i++) map[ordered[i]] = i + 1;
        return map;
    }
}

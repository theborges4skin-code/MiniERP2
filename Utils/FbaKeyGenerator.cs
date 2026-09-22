using System.Text.RegularExpressions;

namespace MiniERP2.Utils;

/// <summary>
/// 매칭키(고객주문번호)에서 뽑아낸 구성요소 — Shipment ID와 총박스수/박스순번(§7.1).
/// 택배사 운송장 결과 파일의 고객주문번호 칸을 이 단위로 해석해 박스를 특정한다.
/// </summary>
/// <param name="ShipmentId">아마존 Shipment ID. 발주 시점에 미입력이었으면 빈 문자열.</param>
/// <param name="TotalBoxes">키를 만든 시점의 총 박스수. 박스 추가/삭제로 사후에 바뀔 수 있어 매칭에는 쓰지 않는다.</param>
/// <param name="BoxSeq">박스 순번(1부터).</param>
public readonly record struct FbaMatchKeyParts(string ShipmentId, int TotalBoxes, int BoxSeq);

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
    /// "[SEND] {ShipmentId} 총 {N}박스중 {M}번째" 형태의 고객주문번호를 구성요소로 분해한다.
    /// 택배사 운송장 결과 파일("운송장출력데이터 상세")에는 어떤 운송장이 어떤 박스인지 직접적인
    /// 단서가 없고 고객주문번호 칸에만 이 문자열이 실려오므로, 문자열 전체를 그대로 비교하는 대신
    /// 여기서 뽑은 (ShipmentId, BoxSeq)로 박스를 특정한다. 총박스수는 박스 추가·삭제 시 재채번되어
    /// 결과 파일 발행 이후 달라질 수 있으므로 매칭 기준에서 제외한다.
    /// 엑셀/수기 입력 과정에서 끼어드는 표기 차이(대소문자, 여분 공백, 줄바꿈, 전각공백·NBSP,
    /// ShipmentId를 감싼 따옴표)는 흡수한다. 형식이 다르면 false — FBO(#FBO26071301-03 등)나
    /// 일반 택배 건이 같은 파일에 섞여 있어도 이 판정으로 걸러낼 수 있다.
    /// </summary>
    public static bool TryParseMatchKey(string? raw, out FbaMatchKeyParts parts)
    {
        parts = default;
        var match = MatchKeyPattern.Match(NormalizeWhitespace(raw));
        if (!match.Success) return false;

        // 총박스수/박스순번은 \d+ 로만 잡았으므로 비정상적으로 긴 숫자면 int 범위를 넘을 수 있다.
        if (!int.TryParse(match.Groups["total"].Value, out var total)) return false;
        if (!int.TryParse(match.Groups["seq"].Value, out var seq)) return false;

        var shipmentId = match.Groups["ship"].Value.Trim(QuoteChars).Trim();
        parts = new FbaMatchKeyParts(shipmentId, total, seq);
        return true;
    }

    /// <summary>
    /// 매칭키를 사전 조회용 키로 바꾼다 — 구조가 해석되면 (ShipmentId, BoxSeq)만 쓰고, 아니면
    /// 정규화한 원문 전체를 쓴다(수기로 다른 형식을 넣어둔 과거 박스도 계속 매칭되도록).
    /// </summary>
    public static string BuildLookupKey(string? raw)
        => TryParseMatchKey(raw, out var parts)
            ? $"SEND{parts.ShipmentId.ToUpperInvariant()}{parts.BoxSeq}"
            : $"RAW{NormalizeMatchKey(raw)}";

    private static readonly char[] QuoteChars = ['\'', '"', '‘', '’', '“', '”'];

    private static readonly Regex MatchKeyPattern = new(
        @"^\[\s*SEND\s*\]\s*(?<ship>.*?)\s*총\s*(?<total>\d+)\s*박스\s*중\s*(?<seq>\d+)\s*번째$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WhitespaceRunPattern = new(@"\s+", RegexOptions.Compiled);

    /// <summary>전각공백·NBSP·줄바꿈·탭을 보통 공백으로 바꾸고 연속 공백을 하나로 줄인다.</summary>
    private static string NormalizeWhitespace(string? raw)
        => raw == null ? string.Empty : WhitespaceRunPattern.Replace(raw.Replace(' ', ' ').Replace('　', ' '), " ").Trim();

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

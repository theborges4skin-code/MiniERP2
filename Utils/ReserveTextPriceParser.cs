namespace MiniERP2.Utils;

/// <summary>
/// <c>ItemTable.Reserve1</c>(권장소비자가, TEXT 자유입력)을 숫자로 파싱한다(간이마진계산기_개발기획서.md §5.1).
/// Reserve1은 규격 문자열 등 비숫자 값이 들어있을 수 있는 자유 텍스트라, 실패는 오류가 아니라
/// 정상 상황이다.
/// </summary>
public static class ReserveTextPriceParser
{
    /// <summary>콤마/공백/₩/원을 제거한 뒤 파싱한다. 파싱 실패 또는 0 이하이면 null(공란) 반환.</summary>
    public static decimal? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var cleaned = raw.Replace(",", "").Replace(" ", "").Replace("₩", "").Replace("원", "");
        if (!decimal.TryParse(cleaned, out var value)) return null;

        return value > 0 ? value : null;
    }
}

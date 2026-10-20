using System.Globalization;

namespace MiniERP2.Utils;

/// <summary>
/// 발주 그리드에 사람이 입력한 유통기한 문자열을 날짜로 해석한다(FBA 선적명세/작업지시서, 풀필먼트
/// 입고양식 공용). 그리드 힌트는 YYYYMMDD지만 실제 입력은 제각각이다 — "2028.07.07.", "2028. 7. 7."
/// (엑셀 한국어 날짜 표시를 그대로 복사), 그리고 아마존 출력 형식 자체를 미리 적어넣은
/// "SEP.07.2028.EXP"(끝 점 유무, 엑셀에서 붙여넣다 딸려온 탭 포함). 마지막 형식을 인식하지 못해
/// 선적명세의 유통기한 칸이 전부 공란으로 나가던 문제가 있었다(사용자 신고, 2026-10-21).
/// 공백/탭은 모두 지우고, 앞뒤의 "EXP" 표기와 끝 점을 떼어낸 뒤 아래 형식을 순서대로 시도한다.
/// "2028.09"처럼 일자가 없는 값은 어느 날로 볼지 정할 수 없어 일부러 해석하지 않는다.
/// </summary>
public static class ExpiryDateParser
{
    private static readonly string[] Formats =
    [
        "yyyyMMdd", "yyyy.M.d", "yyyy-M-d", "yyyy/M/d",
        "yyMMdd",
        "MMM.d.yyyy", "MMM-d-yyyy", "MMM/d/yyyy", "MMMd,yyyy",
    ];

    public static bool TryParse(string? text, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var normalized = string.Concat(text.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant().Trim('.');
        if (normalized.EndsWith("EXP")) normalized = normalized[..^3].TrimEnd('.');
        if (normalized.StartsWith("EXP")) normalized = normalized[3..].TrimStart('.', ':');

        return DateTime.TryParseExact(normalized, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>해석에 실패했을 때 출력물에 대신 쓸 원문(앞뒤 공백/탭 제거). 비어있으면 빈 문자열.</summary>
    public static string RawText(string? text) => text?.Trim() ?? string.Empty;
}

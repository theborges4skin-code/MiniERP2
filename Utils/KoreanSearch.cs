namespace MiniERP2.Utils;

/// <summary>
/// 프로그램 전체 검색창이 공통으로 쓰는 부분일치 판정. 대소문자 무시 부분일치에 더해
/// 한글 초성 검색을 지원한다 — "ㅁㅆ"는 "맘씨", "ㅁ씨건강"처럼 초성과 완성 글자를 섞어 써도 된다.
/// 검색어에 초성(ㄱ~ㅎ)이 하나도 없으면 기존과 똑같이 일반 부분일치만 한다.
/// </summary>
public static class KoreanSearch
{
    private const char HangulFirst = '가';
    private const char HangulLast = '힣';
    private const int SyllablesPerChoseong = 21 * 28;

    private static readonly char[] Choseongs =
    [
        'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ',
        'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ',
    ];

    public static bool IsChoseong(char c) => Array.IndexOf(Choseongs, c) >= 0;

    public static bool HasChoseong(string? query) => !string.IsNullOrEmpty(query) && query.Any(IsChoseong);

    /// <summary>완성형 한글 글자를 초성으로 바꾼 문자열("맘씨" → "ㅁㅆ"). 한글이 아닌 글자는 그대로 둔다.</summary>
    public static string ToChoseong(string text)
    {
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= HangulFirst && chars[i] <= HangulLast)
                chars[i] = Choseongs[(chars[i] - HangulFirst) / SyllablesPerChoseong];
        }
        return new string(chars);
    }

    /// <summary><paramref name="text"/>에 <paramref name="query"/>가 들어있는지(대소문자 무시, 초성 허용).
    /// 검색어가 비어 있으면 true, 대상이 비어 있으면 false.</summary>
    public static bool Matches(string? text, string? query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (!HasChoseong(query)) return false;

        for (int start = 0; start <= text.Length - query.Length; start++)
        {
            int j = 0;
            while (j < query.Length && CharMatches(text[start + j], query[j])) j++;
            if (j == query.Length) return true;
        }
        return false;
    }

    private static bool CharMatches(char t, char q)
    {
        if (t == q) return true;
        if (IsChoseong(q))
            return t >= HangulFirst && t <= HangulLast && Choseongs[(t - HangulFirst) / SyllablesPerChoseong] == q;
        return char.ToUpperInvariant(t) == char.ToUpperInvariant(q);
    }
}

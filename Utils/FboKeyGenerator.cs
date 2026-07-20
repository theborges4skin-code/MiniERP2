using System.Text.RegularExpressions;

namespace MiniERP2.Utils;

/// <summary>
/// FBO(네이버 풀필먼트) 발주의 반품부명(박스 식별자)·고객주문번호(박스 단위 매칭키) 채번 규칙을
/// 계산한다(기획서 §2.1/§4.2/§6.2). 반품부명이 같아야 풀필먼트사가 자동 합포장하므로, 박스가
/// 추가/삭제될 때마다 호출 측(FboOrderForm)이 매번 다시 계산해 전체 박스에 일관되게 적용해야 한다.
/// </summary>
public static class FboKeyGenerator
{
    private static readonly Regex SeqTokenPattern = new(@"\{seq:(0+)\}", RegexOptions.Compiled);

    /// <summary>"{name}{seq:00}" 형태의 포맷 문자열로 반품부명을 만든다. 예: ("{name}{seq:00}", "설레는", 1) → "설레는01".</summary>
    public static string FormatReceiverName(string format, string baseName, int seq)
    {
        var result = format.Replace("{name}", baseName);
        result = SeqTokenPattern.Replace(result, match => seq.ToString().PadLeft(match.Groups[1].Value.Length, '0'));
        return result;
    }

    /// <summary>발주번호(FBO-yyyyMMdd-순번)에서 일자별 순번만 뽑아낸다. 형식이 다르면 1을 반환한다.</summary>
    public static int ExtractDailySeq(string fboNo)
    {
        var lastDash = fboNo.LastIndexOf('-');
        if (lastDash < 0) return 1;
        return int.TryParse(fboNo[(lastDash + 1)..], out var seq) ? seq : 1;
    }

    /// <summary>박스 단위 매칭키(고객주문번호)를 만든다. 예: ("#FBO", 2026-07-13, 1, 3) → "#FBO26071301-03".</summary>
    public static string BuildMatchKey(string orderNoPrefix, DateTime orderDate, int dailySeq, int boxSeq)
        => $"{orderNoPrefix}{orderDate:yyMMdd}{dailySeq:00}-{boxSeq:00}";

    /// <summary>결과 파일에서 읽은 고객주문번호를 매칭 전에 정규화한다(선행 '#'·공백 제거).</summary>
    public static string NormalizeMatchKey(string? raw) => (raw ?? string.Empty).Trim().TrimStart('#');
}

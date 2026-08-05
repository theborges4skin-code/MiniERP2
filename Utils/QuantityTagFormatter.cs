namespace MiniERP2.Utils;

/// <summary>
/// 택배 발송용 출고이서의 "품목명" 칸에 수량을 강조 표시로 덧붙이는 공용 포맷터
/// (FboOrderExporter/FbaCourierExporter 공용, FBA발주관리_개발기획서.md §10 수정사항).
/// </summary>
public static class QuantityTagFormatter
{
    private const string QuantityTemplate = "▶[##개]";

    /// <summary>수량이 2개 이상이면 대괄호 바로 앞에 강조 표시를 붙여 합포장임을 한눈에 알아볼 수
    /// 있게 한다. 원래 수량만큼 '*'를 반복했으나(OFS와 같은 방식) 21개처럼 큰 수량에서 표시가
    /// 지나치게 길어진다는 지적에 따라 소문자 로마숫자(i=1, v=5, x=10, ...)로 축약했다.
    /// 예: 7개 → vii, 20개 → xx, 21개 → xxi.</summary>
    public static string FormatQuantityTag(int qty)
    {
        var effective = QuantityTemplate;
        if (qty >= 2)
        {
            var insertAt = effective.IndexOf('[');
            if (insertAt >= 0) effective = effective.Insert(insertAt, ToLowerRomanNumeral(qty));
        }
        return effective.Replace("##", qty.ToString());
    }

    private static readonly (int Value, string Numeral)[] RomanNumeralMap =
    [
        (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"),
        (100, "c"), (90, "xc"), (50, "l"), (40, "xl"),
        (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i"),
    ];

    private static string ToLowerRomanNumeral(int number)
    {
        var result = new System.Text.StringBuilder();
        foreach (var (value, numeral) in RomanNumeralMap)
        {
            while (number >= value)
            {
                result.Append(numeral);
                number -= value;
            }
        }
        return result.ToString();
    }
}

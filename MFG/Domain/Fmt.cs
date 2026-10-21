using System.Globalization;

namespace MiniERP2.MFG.Domain;

/// <summary>화면 숫자 표기 공용 규칙.</summary>
public static class Fmt
{
    /// <summary>금액: 소수 둘째 자리까지(원 단위 원가가 작아 소수가 의미 있음), 천 단위 구분.</summary>
    public static string Won(double v) => v.ToString("#,##0.##", CultureInfo.InvariantCulture);

    public static string Won(double? v) => v is null ? string.Empty : Won(v.Value);

    public static string Num(double v) => v.ToString("#,##0.###", CultureInfo.InvariantCulture);

    public static string Pct(double fraction) => (fraction * 100).ToString("0.####", CultureInfo.InvariantCulture) + "%";

    public static bool TryParse(string? text, out double value)
    {
        var s = (text ?? string.Empty).Replace(",", "").Replace("%", "").Replace("원", "").Trim();
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

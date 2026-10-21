using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace MiniERP2.MFG.Data;

/// <summary>
/// 코드 자동 채번. 같은 접두어의 기존 코드 중 가장 큰 번호 + 1을 쓰고, 자릿수는 기존 코드와
/// 최소 자릿수 중 큰 쪽에 맞춘다(예: RM137 다음 → RM138, C12 다음 → C13).
/// 엑셀에서 신규 원료마다 코드를 손으로 매기던 불편(사용자 요청)을 없애는 부분이다.
/// </summary>
public static partial class CodeGenerator
{
    public static string Next(SqliteConnection c, string table, string prefix, int minDigits, SqliteTransaction? tx = null)
    {
        using var cmd = MfgDb.Cmd(c, $"SELECT Code FROM {table} WHERE Code LIKE $p", tx, ("$p", prefix + "%"));
        using var r = cmd.ExecuteReader();
        var codes = new List<string>();
        while (r.Read()) codes.Add(r.GetString(0));
        return NextFrom(codes, prefix, minDigits);
    }

    public static string NextFrom(IEnumerable<string> existingCodes, string prefix, int minDigits)
    {
        int max = 0, width = minDigits;
        foreach (var code in existingCodes)
        {
            var m = DigitsRegex().Match(code);
            if (!code.StartsWith(prefix, StringComparison.Ordinal) || !m.Success || m.Index != prefix.Length) continue;
            max = Math.Max(max, int.Parse(m.Value, CultureInfo.InvariantCulture));
            width = Math.Max(width, m.Value.Length);
        }
        return prefix + (max + 1).ToString(new string('0', width), CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"\d+$")]
    private static partial Regex DigitsRegex();
}

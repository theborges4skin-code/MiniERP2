using System.Text.RegularExpressions;

namespace MiniERP2.Utils;

/// <summary>
/// 신규 채널 추가 시 채널 코드를 "CH001" 형식으로 자동 생성합니다.
/// 채널 코드는 화면에 노출되지 않는 내부 식별자이므로 사용자가 직접 입력할 필요가 없습니다.
/// </summary>
public static class ChannelCodeGenerator
{
    private static readonly Regex CodePattern = new(@"^CH(\d+)$", RegexOptions.IgnoreCase);

    public static string GenerateNext(IEnumerable<string> existingCodes)
    {
        var maxNumber = 0;
        foreach (var code in existingCodes)
        {
            var match = CodePattern.Match(code);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > maxNumber)
            {
                maxNumber = number;
            }
        }

        return $"CH{maxNumber + 1:D3}";
    }
}

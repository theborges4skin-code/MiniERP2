using System.Text.RegularExpressions;

namespace MiniERP2.Utils;

/// <summary>
/// 발주서의 품목이 마스터DB에 없을 때 등록할 임시 SKU를 "TEMP001" 형식으로 자동 생성합니다.
/// </summary>
public static class TempSkuGenerator
{
    private static readonly Regex CodePattern = new(@"^TEMP(\d+)$", RegexOptions.IgnoreCase);

    public static string GenerateNext(IEnumerable<string> existingSkus)
    {
        var maxNumber = 0;
        foreach (var sku in existingSkus)
        {
            var match = CodePattern.Match(sku);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > maxNumber)
            {
                maxNumber = number;
            }
        }

        return $"TEMP{maxNumber + 1:D3}";
    }
}

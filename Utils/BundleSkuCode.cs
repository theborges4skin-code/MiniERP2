using System.Text.RegularExpressions;

namespace MiniERP2.Utils;

/// <summary>
/// 1회성 대량구매처럼 마스터SKU를 따로 만들 필요 없는 묶음 건을 위한 코드 규칙:
/// "{마스터SKU}x{수량}" (예: 26c_lgmjnb72x47 = 26c_lgmjnb72 47개 묶음). 매핑 규칙의 대상 코드가
/// 마스터SKU/CSKU로 등록되어 있지 않을 때만 이 규칙으로 해석하며, 제조원가는 기준 마스터SKU의
/// 1개당 원가 × 수량이다. 이미 등록된 코드(예: 마스터SKU 26c_lgsp5x5)는 등록된 쪽이 우선이다.
/// </summary>
public static partial class BundleSkuCode
{
    [GeneratedRegex(@"^(?<base>.+)[xX](?<qty>\d+)$")]
    private static partial Regex Pattern();

    public static bool TryParse(string? code, out string baseSku, out int quantity)
    {
        baseSku = string.Empty;
        quantity = 0;
        if (string.IsNullOrWhiteSpace(code)) return false;

        var match = Pattern().Match(code.Trim());
        if (!match.Success || !int.TryParse(match.Groups["qty"].Value, out quantity) || quantity < 1)
        {
            quantity = 0;
            return false;
        }

        baseSku = match.Groups["base"].Value;
        return true;
    }
}

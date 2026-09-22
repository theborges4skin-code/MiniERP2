namespace MiniERP2.Utils;

/// <summary>
/// CSKU(채널별 SKU) 매핑 시 기본으로 제안할 CSKU 코드를 만듭니다. 사용자가 직접 편집할 수 있는
/// 출발점일 뿐이며, 같은 마스터SKU라도 채널의 옵션 구성에 따라 여러 CSKU로 나뉠 수 있으므로
/// 충돌 여부는 호출 측(ChannelSkuRepository)에서 확인해야 합니다.
/// </summary>
public static class CskuCodeGenerator
{
    /// <summary>
    /// "채널명 앞 3글자_마스터SKU" 형태의 기본 CSKU 코드를 만듭니다.
    /// 예: 채널명 "AAAAA", 마스터SKU "BBB" → "AAA_BBB".
    /// </summary>
    public static string BuildDefault(string channelName, string masterSku)
    {
        var prefix = string.IsNullOrEmpty(channelName)
            ? string.Empty
            : channelName[..Math.Min(3, channelName.Length)];

        return string.IsNullOrEmpty(prefix) ? masterSku : $"{prefix}_{masterSku}";
    }

    /// <summary>
    /// BuildDefault가 만든 코드가 이미 같은 채널의 다른 CSKU에 쓰이고 있으면(같은 채널명 접두사를
    /// 공유하는 다른 상품과 충돌) 번호를 붙여 피한다 — 기존 CSKU를 실수로 덮어쓰는 사고를 막는다.
    /// 온라인 거래처 취합의 CSKU 자동배정(PartnerConsolidationFileLoader)과 단가 미배정 탭의 신규
    /// CSKU 생성(PartnerConsolidationPriceEntryService)이 공유한다.
    /// </summary>
    public static string BuildUniqueDefault(string channelName, string masterSku, IEnumerable<string> existingCskuCodesInChannel)
    {
        var baseCode = BuildDefault(channelName, masterSku);
        var existing = new HashSet<string>(existingCskuCodesInChannel, StringComparer.Ordinal);
        if (!existing.Contains(baseCode))
            return baseCode;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseCode}_{suffix}";
            if (!existing.Contains(candidate))
                return candidate;
        }
    }
}

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
}

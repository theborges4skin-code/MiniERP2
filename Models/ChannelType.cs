namespace MiniERP2.Models;

public enum ChannelType
{
    General,
    CoupangGeneral,
    CoupangRocket,
    ElevenStreet,
    CoupangGrowth,
    AmazonUs,
    AmazonJp,
    Partner,

    /// <summary>B2B 매입/납품 거래처(§D1). 기존 Partner와 완전히 별도로 병존한다(§D8 — 흡수/치환 안 함).</summary>
    B2B,
    Other,
}

public static class ChannelTypeExtensions
{
    /// <summary>
    /// 온라인 마켓플레이스 채널(쿠팡/11번가/아마존 계열 + General)이면 true.
    /// 거래처(Partner/Other)면 false. OFS 발주처리가 필요한지 여부의 기준으로 쓴다.
    /// </summary>
    public static bool IsMarketplace(this ChannelType type) => type switch
    {
        ChannelType.Partner or ChannelType.B2B or ChannelType.Other => false,
        _ => true,
    };

    public static string ToKoreanLabel(this ChannelType type) => type switch
    {
        ChannelType.Partner       => "거래처",
        ChannelType.B2B           => "B2B 거래처",
        ChannelType.Other         => "기타",
        ChannelType.CoupangGeneral => "쿠팡 일반",
        ChannelType.CoupangRocket  => "쿠팡 로켓",
        ChannelType.CoupangGrowth  => "쿠팡 그로스",
        ChannelType.ElevenStreet   => "11번가",
        ChannelType.AmazonUs       => "아마존 미국",
        ChannelType.AmazonJp       => "아마존 일본",
        _                          => "온라인",
    };

    /// <summary>한글 라벨(<see cref="ToKoreanLabel"/> 결과)로 <see cref="ChannelType"/>을 역변환한다.
    /// "온라인"은 General 외에도 표시되는 기본 라벨이라 명시적으로 General에 매핑한다.
    /// 일치하는 라벨이 없으면 null.</summary>
    public static ChannelType? TryParseKoreanLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var trimmed = label.Trim();
        if (trimmed == "온라인") return ChannelType.General;
        foreach (var type in Enum.GetValues<ChannelType>())
        {
            if (type != ChannelType.General && string.Equals(type.ToKoreanLabel(), trimmed, StringComparison.Ordinal))
                return type;
        }
        return null;
    }
}

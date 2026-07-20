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
}

using MiniERP2.Database;

namespace MiniERP2.Mapping;

/// <summary>
/// 온라인 거래처 취합(OnlinePartnerConsolidation_Spec.md §5) — CSKU의 납품단가를 조회한다.
/// 채널 자체의 ChannelSkuTable.SupplyPrice가 없으면(0), 같은 상호명(DocPartyTable.CompanyName)
/// 그룹의 대표단가 채널에서 상속한다. 이 클래스는 조회만 하며 DB에 아무것도 쓰지 않는다 —
/// 비대표 채널의 SupplyPrice는 항상 0인 채로 둔다(스펙 §5: "상속은 조회 시점 계산").
/// </summary>
public class PartnerSupplyPriceResolver(ChannelSkuRepository channelSkuRepository, DocPartyRepository docPartyRepository)
{
    /// <summary>
    /// CSKU의 납품단가를 조회한다.
    /// </summary>
    /// <param name="channelCode">조회 대상 채널.</param>
    /// <param name="cskuCode">조회 대상 CSKU 코드.</param>
    /// <param name="msku">
    /// 이 CSKU가 가리키는 마스터SKU(대표채널에 같은 CskuCode가 없을 때의 재대조용). 모르면 빈 문자열.
    /// </param>
    public SupplyPriceResolution Resolve(string channelCode, string cskuCode, string msku)
    {
        // 1) 자체 채널의 SupplyPrice.
        var own = channelSkuRepository.GetByChannelAndCskuCode(channelCode, cskuCode);
        if (own is { SupplyPrice: > 0 })
            return SupplyPriceResolution.FromOwn(own.SupplyPrice);

        // 2) 소속 상호명 그룹의 대표단가 채널을 찾는다.
        var party = docPartyRepository.GetByChannelCode(channelCode);
        if (party == null || string.IsNullOrWhiteSpace(party.CompanyName))
            return SupplyPriceResolution.Unassigned;

        var master = docPartyRepository.GetPriceMasterByCompanyName(party.CompanyName);
        if (master == null || string.IsNullOrWhiteSpace(master.ChannelCode))
            return SupplyPriceResolution.Unassigned;

        // 대표채널 자신을 조회 중이었다면(1에서 이미 0으로 확인됐으므로) 더 볼 것이 없다.
        if (string.Equals(master.ChannelCode, channelCode, StringComparison.Ordinal))
            return SupplyPriceResolution.Unassigned;

        var masterChannelName = string.IsNullOrWhiteSpace(master.ProfileName) ? master.ChannelCode : master.ProfileName;

        // 같은 CskuCode로 먼저 시도한다.
        var masterCsku = channelSkuRepository.GetByChannelAndCskuCode(master.ChannelCode, cskuCode);
        if (masterCsku != null)
        {
            return masterCsku.SupplyPrice > 0
                ? SupplyPriceResolution.FromInherited(masterCsku.SupplyPrice, master.ChannelCode, masterChannelName)
                : SupplyPriceResolution.Unassigned;
        }

        // 대표채널에 같은 코드가 없으면 마스터SKU로 한 번 더 대조한다. 1:N이면 상속하지 않는다.
        if (string.IsNullOrWhiteSpace(msku))
            return SupplyPriceResolution.Unassigned;

        var candidates = channelSkuRepository.GetAllByChannel(master.ChannelCode)
            .Where(c => string.Equals(c.Msku, msku, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count != 1)
            return candidates.Count > 1
                ? SupplyPriceResolution.AmbiguousMasterSkuMatch
                : SupplyPriceResolution.Unassigned;

        return candidates[0].SupplyPrice > 0
            ? SupplyPriceResolution.FromInherited(candidates[0].SupplyPrice, master.ChannelCode, masterChannelName)
            : SupplyPriceResolution.Unassigned;
    }
}

public enum SupplyPriceSource
{
    /// <summary>채널 자체의 ChannelSkuTable.SupplyPrice.</summary>
    Own,

    /// <summary>대표단가 채널로부터 조회 시점에 상속된 값(DB에 쓰지 않음).</summary>
    Inherited,

    /// <summary>납품단가를 찾지 못함(대표채널 없음/대표채널도 0/마스터SKU 1:N 불일치 등).</summary>
    Unassigned,
}

/// <summary>ResolveSupplyPrice 1회 조회 결과. §5.1 표시 규칙(자체/상속(대표채널명)/미배정)에 대응한다.</summary>
public readonly record struct SupplyPriceResolution(
    decimal Price,
    SupplyPriceSource Source,
    string? MasterChannelCode,
    string? MasterChannelName,
    bool IsAmbiguousMasterSkuMatch)
{
    public static readonly SupplyPriceResolution Unassigned = new(0, SupplyPriceSource.Unassigned, null, null, false);

    public static readonly SupplyPriceResolution AmbiguousMasterSkuMatch = new(0, SupplyPriceSource.Unassigned, null, null, true);

    public static SupplyPriceResolution FromOwn(decimal price) => new(price, SupplyPriceSource.Own, null, null, false);

    public static SupplyPriceResolution FromInherited(decimal price, string masterChannelCode, string masterChannelName) =>
        new(price, SupplyPriceSource.Inherited, masterChannelCode, masterChannelName, false);
}

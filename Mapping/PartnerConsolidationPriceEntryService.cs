using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Mapping;

public enum PartnerConsolidationPriceEntryResult
{
    Saved,

    /// <summary>거래처에 대표단가 채널이 지정돼 있지 않다 — 채널설정에서 먼저 지정해야 한다.</summary>
    NoPriceMasterChannel,

    /// <summary>대표단가 채널에 같은 마스터SKU를 가진 CSKU가 2개 이상 있어 어느 것을 갱신할지 정할 수 없다.</summary>
    AmbiguousMasterCsku,
}

public readonly record struct PartnerConsolidationPriceEntryOutcome(
    PartnerConsolidationPriceEntryResult Result, string? MasterChannelCode, string? CskuCode);

/// <summary>
/// 온라인 거래처 취합(OnlinePartnerConsolidation_Spec.md §6.5 "단가 미배정" 탭) — 화면에서 바로
/// 입력한 납품단가를 대표단가 채널의 ChannelSkuTable.SupplyPrice에 저장한다. 비대표 채널에는
/// 절대 쓰지 않는다(§5 "상속은 조회 시점 계산이며 DB에 쓰지 않는다").
/// </summary>
public class PartnerConsolidationPriceEntryService(ChannelSkuRepository channelSkuRepository, DocPartyRepository docPartyRepository)
{
    public PartnerConsolidationPriceEntryOutcome SavePrice(string companyName, string msku, decimal price, string? reason = null)
    {
        var master = docPartyRepository.GetPriceMasterByCompanyName(companyName);
        if (master == null || string.IsNullOrWhiteSpace(master.ChannelCode))
            return new PartnerConsolidationPriceEntryOutcome(PartnerConsolidationPriceEntryResult.NoPriceMasterChannel, null, null);

        var existingForMsku = channelSkuRepository.GetAllByChannel(master.ChannelCode)
            .Where(c => string.Equals(c.Msku, msku, StringComparison.Ordinal))
            .ToList();

        if (existingForMsku.Count > 1)
            return new PartnerConsolidationPriceEntryOutcome(PartnerConsolidationPriceEntryResult.AmbiguousMasterCsku, master.ChannelCode, null);

        if (existingForMsku.Count == 1)
        {
            var csku = existingForMsku[0];
            csku.SupplyPrice = price;
            channelSkuRepository.Upsert(csku, reason);
            return new PartnerConsolidationPriceEntryOutcome(PartnerConsolidationPriceEntryResult.Saved, master.ChannelCode, csku.CskuCode);
        }

        // 대표채널에 이 마스터SKU를 위한 CSKU가 아직 없다 — 새로 만든다(§8-O5의 실무 공백을 메움).
        var allInMasterChannel = channelSkuRepository.GetAllByChannel(master.ChannelCode);
        var newCode = GenerateUniqueCode(master.ProfileName, msku, allInMasterChannel);
        var newCsku = new ChannelSkuModel { ChannelCode = master.ChannelCode, CskuCode = newCode, Msku = msku, SupplyPrice = price };
        channelSkuRepository.Upsert(newCsku, reason);
        return new PartnerConsolidationPriceEntryOutcome(PartnerConsolidationPriceEntryResult.Saved, master.ChannelCode, newCode);
    }

    /// <summary>
    /// CskuCodeGenerator.BuildDefault가 만든 코드가 이미 다른 마스터SKU에 쓰이고 있으면(같은
    /// 채널명 접두사를 공유하는 다른 상품과 충돌) 번호를 붙여 피한다 — 기존 CSKU를 실수로
    /// 덮어쓰는 사고를 막는다(CskuCodeGenerator 자체 문서 주석의 경고).
    /// </summary>
    private static string GenerateUniqueCode(string channelName, string msku, List<ChannelSkuModel> existingInChannel)
    {
        var baseCode = CskuCodeGenerator.BuildDefault(channelName, msku);
        if (!existingInChannel.Any(c => string.Equals(c.CskuCode, baseCode, StringComparison.Ordinal)))
            return baseCode;

        for (int suffix = 2; ; suffix++)
        {
            var candidate = $"{baseCode}_{suffix}";
            if (!existingInChannel.Any(c => string.Equals(c.CskuCode, candidate, StringComparison.Ordinal)))
                return candidate;
        }
    }
}

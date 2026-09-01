using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Mapping;

/// <summary>
/// 온라인 거래처 취합(OnlinePartnerConsolidation_Spec.md §6.2) — 거래처(CompanyName) × 마스터SKU
/// 단위로 수량을 합산하고 납품매출액/납품이익액을 산출한다. CSKU 코드는 채널마다 다르게 발급될 수
/// 있어(§5) 실제 집계 축은 마스터SKU이며, 화면/엑셀에는 대표 CSKU 코드 1개를 함께 보여준다.
/// </summary>
public class PartnerConsolidationAggregator(PartnerSupplyPriceResolver priceResolver, ItemRepository itemRepository)
{
    /// <param name="rows">여러 파일에서 모은 전체 행. Kind != Mapped인 행은 무시한다(호출자가 별도
    /// 목록으로 이미 분리했어야 한다 — §6.4).</param>
    public PartnerConsolidationAggregationResult Aggregate(IEnumerable<PartnerConsolidationRow> rows)
    {
        var result = new PartnerConsolidationAggregationResult();

        var byCompany = rows
            .Where(r => r.Kind == PartnerConsolidationRowKind.Mapped && r.ResolvedMsku != null && r.ResolvedCskuCode != null)
            .GroupBy(r => r.CompanyName);

        foreach (var companyGroup in byCompany)
        {
            var companyName = companyGroup.Key;
            var cskuDetails = new List<PartnerConsolidationCskuDetail>();

            foreach (var mskuGroup in companyGroup.GroupBy(r => r.ResolvedMsku!))
            {
                var msku = mskuGroup.Key;
                var totalQty = mskuGroup.Sum(r => r.Quantity);

                var (representative, priceResolution) = ResolveGroupPrice(mskuGroup.ToList(), msku);

                var item = itemRepository.GetBySku(msku);
                var costPrice = item?.CostPrice; // null이면 W7: 제조원가 미등록

                var supplyRevenue = totalQty * priceResolution.Price;
                decimal? supplyProfit = costPrice.HasValue ? supplyRevenue - totalQty * costPrice.Value : null;

                cskuDetails.Add(new PartnerConsolidationCskuDetail
                {
                    CompanyName = companyName,
                    CskuCode = representative.ResolvedCskuCode!,
                    Msku = msku,
                    ProductName = item?.ItemName ?? representative.ProductName,
                    Quantity = totalQty,
                    SupplyPrice = priceResolution.Price,
                    PriceSource = priceResolution.Source,
                    MasterChannelName = priceResolution.MasterChannelName,
                    SupplyRevenue = supplyRevenue,
                    CostPrice = costPrice,
                    SupplyProfit = supplyProfit,
                });
            }

            result.CskuDetails.AddRange(cskuDetails);
            result.CompanySummaries.Add(new PartnerConsolidationCompanySummary
            {
                CompanyName = companyName,
                ChannelCount = companyGroup.Select(r => r.ChannelCode).Distinct().Count(),
                TotalQuantity = cskuDetails.Sum(c => c.Quantity),
                TotalSupplyRevenue = cskuDetails.Sum(c => c.SupplyRevenue),
                TotalSupplyProfit = cskuDetails.Sum(c => c.SupplyProfit ?? 0),
                UnassignedPriceCount = cskuDetails.Count(c => c.PriceSource == SupplyPriceSource.Unassigned),
            });
        }

        return result;
    }

    /// <summary>
    /// D8 "납품단가는 거래처 단위 1개" — 그룹에 참여한 채널 중 명시적으로 자체 단가(Own)를 가진
    /// 채널이 있으면 그 값을 우선 채택하고(§5의 "자체" 우선순위와 동일한 정신), 없으면 아무
    /// 채널로 조회해도 결과가 같다(전부 같은 대표단가 채널에서 상속받으므로) — 첫 채널 결과를 쓴다.
    /// </summary>
    private (PartnerConsolidationRow Representative, SupplyPriceResolution Resolution) ResolveGroupPrice(
        List<PartnerConsolidationRow> mskuGroupRows, string msku)
    {
        var candidates = mskuGroupRows.GroupBy(r => r.ChannelCode).Select(g => g.First()).ToList();

        PartnerConsolidationRow? fallbackRow = null;
        SupplyPriceResolution? fallbackResolution = null;

        foreach (var candidate in candidates)
        {
            var resolution = priceResolver.Resolve(candidate.ChannelCode, candidate.ResolvedCskuCode!, msku);
            if (resolution.Source == SupplyPriceSource.Own)
                return (candidate, resolution);

            fallbackRow ??= candidate;
            fallbackResolution ??= resolution;
        }

        return (fallbackRow!, fallbackResolution!.Value);
    }
}

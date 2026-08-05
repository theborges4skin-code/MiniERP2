using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Mapping;

/// <summary>
/// 기획서 5.6절 '채널별 손익 공식'에 따라 정산 1건의 이익액을 계산합니다.
/// </summary>
public static class ProfitCalculator
{
    /// <summary>
    /// 채널 유형별 공식에 따라 이익액을 계산합니다.
    /// </summary>
    /// <param name="channelType">채널 유형</param>
    /// <param name="settlement">정산액</param>
    /// <param name="costPrice">제조원가(마스터SKU 기준, VAT 포함)</param>
    /// <param name="qty">수량</param>
    /// <param name="shipping">배송비(쿠팡그로스의 그로스배송비 등, VAT 별도금액)</param>
    /// <param name="fee">입출고비 등 부가 수수료(VAT 별도금액)</param>
    /// <param name="cfsMode">
    /// true = 쿠팡그로스 CFS 모드. 배송비·입출고비가 이미 VAT포함 금액으로 저장되어 있으므로
    /// 공식에서 vatRate를 추가로 곱하지 않는다. false(기본) = 기존 GrowthAuxSource 방식.
    /// </param>
    /// <remarks>
    /// 아마존(미국/일본)은 원래 통화(달러) 그대로 반환한다 — 환율 환산은 여기서 하지 않는다.
    /// 여러 채널을 원화로 통합 집계하는 보고서(ProfitFactTable/ReportForm)에 저장할 때만
    /// <see cref="ToReportCurrency"/>로 별도 환산한다(화면에 보여주는 값은 원래 통화 그대로 유지).
    /// </remarks>
    public static decimal Calculate(ChannelType channelType, decimal settlement, decimal costPrice, int qty, decimal shipping, decimal fee, bool cfsMode = false)
    {
        const decimal vatRate = 1.1m;

        return channelType switch
        {
            // CFS 모드: 로더가 이미 × 1.1 처리했으므로 여기서는 그대로 차감
            ChannelType.CoupangGrowth when cfsMode =>
                settlement - (costPrice * qty) - shipping - fee,

            // 기존 GrowthAuxSource 모드: VAT 별도 금액이므로 × 1.1
            ChannelType.CoupangGrowth =>
                settlement - (costPrice * qty) - (shipping * vatRate) - (fee * vatRate),

            ChannelType.CoupangRocket =>
                settlement - (costPrice * qty) - (shipping * vatRate),

            ChannelType.AmazonUs or ChannelType.AmazonJp =>
                settlement - (costPrice / vatRate * qty),

            _ => settlement - (costPrice * qty),
        };
    }

    /// <summary>
    /// 다채널 통합 리포트(ReportForm)는 원화 단일 통화를 전제하므로, 원래 통화가 다른 채널(아마존)의
    /// 매출액/이익액을 <see cref="Database.ProfitFactRepository.SaveProfitFacts"/>에 저장하기
    /// 직전에만 원화로 환산한다. 화면에 보여주는 <see cref="SettlementData"/> 자체(Revenue/Profit)는
    /// 원래 통화 그대로 둔다 — 화면 표시 통화는 <c>SettlementForm</c>의 별도 표시 토글이 담당한다.
    /// </summary>
    public static decimal ToReportCurrency(ChannelType? channelType, decimal amount, decimal exchangeRate) =>
        channelType is ChannelType.AmazonUs or ChannelType.AmazonJp ? amount * exchangeRate : amount;

    /// <summary>
    /// 쿠팡그로스 CFS 집계 결과를 정산 행 목록에 배분합니다.
    /// 옵션ID 기준으로 최초 등장 행에만 입출고비·배송비를 할당하여 중복 계상을 방지합니다.
    /// (판매 기간과 정산 기간이 달라 1:1 매칭이 불가능한 현실적 제약을 반영한 설계.)
    /// </summary>
    /// <returns>Fee/Shipping이 실제로 변경된 행 목록 (Profit 재계산 대상).</returns>
    public static List<SettlementData> ApplyCoupangGrowthCfsFees(
        ChannelType channelType,
        List<SettlementData> rows,
        string settlementOptionIdHeader,
        Dictionary<string, decimal> handlingFees,
        Dictionary<string, decimal> shippingFees)
    {
        if (channelType != ChannelType.CoupangGrowth) return [];
        if (handlingFees.Count == 0 && shippingFees.Count == 0) return [];

        var seenOptionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var affected = new List<SettlementData>();

        foreach (var row in rows)
        {
            var optionId = row.RawValues != null &&
                row.RawValues.TryGetValue(settlementOptionIdHeader, out var rv)
                ? rv : null;
            if (string.IsNullOrWhiteSpace(optionId)) continue;
            if (!seenOptionIds.Add(optionId)) continue;  // 최초 등장 이후는 무시

            var hasH = handlingFees.TryGetValue(optionId, out var hFee);
            var hasS = shippingFees.TryGetValue(optionId, out var sFee);
            if (!hasH && !hasS) continue;

            if (hasH) row.Fee = hFee;
            if (hasS) row.Shipping = sFee;
            affected.Add(row);
        }
        return affected;
    }

    /// <summary>
    /// 아마존 채널의 특수 규칙: EventType 값이 지정된 문자열인 행(Transfer·입금 행)을 제거합니다.
    /// <para>
    /// 아마존 Unified Transaction Report에는 실제 입금 이벤트(Transfer)가 행으로 포함되어 있습니다.
    /// 이 행은 판매/수수료가 아닌 단순 입금 기록이므로 이익분석 대상에서 제거합니다.
    /// </para>
    /// 채널 유형이 아마존이 아니거나 transferTypeValue가 비어 있으면 아무 동작도 하지 않습니다.
    /// </summary>
    public static void ApplyAmazonTransferFilter(ChannelType channelType, List<SettlementData> rows, string? transferTypeValue)
    {
        if (channelType is not (ChannelType.AmazonUs or ChannelType.AmazonJp)) return;
        if (string.IsNullOrWhiteSpace(transferTypeValue)) return;
        rows.RemoveAll(r => string.Equals(r.EventType, transferTypeValue, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 쿠팡 로켓 채널의 특수 규칙: 입고상세내역 파일의 소계 행을 제거합니다.
    /// <para>
    /// 쿠팡 로켓 입고상세내역은 세금계산서번호 그룹마다 '소계' 행이 삽입되어 있고,
    /// 해당 행의 상품명이 비어있지 않아 매출액이 2배로 집계되는 문제가 있습니다.
    /// </para>
    /// 채널 유형이 쿠팡 로켓이 아니면 아무 동작도 하지 않습니다.
    /// </summary>
    public static void ApplyCoupangRocketFilter(ChannelType channelType, List<SettlementData> rows)
    {
        if (channelType != ChannelType.CoupangRocket || rows.Count == 0) return;

        rows.RemoveAll(r => string.Equals(r.ProductName?.Trim(), "소계", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 11번가 채널의 특수 규칙: 수량=0인 행(배송비 전용 행)의 배송비를 같은 주문의 상품 행으로
    /// 옮긴 뒤 목록에서 제거합니다.
    /// <para>
    /// 11번가 정산파일은 한 주문을 상품 행(수량&gt;0, 선결제배송비=0)과 배송비 전용 행(수량=0,
    /// 선결제배송비에 실제 금액 기재)으로 나눠서 내보낸다. 배송비 전용 행을 그냥 삭제하면 배송비가
    /// 통째로 사라지므로, 같은 주문번호(OrderNo)의 상품 행 수만큼 균등 분배한 뒤 제거한다.
    /// 배송비 전용 행의 정산금액(실수령액)은 이익분석에서 버린다(요청사항 — 배송비만 보존).
    /// </para>
    /// 주문번호가 매핑되지 않은 경우 — 기존 동작 유지: 전체 배송비를 합산해 첫 상품 행에만 표기한다.
    /// 채널 유형이 11번가가 아니면 아무 동작도 하지 않습니다.
    /// </summary>
    public static void ApplyElevenStreetFilter(ChannelType channelType, List<SettlementData> rows)
    {
        if (channelType != ChannelType.ElevenStreet || rows.Count == 0) return;

        var shippingRows = rows.Where(r => r.Qty == 0).ToList();
        var productRows = rows.Where(r => r.Qty != 0).ToList();

        bool hasOrderNo = rows.Any(r => !string.IsNullOrEmpty(r.OrderNo));
        if (hasOrderNo)
        {
            var shippingByOrder = shippingRows
                .Where(r => !string.IsNullOrEmpty(r.OrderNo))
                .GroupBy(r => r.OrderNo!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Shipping), StringComparer.OrdinalIgnoreCase);

            var productCountByOrder = productRows
                .Where(r => !string.IsNullOrEmpty(r.OrderNo))
                .GroupBy(r => r.OrderNo!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var row in productRows)
            {
                if (!string.IsNullOrEmpty(row.OrderNo) &&
                    shippingByOrder.TryGetValue(row.OrderNo, out var totalShipping) &&
                    productCountByOrder.TryGetValue(row.OrderNo, out var count) && count > 0)
                {
                    row.Shipping = totalShipping / count;
                }
            }
        }
        else if (productRows.Count > 0)
        {
            var total = shippingRows.Sum(r => r.Shipping);
            for (int i = 1; i < productRows.Count; i++)
                productRows[i].Shipping = 0m;
            productRows[0].Shipping = total;
        }

        rows.Clear();
        rows.AddRange(productRows);
    }

    /// <summary>
    /// 쿠팡일반 채널의 특수 규칙: 배송비를 집계합니다.
    /// <para>
    /// OrderNo가 매핑되어 있고 수량=0인 배송비 전용 행이 실제로 존재하는 경우 — 주문번호 단위로
    /// 재배분합니다. 수량=0인 행의 매출액(Revenue)을 해당 주문의 총 배송비로 간주하고, 같은 주문의
    /// 상품 행 수로 나눠 균등 분배한 뒤 수량=0 행을 목록에서 제거합니다.
    /// </para>
    /// <para>
    /// 그 외 모든 경우(OrderNo 미매핑이거나, 매핑되어 있어도 배송비 전용 행이 없는 실데이터) —
    /// 검증된 레퍼런스(SalesManagerV2 Python analysis_engine.py)와 동일하게, 매핑된 배송비 필드를
    /// 그대로 전체 합산해 첫 상품 행에 몰아서 표기합니다. 배송비 전용 행이 있다면 그 매출액(Revenue)도
    /// 더하고 목록에서 제거합니다.
    /// </para>
    /// 채널 유형이 쿠팡일반이 아니면 아무 동작도 하지 않습니다.
    /// </summary>
    public static void ApplyCoupangGeneralShippingAggregation(ChannelType channelType, List<SettlementData> rows)
    {
        if (channelType != ChannelType.CoupangGeneral || rows.Count == 0) return;

        var shippingRows = rows.Where(r => r.Qty == 0).ToList();
        var productRows = rows.Where(r => r.Qty != 0).ToList();

        bool hasOrderNo = rows.Any(r => !string.IsNullOrEmpty(r.OrderNo));

        DiagnosticsLogger.Log(
            $"[ProfitCalculator] 쿠팡일반 배송비 집계 시작 — 전체 {rows.Count}건, Qty=0(배송비 후보) {shippingRows.Count}건" +
            $"(매출액합 {shippingRows.Sum(r => r.Revenue):N0}), 상품 행 {productRows.Count}건" +
            $"(자체Shipping합 {productRows.Sum(r => r.Shipping):N0}), OrderNo매핑={hasOrderNo}");

        if (hasOrderNo && shippingRows.Count > 0)
        {
            // 주문번호별 배송비 합산 (Qty=0 행의 매출액이 배송비)
            var shippingByOrder = shippingRows
                .Where(r => !string.IsNullOrEmpty(r.OrderNo))
                .GroupBy(r => r.OrderNo!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Revenue), StringComparer.OrdinalIgnoreCase);

            // 주문번호별 상품 행 수
            var productCountByOrder = productRows
                .Where(r => !string.IsNullOrEmpty(r.OrderNo))
                .GroupBy(r => r.OrderNo!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var row in productRows)
            {
                if (!string.IsNullOrEmpty(row.OrderNo) &&
                    shippingByOrder.TryGetValue(row.OrderNo, out var totalShippingForOrder) &&
                    productCountByOrder.TryGetValue(row.OrderNo, out var count) && count > 0)
                {
                    row.Shipping = totalShippingForOrder / count;
                }
            }

            rows.Clear();
            rows.AddRange(productRows);
            return;
        }

        // 배송비 전용 행(Qty=0)이 없으면(OrderNo 매핑 여부 무관) — 주문 단위 재배분을 할 대상 자체가
        // 없으므로, 위 분기가 아무 것도 안 하고 그냥 리턴해버리면 배송비가 통째로 0으로 표시되는
        // 버그가 있었다. 이 경우 검증된 레퍼런스 방식대로 매핑된 배송비 필드를 전체 합산해
        // 첫 상품 행에 몰아준다(배송비 전용 행이 있다면 그 매출액도 함께 합산).
        var total = shippingRows.Sum(r => r.Revenue) + productRows.Sum(r => r.Shipping);
        var target = productRows.Count > 0 ? productRows : rows.ToList();

        for (int i = 0; i < target.Count; i++)
            target[i].Shipping = 0m;
        if (target.Count > 0)
            target[0].Shipping = total;

        rows.Clear();
        rows.AddRange(target);
    }
}

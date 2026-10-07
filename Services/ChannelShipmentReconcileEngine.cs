using MiniERP2.Models;

namespace MiniERP2.Services;

/// <summary>운송장 파일의 송장 1건(같은 운송장번호 여러 줄은 하나로)과 그 송장의 이력·배송비 청구 상태.</summary>
public class ChannelShipmentRow
{
    public string TrackingNo { get; set; } = string.Empty;
    public DateTime? ReceivedAt { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal FreightCost { get; set; }

    /// <summary>이력에 등록된 송장이면 그 채널, 아니면 빈 값.</summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>집계 표의 묶음 이름 — 등록 송장은 채널명, 미등록은 "(미등록) 라벨".</summary>
    public string GroupName { get; set; } = string.Empty;

    public bool IsRegistered => Anchor != null;

    /// <summary>귀속월(ClosingPeriod, 없으면 출고일 연월). 미등록·미출고면 빈 값.</summary>
    public string Period { get; set; } = string.Empty;

    public bool IsCharged { get; set; }
    public decimal ChargedAmount { get; set; }

    /// <summary>이 송장에 붙은 배송비 라인 Id(청구 해제 시 지울 대상).</summary>
    public List<long> ChargeLineIds { get; set; } = [];

    /// <summary>배송비를 나중에 붙일 때 기준이 되는 이 송장의 상품 라인(미등록이면 null).</summary>
    public OutboundDetail? Anchor { get; set; }

    public string ChargeText => !IsRegistered ? "(미등록)" : IsCharged ? $"청구 {ChargedAmount:N0}" : "미청구";
}

/// <summary>채널(또는 미등록 라벨)별 송장 수·배송비 청구 현황 한 줄.</summary>
public class ChannelShipmentSummaryRow
{
    public string GroupName { get; set; } = string.Empty;
    public string ChannelCode { get; set; } = string.Empty;
    public int ShipmentCount { get; set; }
    public int ChargedCount { get; set; }
    public int UnchargedCount { get; set; }
    public decimal ChargedAmount { get; set; }
    public decimal FreightTotal { get; set; }
}

/// <summary>
/// 운송장 파일 누락건 점검의 "채널별 송장 대조" — 택배사 운송장 파일의 송장을 이력(OutboundDetailTable)과
/// 운송장번호로 맞춰 채널별 실제 발송 건수를 세고, 송장마다 배송비 라인(<see cref="ShippingFeeLineService"/>)이
/// 붙어 있는지(같은 운송장 또는 같은 묶음키) 판정한다. 월말에 거래처별 배송비 청구 누락을 잡는 용도.
/// </summary>
public static class ChannelShipmentReconcileEngine
{
    public const string UnregisteredPrefix = "(미등록) ";

    /// <param name="fileRows">운송장 파일 행(같은 운송장번호가 여러 줄일 수 있음).</param>
    /// <param name="historyLines">그 운송장번호들 + 그 묶음키들로 조회한 이력 라인 전체(배송비 라인 포함).</param>
    /// <param name="shippingKeys"><see cref="ShippingFeeLineService.GetAllShippingKeys"/>.</param>
    /// <param name="channelNames">채널코드 → 채널명.</param>
    public static List<ChannelShipmentRow> BuildShipments(
        IEnumerable<TrackingBackfillRow> fileRows,
        IReadOnlyCollection<OutboundDetail> historyLines,
        HashSet<string> shippingKeys,
        IReadOnlyDictionary<string, string> channelNames)
    {
        bool IsShipping(OutboundDetail d) => ShippingFeeLineService.IsShippingLine(d, shippingKeys);

        var productByTracking = historyLines
            .Where(d => !IsShipping(d) && !string.IsNullOrWhiteSpace(d.TrackingNo))
            .GroupBy(d => d.TrackingNo.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.Id).ToList(), StringComparer.OrdinalIgnoreCase);
        var shippingLines = historyLines.Where(IsShipping).ToList();

        var result = new List<ChannelShipmentRow>();
        foreach (var g in fileRows.Where(r => !string.IsNullOrWhiteSpace(r.TrackingNo))
                     .GroupBy(r => r.TrackingNo.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var first = g.First();
            var row = new ChannelShipmentRow
            {
                TrackingNo = g.Key,
                ReceivedAt = g.Select(r => r.ReceivedAt).FirstOrDefault(d => d.HasValue),
                Recipient = first.Recipient,
                Address = first.Address,
                ProductName = string.Join(", ", g.Select(r => r.ProductName).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct()),
                FreightCost = g.Max(r => r.FreightCost),
            };

            if (productByTracking.TryGetValue(g.Key, out var products))
            {
                var anchor = products[0];
                row.Anchor = anchor;
                row.ChannelCode = anchor.ChannelCode;
                row.GroupName = channelNames.GetValueOrDefault(anchor.ChannelCode) ?? anchor.ChannelCode;
                row.Period = !string.IsNullOrEmpty(anchor.ClosingPeriod) ? anchor.ClosingPeriod
                    : anchor.ConfirmedAt?.ToString("yyyy-MM") ?? string.Empty;

                var groupKeys = products.Select(p => p.ShipmentGroupKey).Where(k => !string.IsNullOrEmpty(k)).ToHashSet();
                var charges = shippingLines.Where(s => s.ChannelCode == anchor.ChannelCode
                    && (string.Equals(s.TrackingNo?.Trim(), g.Key, StringComparison.OrdinalIgnoreCase) || groupKeys.Contains(s.ShipmentGroupKey)))
                    .DistinctBy(s => s.Id)
                    .ToList();
                row.IsCharged = charges.Count > 0;
                row.ChargedAmount = charges.Sum(s => s.SupplyPrice * s.Qty);
                row.ChargeLineIds = charges.Select(s => s.Id).ToList();
            }
            else
            {
                row.GroupName = UnregisteredPrefix + (string.IsNullOrWhiteSpace(first.Label) ? "기타" : first.Label);
            }
            result.Add(row);
        }
        return result;
    }

    /// <summary>등록 채널을 송장 수 많은 순으로 먼저, 미등록 라벨 묶음은 뒤에.</summary>
    public static List<ChannelShipmentSummaryRow> Summarize(IEnumerable<ChannelShipmentRow> shipments) =>
        shipments
            .GroupBy(s => (s.GroupName, s.ChannelCode))
            .Select(g => new ChannelShipmentSummaryRow
            {
                GroupName = g.Key.GroupName,
                ChannelCode = g.Key.ChannelCode,
                ShipmentCount = g.Count(),
                ChargedCount = g.Count(s => s.IsCharged),
                UnchargedCount = g.Count(s => s.IsRegistered && !s.IsCharged),
                ChargedAmount = g.Sum(s => s.ChargedAmount),
                FreightTotal = g.Sum(s => s.FreightCost),
            })
            .OrderBy(r => string.IsNullOrEmpty(r.ChannelCode) ? 1 : 0)
            .ThenByDescending(r => r.ShipmentCount)
            .ThenBy(r => r.GroupName)
            .ToList();
}

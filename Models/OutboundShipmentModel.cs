namespace MiniERP2.Models;

/// <summary>
/// 발송헤더 — 같은 발송(ShipmentGroupKey)에 속한 여러 출고 라인이 공통으로 나누는 실운임입니다
/// (§D6: 발송 운임은 라인이 아니라 발송헤더 1건에 저장해, 라인 삭제로 운임이 유실되지 않게 함).
/// </summary>
public class OutboundShipmentModel
{
    /// <summary>OutboundDetailTable.ShipmentGroupKey와 같은 값으로 연결됩니다.</summary>
    public required string ShipmentGroupKey { get; set; }

    /// <summary>이 발송 전체의 실운임입니다. 라인별 배부는 WeightKg 비중으로 계산합니다(저장하지 않음).</summary>
    public decimal FreightCost { get; set; }

    public DateTime? ShippedAt { get; set; }

    public string? Note { get; set; }
}

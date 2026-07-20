namespace MiniERP2.Models;

/// <summary>
/// 매입SKU — B2B 매입처(채널)에서 특정 마스터SKU를 얼마에 사입하는지를 나타냅니다.
/// 매출측 <see cref="ChannelSkuModel"/>(ChannelSkuTable)과 대칭 구조이되, CSKU처럼 채널 안에서
/// 여러 코드로 분화되지 않고 (ChannelCode, Msku)가 그대로 고유키입니다(§D3 — Msku 단일 유지).
/// </summary>
public class PurchaseSkuModel
{
    /// <summary>매입처 역할을 하는 채널 코드입니다(SalesChannel.IsPurchase=true인 채널).</summary>
    public required string ChannelCode { get; set; }

    /// <summary>매입 대상 마스터SKU입니다(ItemTable.Sku).</summary>
    public required string Msku { get; set; }

    /// <summary>매입가(원/kg 등 Unit 기준)입니다.</summary>
    public decimal PurchasePrice { get; set; }

    public string Unit { get; set; } = "kg";

    public string? Note { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

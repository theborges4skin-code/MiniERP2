namespace MiniERP2.Models;

/// <summary>
/// 아마존 FBA 전용 품목 마스터 1건입니다. Csku는 <see cref="ChannelSkuModel"/>의
/// (ChannelCode="AMAZON_FBA", CskuCode)와 연동되며(FbaCskuRepository.Upsert 참고), 재고 관리는
/// 하지 않고 순수히 하배출고이서·선적명세 엑셀 생성에 필요한 값만 담는다.
/// </summary>
public class FbaCskuModel
{
    public required string Csku { get; set; }
    public string ItemName { get; set; } = string.Empty;
    /// <summary>하배출고이서 "품목명" 칸에 쓸 표시명. 비어있으면 내보내기 시 ItemName으로 대체된다.</summary>
    public string? InvoiceDisplayName { get; set; }
    public string Asin { get; set; } = string.Empty;
    public string CommodityDescription { get; set; } = string.Empty;
    public string HsCode { get; set; } = string.Empty;
    public decimal ItemPrice { get; set; }
    public string Volume { get; set; } = string.Empty;
    /// <summary>개당 무게(g). 포장 자재 몫을 포함한 근사치 — 박스 공차중량은 별도 관리하지 않는다.</summary>
    public double UnitWeightG { get; set; }
    public int QtyPerLayer { get; set; }
    public string MocraListingNo { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? UpdatedAt { get; set; }
}

namespace MiniERP2.Models;

/// <summary>
/// 메인 화면 "빠른 원가검색" 결과 한 줄. 마스터SKU(ItemTable 대표원가) 또는
/// 채널별 CSKU(ChannelSkuTable) 한 건을 같은 모양으로 담는다.
/// </summary>
public class CostSearchResult
{
    /// <summary>true면 CSKU 행, false면 마스터SKU(MSKU) 행.</summary>
    public bool IsCsku { get; set; }

    /// <summary>MSKU면 Sku, CSKU면 CskuCode.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>품목명. CSKU는 매칭된 마스터SKU의 품목명을 쓰고, 없으면 송장표시명으로 대체한다.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>CSKU 전용 — 채널명(없으면 채널코드).</summary>
    public string? ChannelName { get; set; }

    /// <summary>CSKU가 매칭된 마스터SKU 코드.</summary>
    public string? Msku { get; set; }

    /// <summary>
    /// 적용 원가. CSKU는 개별원가(CostPriceOverride)가 있으면 그 값, 없으면 마스터 원가를 따른다
    /// (CostResolver와 같은 우선순위). 매칭된 마스터SKU가 없어 원가를 알 수 없으면 null.
    /// </summary>
    public decimal? CostPrice { get; set; }

    /// <summary>CSKU가 개별원가(오버라이드)를 쓰고 있으면 true. MSKU 행은 항상 false.</summary>
    public bool IsCostOverride { get; set; }

    /// <summary>
    /// 원가가 마지막으로 바뀐 시각. MSKU는 ItemCostHistory, CSKU 개별원가는 ChannelSkuFieldHistory
    /// (없으면 그 CSKU의 최종 저장일), 마스터 연동 CSKU는 마스터SKU의 원가 변경 이력을 따른다.
    /// 변경 이력이 한 번도 없으면 null(=최초 등록 후 그대로).
    /// </summary>
    public DateTime? CostChangedAt { get; set; }

    /// <summary>원가의 수량 단위(kg 등).</summary>
    public string Unit { get; set; } = "kg";
}

namespace MiniERP2.Models;

/// <summary>
/// CSKU(채널별 SKU) — 채널 안에서 실제 매핑/송장 출력 단위가 되는 식별자입니다.
/// 마스터DB의 SKU는 1개라도, 채널의 옵션 구성에 따라 여러 CSKU로 나뉠 수 있습니다
/// (예: 마스터SKU "상품A" 1개가 채널에서는 옵션1/2/3별로 CskuCode가 다른 CSKU 3개로 관리됨).
/// </summary>
public class ChannelSkuModel
{
    public required string ChannelCode { get; set; }

    /// <summary>
    /// 이 채널 안에서 매핑 규칙(TargetSku)/OfsOrderItem.MappedSku가 실제로 가리키는 코드입니다.
    /// 기본값은 "채널명 앞 3글자_마스터SKU"로 자동 생성되지만 사용자가 직접 편집할 수 있습니다
    /// (Utils/CskuCodeGenerator.BuildDefault 참고). (ChannelCode, CskuCode)가 고유키입니다.
    /// </summary>
    public required string CskuCode { get; set; }

    /// <summary>
    /// 이 CSKU가 원가 조회를 위해 연결되는 마스터DB의 실제 SKU입니다(ItemTable.Sku).
    /// 여러 CSKU가 같은 Msku를 가리킬 수 있습니다(채널 옵션 분화).
    /// </summary>
    public required string Msku { get; set; }

    public decimal SupplyPrice { get; set; }

    /// <summary>
    /// 택배사 출력양식(송장)에 표시할 간결한 상품명입니다. 채널마다 별도로 관리되며,
    /// 마스터DB의 상품명과는 독립적입니다(같은 SKU라도 채널/상품에 따라 송장 표기를 다르게 할 수 있음).
    /// 비어있으면 발주서의 원본 상품명을 그대로 사용합니다.
    /// </summary>
    public string? InvoiceDisplayName { get; set; }

    /// <summary>담당자가 남기는 자유 메모입니다(예: 옵션 특이사항, 협의 이력 등).</summary>
    public string? Note { get; set; }

    /// <summary>이 CSKU의 Msku/InvoiceDisplayName/SupplyPrice/Note 중 하나라도 마지막으로 바뀐 시각입니다.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>B2B 등 kg 단위 납품에 쓰는 단위입니다(예: "kg", "개", "박스"). 기본값 "kg".</summary>
    public string Unit { get; set; } = "kg";

    /// <summary>포장 단위 설명입니다(예: "20kg/포대"). 견적서 출력 시 비고에 함께 표시됩니다.</summary>
    public string? Packing { get; set; }

    /// <summary>
    /// 이 CSKU 전용 제조원가입니다. NULL이면 마스터DB 원가(ItemTable.CostPrice)를 그대로 따르고
    /// (연동), 값이 있으면 그 값이 이 CSKU의 제조원가이며 마스터 변경에 영향받지 않습니다(개별관리).
    /// 우선순위는 Utils/CostResolver 참고(매입처 매입가 > 이 값 > 마스터 대표원가).
    /// </summary>
    public decimal? CostPriceOverride { get; set; }
}

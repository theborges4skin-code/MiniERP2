namespace MiniERP2.Models;

public class ChannelSkuModel
{
    public required string ChannelCode { get; set; }
    public required string Msku { get; set; }
    public decimal SupplyPrice { get; set; }

    /// <summary>
    /// 택배사 출력양식(송장)에 표시할 간결한 상품명입니다. 채널마다 별도로 관리되며,
    /// 마스터DB의 상품명과는 독립적입니다(같은 SKU라도 채널/상품에 따라 송장 표기를 다르게 할 수 있음).
    /// 비어있으면 발주서의 원본 상품명을 그대로 사용합니다.
    /// </summary>
    public string? InvoiceDisplayName { get; set; }
}
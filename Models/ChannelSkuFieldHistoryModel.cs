namespace MiniERP2.Models;

/// <summary>
/// CSKU의 매칭된 마스터SKU/송장표시명/비고 변경 이력입니다. 납품가 변경은 별도로
/// <see cref="ChannelSkuPriceHistoryModel"/>(ChannelSkuPriceHistory 테이블)에 기록됩니다.
/// </summary>
public class ChannelSkuFieldHistoryModel
{
    public long Id { get; set; }
    public required string ChannelCode { get; set; }
    public required string CskuCode { get; set; }
    public required string FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime ChangedAt { get; set; }
}

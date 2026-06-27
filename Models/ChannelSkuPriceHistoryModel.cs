namespace MiniERP2.Models;

public class ChannelSkuPriceHistoryModel
{
    public long Id { get; set; }
    public required string ChannelCode { get; set; }

    /// <summary>
    /// 가격이 바뀐 CSKU 코드입니다. DB 컬럼명은 과거 호환을 위해 여전히 "Msku"이지만,
    /// CSKU 도입 이후로는 CskuCode 값을 저장합니다.
    /// </summary>
    public required string CskuCode { get; set; }

    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public DateTime ChangedAt { get; set; }
}
namespace MiniERP2.Models;

public class SalesChannel
{
    public string ChannelCode { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string? GroupName { get; set; }
    public bool IsFavorite { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime? LastUsedDate { get; set; }

    /// <summary>이 채널에서 물건을 사입(매입)하는지 여부. 한 채널이 매입·매출을 동시에 겸할 수 있다.</summary>
    public bool IsPurchase { get; set; }

    /// <summary>이 채널에 물건을 판매(납품)하는지 여부. 기존 채널은 전부 판매 채널이었으므로 기본값 true.</summary>
    public bool IsSales { get; set; } = true;
}

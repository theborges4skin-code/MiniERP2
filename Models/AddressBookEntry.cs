namespace MiniERP2.Models;

/// <summary>
/// 배송지 주소록(AddressBookTable) 한 건. 채널 종속 없는 범용 원장으로, OFS의 "배송지 불러오기"에서
/// 골라 선택 행의 수취인/연락처/주소를 채우는 데 쓰인다(배송지주소록_개발기획서_확정본.md).
/// </summary>
public class AddressBookEntry
{
    public int AddressId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string ReceiverName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Memo { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>이 주소에 붙은 채널 태그(다대다, AddressChannelTagTable). 비어있으면 모든 채널에서 동일하게 노출된다.</summary>
    public List<string> ChannelTags { get; set; } = new();
}

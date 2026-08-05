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

    /// <summary>
    /// ListBox.DisplayMember="Label" 리플렉션 바인딩이 실패하면(타입 서술자 캐시 문제 등) 조용히
    /// item.ToString()으로 폴백해 "MiniERP2.Models.AddressBookEntry"가 그대로 노출된다. 그 폴백
    /// 경로에서도 라벨이 뜨도록 여기서 직접 오버라이드해둔다.
    /// </summary>
    public override string ToString() => Label;
}

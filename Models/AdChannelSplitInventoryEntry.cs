namespace MiniERP2.Models;

/// <summary>
/// 광고비 채널 분리(§4.2 "inventory")의 (헤더, 값) 완전일치 규칙 1건입니다. 캠페인은 신규 생성이
/// 드물어 육안 확정 결과를 저장소에 쌓아두고 재사용하는 방식입니다. #6과 #13(동명 "브러쉬")처럼
/// 값만으로는 구분이 안 되는 캠페인이 있어 (HeaderName, Value) 조합을 키로 사용합니다.
/// </summary>
public class AdChannelSplitInventoryEntry
{
    public long Id { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string HeaderName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string TargetChannel { get; set; } = string.Empty;
    public string? ConfirmedAt { get; set; }
    public string? LastSeenYymm { get; set; }
    public decimal LastCost { get; set; }

    // ── 아래는 DB에 저장하지 않는, 인벤토리 팝업 표시 전용 계산 필드 ──
    // (현재 불러온 _loadedAdItems 기준으로 AdMappingForm이 채워준다.)
    public int RowCount { get; set; }
    public bool IsNew { get; set; }
    public bool IsMissingThisMonth { get; set; }
}

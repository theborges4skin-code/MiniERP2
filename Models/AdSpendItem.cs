namespace MiniERP2.Models;

/// <summary>
/// 광고비 파일(채널 광고 리포트)의 한 행을 나타냅니다. 발주서의 OfsOrderItem과 같은 역할이지만
/// 광고매핑은 마스터SKU/CSKU가 아니라 상품그룹 단위로만 분류합니다.
/// </summary>
public class AdSpendItem
{
    public string? ChannelCode { get; set; }
    public string? ProductName { get; set; }
    public string? ProductId { get; set; }
    public string? OptionName { get; set; }
    public decimal Cost { get; set; }
    public string? Extra1 { get; set; }
    public string? Extra2 { get; set; }
    public string? Note1 { get; set; }
    public string? Note2 { get; set; }
    public string? Note3 { get; set; }

    // 매핑/변환 데이터
    public string? MappedGroup { get; set; }
    public string? MatchType { get; set; }
    public string? Status { get; set; }

    /// <summary>
    /// 광고비 채널 분리(캠페인 → 하위채널, AdChannelSplit_Spec.md §4)용 파생 필드. "채널 분리 규칙"
    /// 탭에서 분리 기능을 꺼두면(AdChannelSplitRepository.GetSettings의 Enabled == false) 항상 null이다.
    /// CAMPAIGN_SRC/CAMPAIGN_KEY는 값의 출처 헤더명과 캠페인 문자열(예: "캠페인명"/"브러쉬"),
    /// RESOLVED_CHANNEL/CHANNEL_MATCH_TYPE은 판정된 하위채널과 판정 방식("선규칙"/"인벤토리"/"미분류")이다.
    /// </summary>
    public string? CampaignSrc { get; set; }
    public string? CampaignKey { get; set; }
    public string? ResolvedChannel { get; set; }
    public string? ChannelMatchType { get; set; }

    /// <summary>
    /// 광고비 파일 원본 행의 (헤더명 -> 값) 전체. SalesManagerV2의 "광고매핑상세" 시트(원본 열 +
    /// 매핑결과 열)를 그대로 이식하기 위해 AdSpendLoader가 채워준다.
    /// </summary>
    public Dictionary<string, string>? RawValues { get; set; }
}

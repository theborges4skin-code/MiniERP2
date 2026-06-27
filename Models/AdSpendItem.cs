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

    // 매핑/변환 데이터
    public string? MappedGroup { get; set; }
    public string? MatchType { get; set; }
    public string? Status { get; set; }
}

namespace MiniERP2.Models;

/// <summary>
/// 정산파일에서 표준화된 한 행과, 그에 대한 이익분석 결과를 나타냅니다.
/// </summary>
public class SettlementData
{
    public long Id { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? OptionName { get; set; }
    public string? Msku { get; set; }
    public int Qty { get; set; }
    public decimal Settlement { get; set; }
    public decimal Shipping { get; set; }
    public decimal Fee { get; set; }
    public decimal Profit { get; set; }
    public string? Status { get; set; }

    /// <summary>
    /// 정산 파일 원본 행의 (헤더명 -> 값) 전체. "원본데이터" 시트 내보내기 및 추후 디버깅용으로
    /// SettlementLoader가 채워준다. 표준 필드로 매핑되지 않은 열도 모두 포함된다.
    /// </summary>
    public Dictionary<string, string>? RawValues { get; set; }
}

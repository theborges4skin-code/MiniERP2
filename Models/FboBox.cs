namespace MiniERP2.Models;

/// <summary>
/// FBO 발주 안의 박스 1개 = 반품부명(ReceiverDisplayName)이 같으면 풀필먼트사가 자동 합포장해
/// 이송장 1개로 발급하는 단위(기획서 §2.1). MatchKey는 이송장 결과 파일과 매칭할 때 쓰는 박스
/// 단위 키(고객주문번호, §6.2)이다.
/// </summary>
public class FboBox
{
    public required string FboNo { get; set; }
    public int BoxSeq { get; set; }
    public string ReceiverDisplayName { get; set; } = string.Empty;
    public string MatchKey { get; set; } = string.Empty;
    public string BoxType { get; set; } = "소";
    public string? TrackingNo { get; set; }
    public DateTime? TrackingLoadedAt { get; set; }
    public string Status { get; set; } = "대기";
}

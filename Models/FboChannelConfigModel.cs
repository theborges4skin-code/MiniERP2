namespace MiniERP2.Models;

/// <summary>
/// 네이버 풀필먼트(FBO) 센터 1곳의 발주지 설정입니다. 발주 등록 화면에서 채널을 고르면 이 값들이
/// 반품부(수취인)·주소로 고정 표시되고, 반품부명 채번(ReceiverSeqFormat)과 고객주문번호 프리픽스
/// (OrderNoPrefix)가 박스 단위 매칭키를 만드는 데 쓰인다(기획서 §4.2/§6.2).
/// </summary>
public class FboChannelConfigModel
{
    public required string ChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string ReceiverName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ReceiverSeqFormat { get; set; } = "{name}{seq:00}";
    public string ChannelLabel { get; set; } = string.Empty;
    public string OrderNoPrefix { get; set; } = "#FBO";
    public string InboundType { get; set; } = "31";
    public bool IsDefault { get; set; }
}

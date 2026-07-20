namespace MiniERP2.Models;

/// <summary>
/// 네이버 풀필먼트(FBO) 발주 1건의 헤더. 실제 박스/품목은 <see cref="FboBox"/>/<see cref="FboBoxItem"/>이
/// 담당하며, 여기의 ReceiverName/Phone/Address는 저장 시점의 FboChannelConfig 값을 스냅샷한 것이다
/// (기획서 §4.3 — 발주지 설정이 나중에 바뀌어도 과거 발주 이력은 바뀌지 않아야 함).
/// </summary>
public class FboOrder
{
    public required string FboNo { get; set; }
    public DateTime OrderDate { get; set; }
    public required string ChannelId { get; set; }
    public string ReceiverName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Status { get; set; } = "작성중";
    public string Memo { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

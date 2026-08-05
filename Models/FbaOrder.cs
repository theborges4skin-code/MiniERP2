namespace MiniERP2.Models;

/// <summary>
/// 아마존 FBA 발주 1건의 헤더. 실제 박스/품목은 <see cref="FbaBox"/>/<see cref="FbaBoxItem"/>이
/// 담당하며, 여기의 ReceiverName/Phone/Address는 저장 시점의 FbaConfig 값을 스냅샷한 것이다
/// (기획서 §3.4 — 발주지 설정이 나중에 바뀌어도 과거 발주 이력은 바뀌지 않아야 함).
/// </summary>
public class FbaOrder
{
    public required string FbaNo { get; set; }
    public DateTime OrderDate { get; set; }
    /// <summary>미입력 허용. 비면 하배출고이서·선적명세 출력 시 해당 자리만 공란(§7.1).</summary>
    public string? ShipmentId { get; set; }
    public string ReceiverName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Status { get; set; } = "작성중";
    public string Memo { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

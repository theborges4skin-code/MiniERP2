namespace MiniERP2.Models;

/// <summary>
/// 아마존 FBA 박스규격 마스터 1건. FbaOrderForm에서 박스 추가 시 콤보로 선택하며, 선택 시점의
/// 치수를 FbaBox에 스냅샷으로 복사한다 — 이 마스터의 치수를 나중에 고쳐도 과거 발주 재출력값은
/// 바뀌지 않아야 하기 때문이다.
/// </summary>
public class FbaBoxSpec
{
    public required string BoxName { get; set; }
    public double WidthMm { get; set; }
    public double DepthMm { get; set; }
    public double HeightMm { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? UpdatedAt { get; set; }
}

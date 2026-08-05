namespace MiniERP2.Models;

/// <summary>
/// 아마존 FBA 발주 안의 박스 1개 = 엑셀의 carton no.(BoxSeq). FBO와 달리 CSKU 혼재가 기본이며,
/// 박스규격 마스터를 선택한 시점의 치수를 스냅샷으로 갖는다(마스터 치수가 나중에 바뀌어도 과거
/// 발주 재출력값은 불변이어야 함, 기획서 §3.2/§3.4). MatchKey는 고객주문번호 문자열(§7.1)이다.
/// </summary>
public class FbaBox
{
    public required string FbaNo { get; set; }
    public int BoxSeq { get; set; }
    /// <summary>규격 마스터 이름. 직접입력이면 "(직접입력)".</summary>
    public string BoxSpecName { get; set; } = string.Empty;
    public double WidthMm { get; set; }
    public double DepthMm { get; set; }
    public double HeightMm { get; set; }
    public bool IsCustomSize { get; set; }
    /// <summary>자동계산값(Σ UnitWeightG × Qty). 그리드에서 수동 덮어쓰기 가능.</summary>
    public double WeightG { get; set; }
    public string MatchKey { get; set; } = string.Empty;
    public string? TrackingNo { get; set; }
    public DateTime? TrackingLoadedAt { get; set; }
    public string Status { get; set; } = "대기";
}

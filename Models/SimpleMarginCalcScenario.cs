namespace MiniERP2.Models;

/// <summary>정산 마진 계산기 임시저장 스냅샷 — 행 데이터 전체를 통째로 저장해 이후 그대로 복원한다.
/// 같은 CSKU 목록으로 반복 계산할 때 매번 다시 불러오지 않도록 하기 위함(최근 5개까지,
/// <see cref="Config.SimpleMarginCalculatorScenarioService"/>).</summary>
public class SimpleMarginCalcScenario
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>목록 표시용 이름. 사용자가 직접 입력하거나, 비워두면 품목명 기반으로 자동 생성된다.</summary>
    public string Label { get; set; } = "";

    public DateTime SavedAt { get; set; } = DateTime.Now;

    public List<SimpleMarginCalcRow> Rows { get; set; } = new();
}

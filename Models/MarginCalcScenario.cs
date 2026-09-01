using MiniERP2.Utils;

namespace MiniERP2.Models;

/// <summary>간이 마진 계산기 임시저장 스냅샷 — 화면 상태(비용 항목 정의·행 데이터 전체)를 통째로 저장해
/// 이후 그대로 복원한다. 네고 문의 등으로 같은 계산을 반복할 때 매번 처음부터 세팅하지 않도록 하기
/// 위함(최근 5개까지, <see cref="Config.MarginCalculatorScenarioService"/>).</summary>
public class MarginCalcScenario
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>목록 표시용 이름. 사용자가 직접 입력하거나, 비워두면 품목명 기반으로 자동 생성된다.</summary>
    public string Label { get; set; } = "";

    public DateTime SavedAt { get; set; } = DateTime.Now;

    public MarginCalcMode Mode { get; set; }
    public bool VatExcluded { get; set; }
    public int RoundingUnitIndex { get; set; } = 1;
    public bool QtyColumnVisible { get; set; } = true;

    public List<MarginCostItemDef> CostItems { get; set; } = new();
    public List<MarginCalcRow> Rows { get; set; } = new();
}

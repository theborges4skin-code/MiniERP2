using MiniERP2.Utils;

namespace MiniERP2.Models;

/// <summary>모드 C(납품가 산출)에서 판매가/할인율 중 어느 쪽을 사용자가 직접 입력했는지 기억한다(간이마진계산기_개발기획서.md §4.5).</summary>
public enum MarginSupplyInputSource
{
    None,
    Price,
    Discount,
}

/// <summary>비용 항목 정의 — 전역 패널의 기본값 한 줄(§3).</summary>
public class MarginCostItemDef
{
    /// <summary>행별 override(<see cref="MarginCalcRow.CostOverrides"/>)를 이름이 아닌 Id로 연결한다 —
    /// 항목명을 나중에 바꿔도 이미 입력된 override가 끊기지 않게 하기 위함.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
    public bool IsRate { get; set; } = true;
    public decimal Value { get; set; }
    public CostApplyUnit Unit { get; set; } = CostApplyUnit.PerUnit;

    /// <summary>모드 C 진입 시 프리셋을 "거래처"로 자동 전환하되 사용자가 이미 손댄 값은 유지한다(§3.1).</summary>
    public bool IsManuallyEdited { get; set; }
}

/// <summary>계산기 그리드의 행 하나(SKU 한 건, 또는 SKU 없이 수기 입력한 행).</summary>
public class MarginCalcRow
{
    public string? ChannelCode { get; set; }
    public string? ChannelName { get; set; }
    public string? CskuCode { get; set; }
    public string? Msku { get; set; }
    public string? ItemName { get; set; }

    /// <summary>마스터/CSKU 원가 조회값(§5). SKU 불러오기 없이 수기 입력한 행은 0.</summary>
    public decimal CostPriceDb { get; set; }

    /// <summary>초기값=CostPriceDb, 사용자가 임시 수정 가능(§5.3).</summary>
    public decimal CostPriceApplied { get; set; }

    public decimal? RetailPrice { get; set; }
    public decimal? SupplyPriceDb { get; set; }

    public decimal? Quantity { get; set; }

    /// <summary>판매가/납품가 셀의 현재 표시값. 모드 B는 사용자 입력, 모드 A는 계산 결과, 모드 C는 §4.5 참고.</summary>
    public decimal? SalePrice { get; set; }

    /// <summary>모드 A 전용 입력 — 목표마진율(소수).</summary>
    public decimal? TargetMarginRate { get; set; }

    /// <summary>할인율 셀의 현재 표시값(모드 C).</summary>
    public decimal? DiscountRate { get; set; }

    public MarginSupplyInputSource SupplyInputSource { get; set; } = MarginSupplyInputSource.None;

    /// <summary>비용 항목별 행 override. <see cref="MarginCostItemDef.Id"/> → 값(없으면 전역 기본값 적용, §3).</summary>
    public Dictionary<Guid, decimal?> CostOverrides { get; } = new();

    // ── 계산 결과 캐시(그리드 표시용) ──
    public bool IsComputable { get; set; }
    public string? ComputeReason { get; set; }
    public decimal? CostOfGoods { get; set; }
    public decimal? ProfitAmount { get; set; }
    public decimal? MarginRate { get; set; }
    public decimal? SaleAmountTotal { get; set; }
    public decimal? ProfitAmountTotal { get; set; }
    public decimal? CostOfGoodsTotal { get; set; }
}

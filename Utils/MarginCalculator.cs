namespace MiniERP2.Utils;

/// <summary>
/// 간이 마진 계산기의 핵심 계산 엔진(간이마진계산기_개발기획서.md §4). UI/DB에 의존하지 않는 순수 함수.
/// 원가·비용항목은 이미 VAT 환산이 끝난 값으로 전달받는다 — VAT 환산(§4.6)은 호출부 책임이다.
/// </summary>
public enum MarginCalcMode
{
    /// <summary>모드 A — 목표마진을 입력해 판매가를 역산.</summary>
    SalePrice,

    /// <summary>모드 B — 판매가를 입력해 이익액/마진율을 정방향 계산.</summary>
    Margin,

    /// <summary>모드 C — 할인율 또는 납품가 중 하나를 입력해 나머지를 산출.</summary>
    Supply,
}

public enum CostApplyUnit
{
    /// <summary>건당 — 입력값을 그대로 단위당 비용으로 쓴다.</summary>
    PerUnit,

    /// <summary>총액 — 입력값을 수량으로 나눠 단위당 비용으로 환산한다(수량 필수).</summary>
    Total,
}

/// <summary>비용 항목 한 줄(전역 기본값 또는 행별 override 어느 쪽이든 이 형태로 전달).</summary>
public sealed class MarginCostItemInput
{
    public string Name { get; init; } = string.Empty;

    /// <summary>true면 율(%) 방식(<see cref="Value"/>는 0.2 = 20% 같은 소수), false면 정액 방식.</summary>
    public bool IsRate { get; init; }

    /// <summary>율 방식이면 소수(0.2=20%), 정액 방식이면 원화 금액(환산 전, <see cref="Unit"/> 기준).</summary>
    public decimal Value { get; init; }

    /// <summary>정액 방식에서만 의미가 있다. 율 방식은 항상 판매가 비례라 적용단위 구분이 없다(§4.2).</summary>
    public CostApplyUnit Unit { get; init; } = CostApplyUnit.PerUnit;
}

public sealed class MarginCalcInput
{
    public MarginCalcMode Mode { get; init; }

    /// <summary>제조원가(단위당, VAT 환산 완료된 값) — C.</summary>
    public decimal CostPrice { get; init; }

    public IReadOnlyList<MarginCostItemInput> CostItems { get; init; } = Array.Empty<MarginCostItemInput>();

    /// <summary>수량 — Q. 총액형 정액 항목의 단위당 환산에 필요.</summary>
    public decimal? Quantity { get; init; }

    /// <summary>모드 A 전용 — 목표마진율(소수, 0.4=40%) m.</summary>
    public decimal? TargetMarginRate { get; init; }

    /// <summary>모드 A 전용 — 판매가 반올림 단위(§4.7, 기본 10원 올림).</summary>
    public int RoundingUnit { get; init; } = 10;

    /// <summary>모드 B 전용 — 판매가 입력값 P.</summary>
    public decimal? SalePrice { get; init; }

    /// <summary>모드 C 전용 — 권장소비자가 L.</summary>
    public decimal? RetailPrice { get; init; }

    /// <summary>모드 C 전용 — 할인율 입력 d. <see cref="SupplyPriceInput"/>과 상호배타(둘 다 있으면 SupplyPriceInput 우선).</summary>
    public decimal? DiscountRate { get; init; }

    /// <summary>모드 C 전용 — 납품가 직접입력 P.</summary>
    public decimal? SupplyPriceInput { get; init; }
}

public sealed class MarginCalcResult
{
    public bool IsComputable { get; init; }

    /// <summary>계산 불가/보류 사유(§4.4, §7). 예: "산출불가", "수량 필요".</summary>
    public string? Reason { get; init; }

    /// <summary>판매가 또는 납품가 — P.</summary>
    public decimal? SalePrice { get; init; }

    /// <summary>모드 C 전용 산출 결과 — d.</summary>
    public decimal? DiscountRate { get; init; }

    public decimal? CostOfGoods { get; init; }
    public decimal? ProfitAmount { get; init; }
    public decimal? MarginRate { get; init; }
    public decimal? SaleAmountTotal { get; init; }
    public decimal? ProfitAmountTotal { get; init; }
    public decimal? CostOfGoodsTotal { get; init; }

    public static MarginCalcResult NotComputable(string reason) => new() { IsComputable = false, Reason = reason };
}

public static class MarginCalculator
{
    public static MarginCalcResult Calculate(MarginCalcInput input)
    {
        var needsQtyForFlatTotal = input.CostItems.Any(i => !i.IsRate && i.Unit == CostApplyUnit.Total);
        if (needsQtyForFlatTotal && (input.Quantity is null || input.Quantity == 0))
        {
            return MarginCalcResult.NotComputable("수량 필요");
        }

        var sumRates = input.CostItems.Where(i => i.IsRate).Sum(i => i.Value);
        var sumFlatPerUnit = input.CostItems.Where(i => !i.IsRate)
            .Sum(i => i.Unit == CostApplyUnit.PerUnit ? i.Value : i.Value / input.Quantity!.Value);

        return input.Mode switch
        {
            MarginCalcMode.SalePrice => CalculateSalePrice(input, sumRates, sumFlatPerUnit),
            MarginCalcMode.Margin => CalculateMargin(input, sumRates, sumFlatPerUnit, input.SalePrice),
            MarginCalcMode.Supply => CalculateSupply(input, sumRates, sumFlatPerUnit),
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };
    }

    private static MarginCalcResult CalculateSalePrice(MarginCalcInput input, decimal sumRates, decimal sumFlatPerUnit)
    {
        if (input.TargetMarginRate is not { } m)
        {
            return MarginCalcResult.NotComputable("목표마진 필요");
        }

        var denom = 1m - sumRates - m;
        if (denom <= 0)
        {
            return MarginCalcResult.NotComputable(
                $"목표마진 {m:P0} + 비용율 {sumRates:P0} = {m + sumRates:P0} → 합이 100% 이상이면 산출 불가");
        }

        var rawPrice = (input.CostPrice + sumFlatPerUnit) / denom;
        var roundedPrice = RoundUpToUnit(rawPrice, input.RoundingUnit);

        // §4.7: 반올림 후 마진율은 반올림된 판매가 기준으로 재계산(목표마진과 미세하게 달라지는 것이 정상).
        return CalculateMargin(input, sumRates, sumFlatPerUnit, roundedPrice);
    }

    private static MarginCalcResult CalculateMargin(MarginCalcInput input, decimal sumRates, decimal sumFlatPerUnit, decimal? salePrice)
    {
        if (salePrice is null || salePrice == 0)
        {
            return new MarginCalcResult
            {
                IsComputable = true,
                SalePrice = salePrice == 0 ? 0m : null,
                DiscountRate = ResolveDiscountRate(input, salePrice: salePrice == 0 ? 0m : null),
            };
        }

        var p = salePrice.Value;
        var costOfGoods = input.CostPrice + p * sumRates + sumFlatPerUnit;
        var profitAmount = p - costOfGoods;
        var marginRate = profitAmount / p;

        decimal? saleAmountTotal = null, profitAmountTotal = null, costOfGoodsTotal = null;
        if (input.Quantity is { } q)
        {
            saleAmountTotal = q * p;
            profitAmountTotal = q * profitAmount;
            costOfGoodsTotal = q * costOfGoods;
        }

        return new MarginCalcResult
        {
            IsComputable = true,
            SalePrice = p,
            DiscountRate = ResolveDiscountRate(input, p),
            CostOfGoods = costOfGoods,
            ProfitAmount = profitAmount,
            MarginRate = marginRate,
            SaleAmountTotal = saleAmountTotal,
            ProfitAmountTotal = profitAmountTotal,
            CostOfGoodsTotal = costOfGoodsTotal,
        };
    }

    private static MarginCalcResult CalculateSupply(MarginCalcInput input, decimal sumRates, decimal sumFlatPerUnit)
    {
        decimal? resolvedPrice;

        if (input.SupplyPriceInput is { } directPrice)
        {
            // §4.5: 할인율과 납품가는 상호 배타 입력. 둘 다 있으면 직접입력한 납품가를 우선한다.
            resolvedPrice = directPrice;
        }
        else if (input.DiscountRate is { } d)
        {
            // L이 공란이면 납품가 자체를 산출할 수 없다(할인율만으로는 금액이 안 나옴).
            resolvedPrice = input.RetailPrice is { } l ? l * (1 - d) : null;
        }
        else
        {
            resolvedPrice = null;
        }

        if (resolvedPrice is null)
        {
            return MarginCalcResult.NotComputable("할인율 또는 납품가 필요");
        }

        return CalculateMargin(input, sumRates, sumFlatPerUnit, resolvedPrice);
    }

    private static decimal? ResolveDiscountRate(MarginCalcInput input, decimal? salePrice)
    {
        if (input.Mode != MarginCalcMode.Supply) return null;

        // 직접입력된 할인율이 있으면(그리고 납품가를 직접 입력한 게 아니면) 그대로 보존.
        if (input.SupplyPriceInput is null && input.DiscountRate is { } d) return d;

        // §4.5: L이 공란이면 할인율 산출 불가 → 공란.
        if (input.RetailPrice is not { } l || l == 0 || salePrice is not { } p) return null;

        return 1 - p / l;
    }

    private static decimal RoundUpToUnit(decimal value, int unit)
    {
        if (unit <= 0) return value;
        return Math.Ceiling(value / unit) * unit;
    }
}

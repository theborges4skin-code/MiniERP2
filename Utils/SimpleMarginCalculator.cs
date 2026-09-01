namespace MiniERP2.Utils;

/// <summary>
/// 정산 마진 계산기의 계산 엔진. UI/DB에 의존하지 않는 순수 함수.
/// 정산금액이 입력되면(수수료 입력 여부와 무관하게) 실수령액 기준으로 우선 계산하고,
/// 정산금액이 없으면 판매금액 기준(수수료 있으면 차감)으로 계산한다.
/// </summary>
public sealed class SimpleMarginCalcInput
{
    public decimal CostPrice { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? SaleAmount { get; init; }
    public decimal? SettlementAmount { get; init; }

    /// <summary>수수료율(소수, 0.1=10%).</summary>
    public decimal? FeeRate { get; init; }
}

public sealed class SimpleMarginCalcResult
{
    public bool IsComputable { get; init; }
    public string? Reason { get; init; }
    public decimal? CostOfGoodsTotal { get; init; }
    public decimal? ProfitAmount { get; init; }
    public decimal? ProfitPerUnit { get; init; }
    public decimal? SalePerUnit { get; init; }

    /// <summary>이익액 계산에 실제로 쓰인 매출 기준액(정산금액 우선, 없으면 판매금액) — 마진율의 분모.</summary>
    public decimal? RevenueBasis { get; init; }

    /// <summary>마진율 = 이익액 / RevenueBasis.</summary>
    public decimal? MarginRate { get; init; }

    public static SimpleMarginCalcResult NotComputable(string reason) => new() { IsComputable = false, Reason = reason };
}

public static class SimpleMarginCalculator
{
    public static SimpleMarginCalcResult Calculate(SimpleMarginCalcInput input)
    {
        if (input.Quantity is not { } quantity || quantity <= 0)
            return SimpleMarginCalcResult.NotComputable("수량 필요");

        var costTotal = input.CostPrice * quantity;
        decimal profit;
        decimal revenueBasis;

        if (input.SettlementAmount is { } settlement)
        {
            profit = settlement - costTotal;
            revenueBasis = settlement;
        }
        else if (input.SaleAmount is { } sale)
        {
            profit = input.FeeRate is { } fee ? sale - sale * fee - costTotal : sale - costTotal;
            revenueBasis = sale;
        }
        else
        {
            return SimpleMarginCalcResult.NotComputable("판매금액 또는 정산금액 필요");
        }

        return new SimpleMarginCalcResult
        {
            IsComputable = true,
            CostOfGoodsTotal = costTotal,
            ProfitAmount = profit,
            ProfitPerUnit = profit / quantity,
            SalePerUnit = input.SaleAmount is { } s ? s / quantity : null,
            RevenueBasis = revenueBasis,
            MarginRate = revenueBasis == 0 ? null : profit / revenueBasis,
        };
    }
}

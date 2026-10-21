namespace MiniERP2.MFG.Domain;

/// <summary>
/// 제조법·완제품 원가 계산(순수 로직, DB 접근 없음). 계산식은 제조법관련(신규).xlsx 배합 시트와 같다:
/// <list type="bullet">
/// <item>kg당 원료비 = Σ(투입량 × 원료 단가[원/kg])  — 투입량 합계로 나누지 않는다(엑셀과 동일).</item>
/// <item>계산 비중 = Σ투입량(비중 있는 원료) ÷ Σ(투입량 ÷ 비중), 소수 셋째 자리 반올림. 비중 있는 원료가 없으면 1.</item>
/// <item>적용 비중 = 실측 비중 ?? 계산 비중.  L당 원료비 = kg당 × 적용 비중.</item>
/// <item>용량별: 충진중량(g) = 용량 × 적용 비중, 원액비 = kg당 × 충진중량 ÷ 1000, 완제품 단가 = 원액비 + 부자재·가공비 합.</item>
/// </list>
/// 모든 금액은 <paramref name="showIncluded"/> 보기 기준으로 환산해 돌려준다(과세 항목만 ÷1.1).
/// </summary>
public class CostCalculator
{
    private readonly Func<long, Material?> _material;
    private readonly Func<long, PackMaterial?> _pack;

    public CostCalculator(Func<long, Material?> materialLookup, Func<long, PackMaterial?> packLookup)
    {
        _material = materialLookup;
        _pack = packLookup;
    }

    public RecipeCostResult CalculateRecipe(Recipe recipe, bool showIncluded)
    {
        var result = new RecipeCostResult();
        double densWeight = 0, densVolume = 0;

        foreach (var line in recipe.Lines.OrderBy(l => l.SortOrder))
        {
            var m = _material(line.MaterialId);
            var lr = new RecipeLineCost { Line = line, Material = m };
            result.TotalPercent += line.Percent;

            if (m?.CurrentPrice is { } price)
            {
                lr.PricePerKg = Vat.ToView(price.PricePerKg, showIncluded, m.IsTaxFree);
                lr.CostPerKgProduct = line.Percent * lr.PricePerKg.Value;
                result.KgCost += lr.CostPerKgProduct.Value;
                if (price.IsUncertain) result.UncertainPriceCount++;
            }
            else if (line.Percent > 0)
            {
                result.MissingPriceCount++;
            }

            if (m?.Density is > 0 && line.Percent > 0)
            {
                densWeight += line.Percent;
                densVolume += line.Percent / m.Density.Value;
            }
            else if (line.Percent > 0)
            {
                result.MissingDensityCount++;
            }

            result.Lines.Add(lr);
        }

        result.CalculatedDensity = densVolume > 0 ? Math.Round(densWeight / densVolume, 3) : 1;
        result.AppliedDensity = recipe.MeasuredDensity is > 0 ? recipe.MeasuredDensity.Value : result.CalculatedDensity;
        result.LiterCost = result.KgCost * result.AppliedDensity;

        foreach (var size in recipe.Sizes.OrderBy(s => s.SortOrder))
        {
            result.Sizes.Add(CalculateSize(size, result.KgCost, result.AppliedDensity, showIncluded));
        }
        return result;
    }

    public RecipeSizeCostResult CalculateSize(RecipeSize size, double kgCostView, double density, bool showIncluded)
    {
        var r = new RecipeSizeCostResult { Size = size };
        r.FillWeightG = size.VolumeMl * density;
        r.RawCost = kgCostView * r.FillWeightG / 1000;
        foreach (var cost in size.Costs)
        {
            var amount = CostLineAmount(cost, showIncluded);
            r.CostByCategory[cost.Category] = r.CostByCategory.GetValueOrDefault(cost.Category) + amount;
            r.PackSubtotal += amount;
        }
        r.UnitCost = r.RawCost + r.PackSubtotal;
        r.CostPerMl = size.VolumeMl > 0 ? r.UnitCost / size.VolumeMl : 0;
        return r;
    }

    /// <summary>용량별 원가 한 줄의 금액(보기 기준). 부자재 연결이 있으면 현재 단가 × 수량, 없으면 직접 입력 금액.</summary>
    public double CostLineAmount(RecipeSizeCost cost, bool showIncluded)
    {
        if (cost.PackMaterialId is { } packId && _pack(packId) is { CurrentPrice: { } p } pack)
        {
            return Vat.ToView(p.UnitPrice, showIncluded, pack.IsTaxFree) * cost.Quantity;
        }
        return Vat.ToView(cost.ManualAmount ?? 0, showIncluded);
    }

    /// <summary>원료 소분 판매품의 원료비(보기 기준). 단위 ml/L는 비중을 곱하고, g/kg는 그대로 쓴다.</summary>
    public double? MaterialPortionCost(long materialId, double qty, string unit, bool showIncluded)
    {
        var m = _material(materialId);
        if (m?.CurrentPrice is not { } price) return null;
        var kgPrice = Vat.ToView(price.PricePerKg, showIncluded, m.IsTaxFree);
        var density = m.Density is > 0 ? m.Density.Value : 1;
        var kg = unit switch
        {
            "L" => qty * density,
            "g" => qty / 1000,
            "kg" => qty,
            _ => qty / 1000 * density, // ml
        };
        return kgPrice * kg;
    }
}

public class RecipeCostResult
{
    public List<RecipeLineCost> Lines { get; } = [];
    public List<RecipeSizeCostResult> Sizes { get; } = [];
    public double TotalPercent { get; set; }
    public double KgCost { get; set; }
    public double LiterCost { get; set; }
    public double CalculatedDensity { get; set; } = 1;
    public double AppliedDensity { get; set; } = 1;
    public int MissingPriceCount { get; set; }
    public int MissingDensityCount { get; set; }
    public int UncertainPriceCount { get; set; }

    public bool TotalIsOk => Math.Abs(TotalPercent - 1) < 0.000001;
}

public class RecipeLineCost
{
    public required RecipeLine Line { get; init; }
    public Material? Material { get; init; }
    public double? PricePerKg { get; set; }
    public double? CostPerKgProduct { get; set; }
}

public class RecipeSizeCostResult
{
    public required RecipeSize Size { get; init; }
    public double FillWeightG { get; set; }
    public double RawCost { get; set; }
    public Dictionary<string, double> CostByCategory { get; } = new();
    public double PackSubtotal { get; set; }
    public double UnitCost { get; set; }
    public double CostPerMl { get; set; }
}

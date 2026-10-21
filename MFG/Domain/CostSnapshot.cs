using System.Text.Json;
using MiniERP2.MFG.Data;

namespace MiniERP2.MFG.Domain;

/// <summary>
/// 화면 한 번 그릴 때 쓰는 mfgdb 전체 스냅샷(원료·부자재·제조법·완제품) + 계산기 묶음.
/// 데이터가 수천 건 수준이라 통째로 읽어 메모리에서 계산하는 편이 단순하고 빠르다.
/// </summary>
public class CostSnapshot
{
    public Dictionary<long, Material> Materials { get; }
    public Dictionary<long, PackMaterial> Packs { get; }
    public List<Recipe> Recipes { get; }
    public Dictionary<long, (Recipe Recipe, RecipeSize Size)> SizesById { get; }
    public Dictionary<long, FinishedProduct> Products { get; }
    public CostCalculator Calculator { get; }

    private readonly Dictionary<long, RecipeConfirmLog> _latestConfirm;

    public CostSnapshot(List<Material> materials, List<PackMaterial> packs, List<Recipe> recipes, List<FinishedProduct> products, List<RecipeConfirmLog> confirmLogs)
    {
        Materials = materials.ToDictionary(m => m.Id);
        Packs = packs.ToDictionary(p => p.Id);
        Recipes = recipes;
        SizesById = recipes.SelectMany(r => r.Sizes.Select(s => (r, s))).ToDictionary(x => x.s.Id, x => (x.r, x.s));
        Products = products.ToDictionary(p => p.Id);
        Calculator = new CostCalculator(id => Materials.GetValueOrDefault(id), id => Packs.GetValueOrDefault(id));
        _latestConfirm = confirmLogs.GroupBy(l => l.RecipeId).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.Id).First());
    }

    public static CostSnapshot Load() => new(
        new MaterialRepository().GetAll(), new PackMaterialRepository().GetAll(), new RecipeRepository().GetAll(),
        new ProductRepository().GetAll(), new RecipeRepository().GetConfirmLogs());

    public RecipeCostResult Recipe(Recipe r, bool showIncluded) => Calculator.CalculateRecipe(r, showIncluded);

    public double? SizeUnitCost(long sizeId, bool showIncluded)
    {
        if (!SizesById.TryGetValue(sizeId, out var x)) return null;
        var recipeResult = Calculator.CalculateRecipe(new Recipe { Lines = x.Recipe.Lines, MeasuredDensity = x.Recipe.MeasuredDensity, Sizes = [] }, showIncluded);
        return Calculator.CalculateSize(x.Size, recipeResult.KgCost, recipeResult.AppliedDensity, showIncluded).UnitCost;
    }

    public ProductCostCalculator ProductCalculator(bool showIncluded) =>
        new(id => SizeUnitCost(id, showIncluded), id => Products.GetValueOrDefault(id), Calculator);

    public string SizeLabel(long sizeId) =>
        SizesById.TryGetValue(sizeId, out var x) ? $"{x.Recipe.Code} {x.Recipe.Name} {Fmt.Num(x.Size.VolumeMl)}ml" : "(삭제된 용량)";

    /// <summary>
    /// 확정된 제조법인데 확정 시점 이후 원료 단가 등이 바뀌어 지금 계산한 kg당 원료비(VAT포함)가
    /// 확정 스냅샷과 0.5원 넘게 다르면 true — "확정 후 단가 변동 있음, 재확인 필요" 표시용.
    /// </summary>
    public bool RecipeNeedsReconfirm(Recipe r)
    {
        if (!r.IsConfirmed || !_latestConfirm.TryGetValue(r.Id, out var log)) return false;
        return Math.Abs(Calculator.CalculateRecipe(r, true).KgCost - log.KgCost) > 0.5;
    }

    public RecipeConfirmLog? LatestConfirm(long recipeId) => _latestConfirm.GetValueOrDefault(recipeId);

    /// <summary>확정된 완제품인데 지금 계산한 합계(VAT포함)가 확정 합계와 0.5원 넘게 다르면 true.</summary>
    public bool ProductNeedsReconfirm(FinishedProduct p, ProductCostCalculator inclCalculator) =>
        p.IsConfirmed && p.ConfirmedTotal is { } t && Math.Abs(inclCalculator.Calculate(p, true).Total - t) > 0.5;

    /// <summary>제조법 확정 스냅샷 JSON(VAT포함 기준): 배합행 단가와 용량별 원가.</summary>
    public string BuildConfirmSnapshot(Recipe r)
    {
        var res = Calculator.CalculateRecipe(r, true);
        var snapshot = new
        {
            r.Code, r.Name, r.Version,
            KgCost = res.KgCost, Density = res.AppliedDensity, TotalPercent = res.TotalPercent,
            Lines = res.Lines.Select(l => new { Material = l.Material?.Code, MaterialName = l.Material?.Name, l.Line.Percent, PricePerKg = l.PricePerKg }),
            Sizes = res.Sizes.Select(s => new { s.Size.VolumeMl, s.RawCost, s.PackSubtotal, s.UnitCost, Costs = s.CostByCategory }),
        };
        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}

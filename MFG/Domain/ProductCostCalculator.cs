namespace MiniERP2.MFG.Domain;

/// <summary>
/// 완제품(SKU) 원가 계산. 열 구성은 product_master와 같다(사용자 결정 2026-10-21: MiniERP2로 보내는
/// 원가도 PM과 같은 방식 — 원료+용기+패키지+스티커 등+기타+택배비 합계).
/// "원료" 열만 연결 유형에 따라 자동 계산된다:
/// <list type="bullet">
/// <item>RecipeSize: 제조법 용량의 완제품 단가(원액비+부자재+가공비). PM 핸드워시처럼 K열에 단품 전체 원가를 넣던 방식과 같다.</item>
/// <item>Material: 원료 kg단가 × 수량(ml이면 비중 반영).</item>
/// <item>Bundle: Σ(구성 SKU의 합계 × 수량). PM의 "K=단품원가×2"와 같다.</item>
/// <item>Manual: 수동값.</item>
/// </list>
/// 나머지 열은 항상 입력값(VAT포함 저장, 과세로 취급)이다.
/// </summary>
public class ProductCostCalculator
{
    private readonly Func<long, double?> _recipeSizeUnitCost;
    private readonly Func<long, FinishedProduct?> _product;
    private readonly CostCalculator _costCalculator;

    /// <param name="recipeSizeUnitCost">RecipeSizeId → 완제품 단가(보기 기준). 계산 불가면 null.</param>
    public ProductCostCalculator(Func<long, double?> recipeSizeUnitCost, Func<long, FinishedProduct?> productLookup, CostCalculator costCalculator)
    {
        _recipeSizeUnitCost = recipeSizeUnitCost;
        _product = productLookup;
        _costCalculator = costCalculator;
    }

    public ProductCostResult Calculate(FinishedProduct p, bool showIncluded) => Calculate(p, showIncluded, new HashSet<long>());

    private ProductCostResult Calculate(FinishedProduct p, bool showIncluded, HashSet<long> visiting)
    {
        var r = new ProductCostResult
        {
            Container = Vat.ToView(p.ContainerCost, showIncluded),
            Package = Vat.ToView(p.PackageCost, showIncluded),
            Sticker = Vat.ToView(p.StickerCost, showIncluded),
            Etc = Vat.ToView(p.EtcCost, showIncluded),
            Shipping = Vat.ToView(p.ShippingCost, showIncluded),
        };

        if (!visiting.Add(p.Id))
        {
            r.Problem = "묶음 구성이 자기 자신을 포함함(순환)";
            return r;
        }

        switch (p.SourceType)
        {
            case ProductSourceType.RecipeSize when p.RecipeSizeId is { } sizeId:
                var unit = _recipeSizeUnitCost(sizeId);
                if (unit is null) r.Problem = "연결된 제조법 용량을 찾을 수 없음";
                r.Raw = unit ?? 0;
                break;
            case ProductSourceType.Material when p.MaterialId is { } mid && p.MaterialQty is { } qty:
                var cost = _costCalculator.MaterialPortionCost(mid, qty, p.MaterialQtyUnit, showIncluded);
                if (cost is null) r.Problem = "원료 단가 없음";
                r.Raw = cost ?? 0;
                break;
            case ProductSourceType.Bundle:
                foreach (var comp in p.Components)
                {
                    var child = _product(comp.ComponentProductId);
                    if (child is null) { r.Problem = "묶음 구성품을 찾을 수 없음"; continue; }
                    var childResult = Calculate(child, showIncluded, visiting);
                    if (childResult.Problem is not null) r.Problem ??= $"구성품 {child}: {childResult.Problem}";
                    r.Raw += childResult.Total * comp.Quantity;
                }
                if (p.Components.Count == 0) r.Problem = "묶음 구성품이 없음";
                break;
            case ProductSourceType.Manual:
                r.Raw = Vat.ToView(p.ManualRaw, showIncluded);
                break;
            default:
                r.Problem = "연결 대상이 지정되지 않음";
                r.Raw = Vat.ToView(p.ManualRaw, showIncluded);
                break;
        }

        visiting.Remove(p.Id);
        return r;
    }
}

public class ProductCostResult
{
    public double Raw { get; set; }
    public double Container { get; set; }
    public double Package { get; set; }
    public double Sticker { get; set; }
    public double Etc { get; set; }
    public double Shipping { get; set; }
    public string? Problem { get; set; }

    public double Total => Raw + Container + Package + Sticker + Etc + Shipping;
}

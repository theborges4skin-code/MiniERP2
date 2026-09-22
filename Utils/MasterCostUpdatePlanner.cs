using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>엑셀에서 읽은 원가 한 줄(마스터DB와 비교하기 전 원본).</summary>
public record MasterCostUpdateImportRow(string Sku, string ItemName, decimal NewCost);

/// <summary>마스터DB 원가와 값이 달라 반영 여부를 선택해야 하는 한 줄.</summary>
public class MasterCostUpdateRow
{
    public string Sku { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public decimal OldCost { get; init; }
    public decimal NewCost { get; init; }
    public decimal Diff => NewCost - OldCost;
}

public class MasterCostUpdatePlan
{
    public List<MasterCostUpdateRow> Changed { get; } = new();
    public int UnchangedCount { get; set; }
    public int NotFoundCount { get; set; }
    public int DuplicateSkuCount { get; set; }
}

/// <summary>
/// 엑셀로 읽은 원가 목록과 마스터DB(ItemTable) 현재 원가를 비교해, 화면에 검토용으로 보여줄 목록을
/// 만든다. CostPrice는 REAL(부동소수)로 저장되므로 소수 4자리(ChannelSkuRepository의 0.#### 표기와
/// 동일 기준) 이하 오차는 "동일"로 취급해 저장 왕복 과정의 부동소수 노이즈로 인한 오탐을 막는다.
/// </summary>
public static class MasterCostUpdatePlanner
{
    private const decimal Tolerance = 0.0001m;

    public static MasterCostUpdatePlan Build(
        IEnumerable<MasterCostUpdateImportRow> imported,
        IReadOnlyDictionary<string, ItemModel> masterBySku)
    {
        var plan = new MasterCostUpdatePlan();
        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in imported)
        {
            if (string.IsNullOrWhiteSpace(row.Sku)) continue;

            // 엑셀 안에서 같은 SKU가 여러 번 나오면 첫 값만 채택하고 나머지는 건너뜀
            if (!seenSkus.Add(row.Sku))
            {
                plan.DuplicateSkuCount++;
                continue;
            }

            if (!masterBySku.TryGetValue(row.Sku, out var existing))
            {
                plan.NotFoundCount++;
                continue;
            }

            if (Math.Abs(existing.CostPrice - row.NewCost) < Tolerance)
            {
                plan.UnchangedCount++;
                continue;
            }

            plan.Changed.Add(new MasterCostUpdateRow
            {
                Sku = row.Sku,
                ItemName = string.IsNullOrWhiteSpace(row.ItemName) ? existing.ItemName : row.ItemName,
                OldCost = existing.CostPrice,
                NewCost = row.NewCost,
            });
        }

        return plan;
    }
}

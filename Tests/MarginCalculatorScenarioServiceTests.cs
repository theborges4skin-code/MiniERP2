using MiniERP2.Config;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

/// <summary>간이 마진 계산기 임시저장(§9) — JSON 직렬화 왕복(비용항목 Id ↔ 행의 CostOverrides 연결
/// 유지) 및 최근 5개 제한을 검증한다.</summary>
[TestClass]
public class MarginCalculatorScenarioServiceTests
{
    private static string TempFilePath() => Path.Combine(Path.GetTempPath(), $"margin_scenarios_test_{Guid.NewGuid():N}.json");

    [TestMethod]
    public void Save_RoundTripsRowsAndCostOverrideLinkage()
    {
        var path = TempFilePath();
        try
        {
            var service = new MarginCalculatorScenarioService(path);
            var costItem = new MarginCostItemDef { Name = "광고비", IsRate = true, Value = 0.2m };
            var row = new MarginCalcRow { Msku = "SKU1", ItemName = "테스트상품", CostPriceApplied = 1000m, SalePrice = 2000m };
            row.CostOverrides[costItem.Id] = 0.15m;

            var scenario = new MarginCalcScenario
            {
                Label = "테스트 저장",
                Mode = MarginCalcMode.Margin,
                CostItems = new List<MarginCostItemDef> { costItem },
                Rows = new List<MarginCalcRow> { row },
            };

            service.Save(scenario);
            var loaded = service.LoadAll();

            Assert.AreEqual(1, loaded.Count);
            var loadedScenario = loaded[0];
            Assert.AreEqual("테스트 저장", loadedScenario.Label);
            Assert.AreEqual(1, loadedScenario.Rows.Count);
            Assert.AreEqual("SKU1", loadedScenario.Rows[0].Msku);

            var loadedCostItemId = loadedScenario.CostItems[0].Id;
            Assert.AreEqual(costItem.Id, loadedCostItemId); // Id가 그대로 복원되어야 함
            Assert.IsTrue(loadedScenario.Rows[0].CostOverrides.TryGetValue(loadedCostItemId, out var overrideVal));
            Assert.AreEqual(0.15m, overrideVal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public void Save_KeepsOnlyMostRecentFive()
    {
        var path = TempFilePath();
        try
        {
            var service = new MarginCalculatorScenarioService(path);
            for (var i = 0; i < 7; i++)
            {
                service.Save(new MarginCalcScenario { Label = $"저장{i}" });
            }

            var loaded = service.LoadAll();

            Assert.AreEqual(5, loaded.Count);
            Assert.AreEqual("저장6", loaded[0].Label); // 최신이 맨 앞
            Assert.AreEqual("저장2", loaded[4].Label); // 가장 오래된 것부터 잘림
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

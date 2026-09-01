using System.Text.Json;
using MiniERP2.Models;

namespace MiniERP2.Config;

/// <summary>정산 마진 계산기 임시저장 CRUD. 최근 5개까지만 보관 — 6번째 저장 시 가장 오래된
/// 항목을 자동으로 버린다. 별도 DB 테이블 없이 설정 파일과 같은 방식(JSON 파일)으로 둔다.</summary>
public class SimpleMarginCalculatorScenarioService
{
    private const int MaxScenarios = 5;
    private readonly string _filePath;

    public SimpleMarginCalculatorScenarioService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(PathProvider.AppDataFolder, "simple_margin_calculator_scenarios.json");
    }

    /// <summary>최신순(저장 시 맨 앞에 추가)으로 반환한다.</summary>
    public List<SimpleMarginCalcScenario> LoadAll()
    {
        if (!File.Exists(_filePath)) return new List<SimpleMarginCalcScenario>();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<SimpleMarginCalcScenario>>(json) ?? new List<SimpleMarginCalcScenario>();
        }
        catch
        {
            return new List<SimpleMarginCalcScenario>();
        }
    }

    /// <summary>맨 앞(최신)에 추가하고, 5개를 넘으면 가장 오래된 것부터 버린다.</summary>
    public List<SimpleMarginCalcScenario> Save(SimpleMarginCalcScenario scenario)
    {
        var all = LoadAll();
        all.Insert(0, scenario);
        if (all.Count > MaxScenarios) all.RemoveRange(MaxScenarios, all.Count - MaxScenarios);
        Persist(all);
        return all;
    }

    public void Delete(Guid id)
    {
        var all = LoadAll();
        all.RemoveAll(s => s.Id == id);
        Persist(all);
    }

    private void Persist(List<SimpleMarginCalcScenario> all)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(_filePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }
}

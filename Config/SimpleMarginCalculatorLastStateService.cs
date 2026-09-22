using System.Text.Json;
using MiniERP2.Models;

namespace MiniERP2.Config;

/// <summary>정산 마진 계산기의 마지막 화면 상태 저장/복원 — 사용자가 "임시저장" 버튼을 누르지
/// 않았더라도, 창을 닫을 때마다 자동으로 남겨 다음에 열 때 그대로 이어서 보여준다. 이름 붙여
/// 여러 개 보관하는 임시저장 목록(<see cref="SimpleMarginCalculatorScenarioService"/>)과는 별개의
/// 단일 슬롯이다.</summary>
public class SimpleMarginCalculatorLastStateService
{
    private readonly string _filePath;

    public SimpleMarginCalculatorLastStateService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(PathProvider.AppDataFolder, "simple_margin_calculator_last_state.json");
    }

    public List<SimpleMarginCalcRow> Load()
    {
        if (!File.Exists(_filePath)) return new List<SimpleMarginCalcRow>();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<SimpleMarginCalcRow>>(json) ?? new List<SimpleMarginCalcRow>();
        }
        catch
        {
            return new List<SimpleMarginCalcRow>();
        }
    }

    public void Save(List<SimpleMarginCalcRow> rows)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(_filePath, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }
}

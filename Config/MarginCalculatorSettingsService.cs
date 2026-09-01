using System.Text.Json;
using MiniERP2.Models;

namespace MiniERP2.Config;

public class MarginCalculatorSettings
{
    /// <summary>§8: 비용 항목 정의(전역 패널)를 다음 실행 시 복원한다.</summary>
    public List<MarginCostItemDef> CostItems { get; set; } = new();

    public bool VatExcluded { get; set; }
    public int RoundingUnitIndex { get; set; } = 1;
    public bool QtyColumnVisible { get; set; } = true;
}

public class MarginCalculatorSettingsService
{
    private readonly string _filePath;

    public MarginCalculatorSettingsService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(PathProvider.AppDataFolder, "margin_calculator_settings.json");
    }

    public MarginCalculatorSettings Load()
    {
        if (!File.Exists(_filePath)) return new MarginCalculatorSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<MarginCalculatorSettings>(json) ?? new MarginCalculatorSettings();
        }
        catch
        {
            return new MarginCalculatorSettings();
        }
    }

    public void Save(MarginCalculatorSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

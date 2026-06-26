using System.Text.Json;
using MiniERP2.Models;

namespace MiniERP2.Config;

public class GridSettingsService
{
    private readonly string _settingsFilePath;
    private Dictionary<string, List<GridColumnLayout>> _allLayouts;

    public GridSettingsService()
    {
        _settingsFilePath = Path.Combine(PathProvider.AppDataFolder, "grid_layouts.json");
        _allLayouts = LoadAllLayoutsFromFile();
    }

    public void SaveLayout(string key, List<GridColumnLayout> layout)
    {
        _allLayouts[key] = layout;
        SaveAllLayoutsToFile();
    }

    public List<GridColumnLayout>? LoadLayout(string key)
    {
        return _allLayouts.TryGetValue(key, out var layout) ? layout : null;
    }

    private Dictionary<string, List<GridColumnLayout>> LoadAllLayoutsFromFile()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new Dictionary<string, List<GridColumnLayout>>();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, List<GridColumnLayout>>>(json) ?? new();
        }
        catch
        {
            return new Dictionary<string, List<GridColumnLayout>>();
        }
    }

    private void SaveAllLayoutsToFile()
    {
        var json = JsonSerializer.Serialize(_allLayouts, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsFilePath, json);
    }
}
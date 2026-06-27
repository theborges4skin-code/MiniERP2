using System.Text.Json;

namespace MiniERP2.Config;

/// <summary>
/// SplitContainer의 분할 위치(SplitterDistance)를 키별로 저장/조회합니다.
/// Controls/PersistentSplitContainer.cs가 사용합니다.
/// </summary>
public class SplitterSettingsService
{
    private readonly string _settingsFilePath;
    private readonly Dictionary<string, int> _allDistances;

    public SplitterSettingsService()
    {
        _settingsFilePath = Path.Combine(PathProvider.AppDataFolder, "splitter_layouts.json");
        _allDistances = Load();
    }

    public void SaveDistance(string key, int distance)
    {
        _allDistances[key] = distance;
        Save();
    }

    public int? LoadDistance(string key) => _allDistances.TryGetValue(key, out var distance) ? distance : null;

    private Dictionary<string, int> Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new Dictionary<string, int>();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new();
        }
        catch
        {
            return new Dictionary<string, int>();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_allDistances, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsFilePath, json);
    }
}

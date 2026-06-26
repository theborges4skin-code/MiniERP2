using System.Text.Json;

namespace MiniERP2.Config;

public class SettingsService
{
    private readonly string _filePath;
    private Dictionary<string, string> _lastFolders;

    public SettingsService(string? filePath = null)
    {
        _filePath = filePath ?? PathProvider.SettingsFilePath;
        _lastFolders = Load();
    }

    public string? GetLastFolder(string key) => _lastFolders.GetValueOrDefault(key);

    public void SetLastFolder(string key, string path)
    {
        _lastFolders[key] = path;
        Save();
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, string>();
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(_lastFolders));
    }
}

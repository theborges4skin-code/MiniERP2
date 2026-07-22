using System.Text.Json;
using MiniERP2.Models;

namespace MiniERP2.Config;

public class AutoOrderSettingsService
{
    private readonly string _filePath;

    public AutoOrderSettingsService(string? filePath = null)
    {
        _filePath = filePath ?? PathProvider.AutoOrderSettingsFilePath;
    }

    public AutoOrderSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AutoOrderSettings();
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<AutoOrderSettings>(json) ?? new AutoOrderSettings();
    }

    public void Save(AutoOrderSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}

using System.Text.Json;
using MiniERP2.Models;

namespace MiniERP2.Config;

public class ChannelConfigService
{
    private readonly string _filePath;

    public ChannelConfigService(string? filePath = null)
    {
        _filePath = filePath ?? PathProvider.ChannelConfigFilePath;
    }

    public List<ChannelConfig> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new List<ChannelConfig>();
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<List<ChannelConfig>>(json) ?? new List<ChannelConfig>();
    }

    public void Save(List<ChannelConfig> channelConfigs)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(channelConfigs, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}

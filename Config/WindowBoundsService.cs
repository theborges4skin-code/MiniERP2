using System.Text.Json;
using MiniERP2.Models;

namespace MiniERP2.Config;

/// <summary>
/// 화면(Form) 타입별 마지막 크기/위치/상태를 JSON 파일로 저장하고 불러옵니다(기획서 2.6절).
/// </summary>
public class WindowBoundsService
{
    private readonly string _filePath;
    private readonly Dictionary<string, WindowBounds> _bounds;

    public WindowBoundsService(string? filePath = null)
    {
        _filePath = filePath ?? PathProvider.WindowBoundsFilePath;
        _bounds = Load();
    }

    public WindowBounds? Get(string formTypeName) => _bounds.GetValueOrDefault(formTypeName);

    public void Save(string formTypeName, WindowBounds bounds)
    {
        _bounds[formTypeName] = bounds;
        Persist();
    }

    private Dictionary<string, WindowBounds> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, WindowBounds>();
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<Dictionary<string, WindowBounds>>(json) ?? new Dictionary<string, WindowBounds>();
    }

    private void Persist()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(_bounds));
    }
}

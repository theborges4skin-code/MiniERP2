using System.Text.Json;
using MiniERP2.Models;

namespace MiniERP2.Config;

/// <summary>
/// 메인 허브 메뉴(상단 드롭다운 + 중앙 버튼 + 검색창에서 여는 최상위 화면)를 레이블 기준으로
/// 얼마나 자주 열었는지 JSON 파일로 누적 기록한다. WindowBoundsService와 같은 형식(타입/레이블별
/// 키-값을 JSON 파일에 저장)이며, "자주 쓰는 기능을 즐겨찾기로 강조"하는 기능의 1단계(데이터 수집)다.
/// </summary>
public class MenuUsageLogService
{
    private readonly string _filePath;
    private readonly Dictionary<string, MenuUsageStat> _stats;

    public MenuUsageLogService(string? filePath = null)
    {
        _filePath = filePath ?? PathProvider.MenuUsageLogFilePath;
        _stats = Load();
    }

    public void RecordUse(string label)
    {
        if (_stats.TryGetValue(label, out var stat))
        {
            stat.Count++;
            stat.LastUsedAt = DateTime.Now;
        }
        else
        {
            _stats[label] = new MenuUsageStat { Count = 1, LastUsedAt = DateTime.Now };
        }
        Persist();
    }

    public IReadOnlyDictionary<string, MenuUsageStat> GetAll() => _stats;

    private Dictionary<string, MenuUsageStat> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, MenuUsageStat>();
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<Dictionary<string, MenuUsageStat>>(json) ?? new Dictionary<string, MenuUsageStat>();
    }

    private void Persist()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(_stats));
    }
}

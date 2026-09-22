using System.Text.Json;

namespace MiniERP2.Config;

/// <summary>채널 선택 다이얼로그의 마지막 화면 위치(펼쳐둔 그룹 폴더 + 마지막으로 고른 채널)를
/// 저장/복원합니다. 채널이 많아 매번 같은 폴더를 다시 펼치고 스크롤해야 하는 번거로움을 없애기
/// 위한 것으로, 창(정산/출고이력/OFS 등)과 무관한 단일 슬롯을 공유합니다.</summary>
public class SelectChannelDialogStateService
{
    private readonly string _filePath;

    public SelectChannelDialogStateService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(PathProvider.AppDataFolder, "select_channel_dialog_state.json");
    }

    public SelectChannelDialogState Load()
    {
        if (!File.Exists(_filePath)) return new SelectChannelDialogState();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<SelectChannelDialogState>(json) ?? new SelectChannelDialogState();
        }
        catch
        {
            // 상태 파일이 깨져도 채널 선택 자체는 막히면 안 되므로 기본 상태로 되돌린다.
            return new SelectChannelDialogState();
        }
    }

    public void Save(SelectChannelDialogState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(_filePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 저장 실패는 편의 기능 손실일 뿐이므로 무시한다.
        }
    }
}

/// <summary>채널 선택 다이얼로그가 기억하는 마지막 위치.</summary>
public class SelectChannelDialogState
{
    /// <summary>펼쳐져 있던 그룹 노드 키 목록(즐겨찾기는 "__FAV__", 미분류는 "(미분류)").</summary>
    public List<string> ExpandedGroups { get; set; } = new();

    /// <summary>마지막으로 선택된 채널 코드. 다음에 열 때 이 채널로 스크롤·선택한다.</summary>
    public string? LastChannelCode { get; set; }

    /// <summary>한 번이라도 저장된 상태인지. false면 기본값(즐겨찾기 펼침)을 사용한다.</summary>
    public bool HasState { get; set; }
}

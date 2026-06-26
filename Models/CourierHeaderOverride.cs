namespace MiniERP2.Models;

/// <summary>
/// 채널별로, 특정 택배사 출력 양식의 한 헤더에 항상 고정된 텍스트를 출력하도록 지정합니다.
/// 같은 택배사를 여러 채널이 공유하더라도 채널마다 다른 고정값(예: 도착지 코드)을 쓸 수 있습니다.
/// </summary>
public class CourierHeaderOverride
{
    public string CourierName { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public string FixedValue { get; set; } = string.Empty;
}

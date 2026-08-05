namespace MiniERP2.Models;

/// <summary>
/// 아마존 FBA 발주지(수취지) 설정. 수취지가 1곳으로 고정이라 FBO의 채널별 다건 테이블과 달리
/// ConfigKey="DEFAULT" 단일 행으로 관리한다. FbaOrder 저장 시점에 이 값들을 스냅샷으로 복사한다.
/// </summary>
public class FbaConfigModel
{
    public const string DefaultConfigKey = "DEFAULT";

    public string ConfigKey { get; set; } = DefaultConfigKey;
    public string ReceiverName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Phone2 { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DeliveryMessage { get; set; } = string.Empty;
    /// <summary>운임 계산용 구분이며 FBA 박스규격명과 무관 — 기본값 "중" 고정.</summary>
    public string BoxTypeLabel { get; set; } = "중";
    public string TransferType { get; set; } = string.Empty;
    public string Etc1 { get; set; } = string.Empty;
    public string OrderNoPrefix { get; set; } = "#FBA";
}

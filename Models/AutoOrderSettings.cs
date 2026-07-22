namespace MiniERP2.Models;

/// <summary>
/// 자동발주처리(Gmail 자동화) 연동 로컬 설정. Drive 접근에 필요한 OAuth 클라이언트 정보와
/// pending 폴더 위치, 폴링 정책을 담는다(02_자동발주처리_MiniERP2연동_설계.md §2, §7).
/// client_id/secret은 데스크톱(설치형) 앱용이라 기밀로 취급되지 않는다(Google의 설치형 앱
/// OAuth 모델 — RFC 8252) — 그래서 다른 설정과 같은 평문 JSON에 둔다. 실제로 유출 시 위험한
/// 것은 refresh token쪽이라 그건 별도로 DPAPI 암호화해 저장한다(GoogleDriveTokenStore 참고).
/// </summary>
public class AutoOrderSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Drive의 /자동발주처리/pending 폴더 ID(가이드 03 Part 3-2).</summary>
    public string PendingFolderId { get; set; } = string.Empty;

    public int PollingIntervalMinutes { get; set; } = 30;

    public bool PollOnStartup { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(PendingFolderId);
}

using MiniERP2.Models;

namespace MiniERP2.Services;

/// <summary>
/// 자동발주처리(Gmail 자동화) Drive 연동의 읽기전용 접근 인터페이스. 실제 구현(GoogleDriveAutoOrderClient)과
/// 분리해두어야 폴링 로직(AutoOrderPollingService)을 네트워크 없이 단위테스트할 수 있다.
/// </summary>
public interface IAutoOrderDriveClient
{
    /// <summary>이미 로그인되어 캐시된 토큰이 있는지(브라우저를 새로 띄우지 않고) 저비용으로 확인한다.</summary>
    bool HasCachedAuthorization();

    /// <summary>
    /// 캐시된 토큰이 있으면 조용히 갱신하고, 없으면 브라우저를 열어 사용자 로그인을 요청한다
    /// (Google OAuth 데스크톱 앱 흐름 — 로컬 루프백으로 인가 코드 수신).
    /// </summary>
    Task AuthorizeAsync(CancellationToken cancellationToken = default);

    /// <summary>/pending/manifest.json을 읽어온다. 설정 미완료·인증 안 됨·파일 없음이면 null.</summary>
    Task<AutoOrderManifest?> FetchManifestAsync(CancellationToken cancellationToken = default);

    /// <summary>/pending/{fileName} 파일의 바이트를 다운로드한다. 찾지 못하면 null.</summary>
    Task<byte[]?> DownloadFileAsync(string fileName, CancellationToken cancellationToken = default);
}

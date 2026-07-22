using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using MiniERP2.Config;
using MiniERP2.Models;

namespace MiniERP2.Services;

/// <summary>
/// Drive의 /자동발주처리/pending 폴더를 drive.readonly 스코프로만 읽는 클라이언트
/// (02_자동발주처리_MiniERP2연동_설계.md §2). manifest.json 조회 + 개별 xlsx 다운로드 외의
/// 접근은 하지 않는다.
/// </summary>
public class GoogleDriveAutoOrderClient : IAutoOrderDriveClient
{
    private const string UserId = "user";
    private static readonly string[] Scopes = [DriveService.Scope.DriveReadonly];

    private readonly AutoOrderSettingsService _settingsService;
    private readonly GoogleDriveTokenStore _tokenStore;
    private DriveService? _driveService;

    public GoogleDriveAutoOrderClient(AutoOrderSettingsService? settingsService = null, string? tokenFolder = null)
    {
        _settingsService = settingsService ?? new AutoOrderSettingsService();
        _tokenStore = new GoogleDriveTokenStore(tokenFolder ?? PathProvider.AutoOrderTokenFolderPath);
    }

    public bool HasCachedAuthorization() => _tokenStore.HasCachedToken(UserId);

    public async Task AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("자동발주처리 연동 설정(OAuth 클라이언트 ID/보안 비밀, pending 폴더 ID)이 아직 입력되지 않았습니다.");
        }

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets { ClientId = settings.ClientId, ClientSecret = settings.ClientSecret },
            Scopes,
            UserId,
            cancellationToken,
            _tokenStore);

        _driveService = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "MiniERP2",
        });
    }

    public async Task<AutoOrderManifest?> FetchManifestAsync(CancellationToken cancellationToken = default)
    {
        var bytes = await DownloadFileAsync("manifest.json", cancellationToken);
        if (bytes == null) return null;

        return JsonSerializer.Deserialize<AutoOrderManifest>(bytes);
    }

    public async Task<byte[]?> DownloadFileAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Load();
        if (!settings.IsConfigured) return null;

        await EnsureDriveServiceAsync(cancellationToken);
        if (_driveService == null) return null;

        var listRequest = _driveService.Files.List();
        listRequest.Q = $"'{settings.PendingFolderId}' in parents and name = '{EscapeForQuery(fileName)}' and trashed = false";
        listRequest.Fields = "files(id, name)";
        listRequest.Spaces = "drive";

        var listResult = await listRequest.ExecuteAsync(cancellationToken);
        var file = listResult.Files?.FirstOrDefault();
        if (file == null) return null;

        using var stream = new MemoryStream();
        var downloadRequest = _driveService.Files.Get(file.Id);
        await downloadRequest.DownloadAsync(stream, cancellationToken);
        return stream.ToArray();
    }

    private async Task EnsureDriveServiceAsync(CancellationToken cancellationToken)
    {
        if (_driveService != null) return;
        await AuthorizeAsync(cancellationToken);
    }

    /// <summary>Drive 쿼리 문자열의 작은따옴표는 이스케이프해야 한다(파일명에 포함될 가능성 대비).</summary>
    private static string EscapeForQuery(string value) => value.Replace("'", "\\'");
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Util.Store;

namespace MiniERP2.Services;

/// <summary>
/// Google OAuth refresh token을 DPAPI(현재 Windows 사용자 전용)로 암호화해 로컬 폴더에 저장하는
/// IDataStore 구현. 기본 제공되는 FileDataStore는 평문 저장이라, 02_자동발주처리_MiniERP2연동_
/// 설계.md §2의 "refresh token은 DPAPI 등으로 보호 권장"을 satisfy하기 위해 직접 구현했다.
/// </summary>
public class GoogleDriveTokenStore : IDataStore
{
    private readonly string _folder;

    public GoogleDriveTokenStore(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(_folder);
    }

    /// <summary>브라우저 인터랙티브 인증을 새로 띄우지 않고도 "이미 로그인된 상태인지"만 저비용으로 확인한다.</summary>
    public bool HasCachedToken(string userId) => File.Exists(GetFilePath(userId));

    public Task StoreAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(GetFilePath(key), protectedBytes);
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        var path = GetFilePath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var path = GetFilePath(key);
        if (!File.Exists(path)) return Task.FromResult<T>(default!);

        var protectedBytes = File.ReadAllBytes(path);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var json = Encoding.UTF8.GetString(plainBytes);
        return Task.FromResult(JsonSerializer.Deserialize<T>(json)!);
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(_folder))
        {
            foreach (var file in Directory.GetFiles(_folder)) File.Delete(file);
        }
        return Task.CompletedTask;
    }

    private string GetFilePath(string key) => Path.Combine(_folder, SanitizeKey(key) + ".dat");

    private static string SanitizeKey(string key)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
        return key;
    }
}

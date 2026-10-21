using System.Text.Json;

namespace MiniERP2.MFG.Config;

/// <summary>MFG 설정(settings.json). 값이 없으면 기본값을 쓴다.</summary>
public class MfgSettings
{
    /// <summary>MiniERP2 DB 경로. 비어 있으면 <see cref="MfgPaths.DefaultMiniErp2DatabasePath"/>.</summary>
    public string MiniErp2DatabasePath { get; set; } = string.Empty;

    /// <summary>마지막으로 쓴 VAT 보기(true=VAT포함). 다음 실행 때 그대로 연다.</summary>
    public bool ShowVatIncluded { get; set; } = true;

    /// <summary>초기 이관용 엑셀 경로 기억(신제조법 / product_master).</summary>
    public string LastRecipeWorkbookPath { get; set; } = @"C:\00_CompanyWorks\회사폴더\제조법관련(신규).xlsx";

    public string LastProductMasterPath { get; set; } =
        @"C:\00_CompanyWorks\회사폴더\02_견적,거래명세_보냄\온라인 매출내역\product_master.xlsx";

    public string ResolvedMiniErp2DatabasePath =>
        string.IsNullOrWhiteSpace(MiniErp2DatabasePath) ? MfgPaths.DefaultMiniErp2DatabasePath : MiniErp2DatabasePath;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static MfgSettings Load()
    {
        try
        {
            if (File.Exists(MfgPaths.SettingsFilePath))
            {
                return JsonSerializer.Deserialize<MfgSettings>(File.ReadAllText(MfgPaths.SettingsFilePath)) ?? new MfgSettings();
            }
        }
        catch (JsonException)
        {
            // 깨진 설정 파일은 기본값으로 시작한다(설정은 언제든 다시 지정 가능).
        }
        return new MfgSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(MfgPaths.AppDataFolder);
        File.WriteAllText(MfgPaths.SettingsFilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}

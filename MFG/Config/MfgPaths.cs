namespace MiniERP2.MFG.Config;

/// <summary>
/// MFG의 실사용 데이터 위치. MiniERP2(PathProvider)와 같은 이유로 빌드 산출물(bin) 밖의
/// %AppData%를 쓴다 — dotnet clean 등이 DB·백업을 지워버리는 사고(MiniERP2 2026-09-04)를 막기 위함.
/// 테스트는 <see cref="AppDataFolder"/>를 임시 폴더로 바꿔 쓴다.
/// </summary>
public static class MfgPaths
{
    public static string AppDataFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniERP2_MFG");

    /// <summary>mfgdb — MFG 전용 DB. MiniERP2 DB와는 별개 파일이다.</summary>
    public static string DatabaseFilePath => Path.Combine(AppDataFolder, "mfgdb.sqlite");

    public static string SettingsFilePath => Path.Combine(AppDataFolder, "settings.json");

    public static string BackupsFolder => Path.Combine(AppDataFolder, "backups");

    public static string DiagnosticsLogFilePath => Path.Combine(AppDataFolder, "diagnostics.log");

    /// <summary>MiniERP2 실사용 DB 기본 위치(MiniERP2 PathProvider.DatabaseFilePath와 같은 규칙).</summary>
    public static string DefaultMiniErp2DatabasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniERP2", "ERP_Database.sqlite");
}

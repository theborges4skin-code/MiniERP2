namespace MiniERP2.Config;

public static class PathProvider
{
    public static string AppDataFolder { get; set; } = AppContext.BaseDirectory;

    public static string DatabaseFilePath => Path.Combine(AppDataFolder, "ERP_Database.sqlite");

    public static string SettingsFilePath => Path.Combine(AppDataFolder, "settings.json");

    public static string ChannelConfigFilePath => Path.Combine(AppDataFolder, "channels_config.json");

    public static string ExportSummaryConfigFilePath => Path.Combine(AppDataFolder, "export_summary_config.json");

    public static string WindowBoundsFilePath => Path.Combine(AppDataFolder, "window_bounds.json");

    /// <summary>진단용 로그 파일(정산파일 로드가 멈춘 듯 보일 때, 어느 단계에서 멈췄는지 추적하기 위함).</summary>
    public static string DiagnosticsLogFilePath => Path.Combine(AppDataFolder, "diagnostics.log");

    /// <summary>자동발주처리(Gmail 자동화) 연동 설정(OAuth client_id/secret, Drive 폴더 ID, 폴링 간격 등).</summary>
    public static string AutoOrderSettingsFilePath => Path.Combine(AppDataFolder, "autoorder_settings.json");

    /// <summary>
    /// 자동발주처리 Drive OAuth refresh token 저장 폴더. DPAPI(현재 사용자 전용)로 암호화해 저장한다
    /// (02_자동발주처리_MiniERP2연동_설계.md §2 — "DPAPI 등으로 보호 권장").
    /// </summary>
    public static string AutoOrderTokenFolderPath => Path.Combine(AppDataFolder, "autoorder_token");
}

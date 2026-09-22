namespace MiniERP2.Config;

public static class PathProvider
{
    // bin/obj(빌드 산출물) 밖의 고정 위치. 예전엔 AppContext.BaseDirectory(실행 파일이 있는
    // bin\Debug\... 폴더)를 썼는데, dotnet clean이나 "obj/bin 지우고 다시 빌드" 같은 흔한
    // 빌드 캐시 정리 작업이 DB·설정·백업까지 통째로 지워버리는 사고(2026-09-04)가 있었다.
    // %AppData%는 빌드와 무관하게 항상 살아있으므로 실사용 데이터는 여기 둔다.
    public static string AppDataFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniERP2");

    public static string DatabaseFilePath => Path.Combine(AppDataFolder, "ERP_Database.sqlite");

    public static string SettingsFilePath => Path.Combine(AppDataFolder, "settings.json");

    public static string ChannelConfigFilePath => Path.Combine(AppDataFolder, "channels_config.json");

    public static string ExportSummaryConfigFilePath => Path.Combine(AppDataFolder, "export_summary_config.json");

    public static string WindowBoundsFilePath => Path.Combine(AppDataFolder, "window_bounds.json");

    /// <summary>메인 허브 메뉴/버튼별 누적 사용 횟수·마지막 사용 시각(자주 쓰는 기능 파악용).</summary>
    public static string MenuUsageLogFilePath => Path.Combine(AppDataFolder, "menu_usage.json");

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

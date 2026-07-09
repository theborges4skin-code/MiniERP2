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
}

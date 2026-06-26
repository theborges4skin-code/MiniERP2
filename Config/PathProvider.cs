namespace MiniERP2.Config;

public static class PathProvider
{
    public static string AppDataFolder { get; set; } = AppContext.BaseDirectory;

    public static string DatabaseFilePath => Path.Combine(AppDataFolder, "ERP_Database.sqlite");

    public static string SettingsFilePath => Path.Combine(AppDataFolder, "settings.json");

    public static string ChannelConfigFilePath => Path.Combine(AppDataFolder, "channels_config.json");
}

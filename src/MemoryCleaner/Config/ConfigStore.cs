using System.Text.Json;

namespace MemoryCleaner.Config;

/// <summary>
/// 配置读写，存于 %AppData%/MemoryCleaner/config.json。
/// </summary>
internal static class ConfigStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MemoryCleaner");
    private static readonly string Path_ = Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(Path_))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path_), Opts);
                if (cfg != null)
                {
                    cfg.Sanitize(); // 钳制手改配置中的非法值
                    return cfg;
                }
            }
        }
        catch
        {
            // 配置损坏时回退默认
        }
        return new AppConfig();
    }

    public static void Save(AppConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path_, JsonSerializer.Serialize(cfg, Opts));
        }
        catch
        {
            // 写失败不致命
        }
    }
}

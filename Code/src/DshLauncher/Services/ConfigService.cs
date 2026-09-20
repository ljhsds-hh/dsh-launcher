using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DshLauncher.Models;

namespace DshLauncher.Services;

/// <summary>
/// 负责 <c>%AppData%\DshLauncher\config.json</c> 的读写。
/// 任何异常都退化为默认配置，绝不因为配置问题挡住主流程。
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <param name="directoryPath">
    /// 配置目录；传 null 时使用 <c>%AppData%\DshLauncher</c>（测试可指向临时目录）。
    /// </param>
    public ConfigService(string? directoryPath = null)
    {
        DirectoryPath = string.IsNullOrWhiteSpace(directoryPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DshLauncher")
            : directoryPath;
        FilePath = Path.Combine(DirectoryPath, "config.json");
    }

    public string DirectoryPath { get; }

    public string FilePath { get; }

    public AppConfig Load()
    {
        AppConfig? config = null;

        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath, Encoding.UTF8);
                config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
            }
        }
        catch (Exception)
        {
            // 配置文件损坏时直接回退到默认值。
            config = null;
        }

        config ??= new AppConfig();
        Normalize(config);
        return config;
    }

    public void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var json = JsonSerializer.Serialize(config, SerializerOptions);
            File.WriteAllText(FilePath, json, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // 写入失败不应中断用户操作。
        }
    }

    /// <summary>
    /// 配置文件不存在时，先把当前（已归一化的）配置落盘一份。
    /// </summary>
    /// <remarks>
    /// 配置原本是**懒创建**的：只有「保存设置」或关窗时才写。于是第一次用的人点界面上那个
    /// 「配置文件」，打开的是一个只有 logs 子目录的空文件夹，看着就像这工具没生成配置。
    /// 启动时先落一份，那个按钮就永远有东西可看。
    /// </remarks>
    /// <returns>这次真的新建了文件才返回 <c>true</c>。</returns>
    public bool SaveIfMissing(AppConfig config)
    {
        if (File.Exists(FilePath)) return false;

        Save(config);
        return File.Exists(FilePath);
    }

    /// <summary>把配置归一化到合法范围，保证配置文件被改坏也能启动。</summary>
    public static void Normalize(AppConfig config)
    {
        config.Steps = CommandCatalog.MergeWithDefaults(config.Steps);

        if (config.RecentWorkspaces is null) config.RecentWorkspaces = new List<string>();
        if (config.MaxLogLines < 200) config.MaxLogLines = 5000;
        if (config.ServerPort is <= 0 or > 65535) config.ServerPort = 3080;
        if (config.WindowWidth < 800) config.WindowWidth = 1120;
        if (config.WindowHeight < 520) config.WindowHeight = 760;
        if (string.IsNullOrWhiteSpace(config.ReadyPattern))
        {
            config.ReadyPattern = @"https?://(?:127\.0\.0\.1|localhost):\d+";
        }
    }
}

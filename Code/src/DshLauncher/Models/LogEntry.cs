namespace DshLauncher.Models;

/// <summary>日志级别，用于着色与过滤。</summary>
public enum LogLevel
{
    /// <summary>普通输出。</summary>
    Info,

    /// <summary>警告。</summary>
    Warn,

    /// <summary>错误或失败输出。</summary>
    Error,

    /// <summary>启动器自身产生的提示。</summary>
    System,
}

/// <summary>一行日志。</summary>
public sealed class LogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;

    /// <summary>产生日志的步骤标识，启动器自身使用 <c>launcher</c>。</summary>
    public string Source { get; init; } = "launcher";

    public LogLevel Level { get; init; } = LogLevel.Info;

    public string Message { get; init; } = string.Empty;

    public string TimeText => Time.ToString("HH:mm:ss");

    public string SourceText => $"[{Source}]";

    public string LevelText => Level switch
    {
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.System => "SYS",
        _ => "INFO",
    };
}

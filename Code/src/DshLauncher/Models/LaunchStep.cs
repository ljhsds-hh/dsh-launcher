namespace DshLauncher.Models;

/// <summary>
/// 一个可执行的启动步骤（清理 / 构建 / 启动）。
/// 命令行保存在配置文件中，可随时修改而无需重新编译。
/// </summary>
public sealed class LaunchStep
{
    /// <summary>步骤标识，同时用作日志来源标签。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>展示名称，例如「清理」。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>在工作目录下通过 cmd.exe 执行的命令。</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// 是否为常驻进程（例如 <c>pnpm dsh web</c> 启动的 Web 服务）。
    /// 常驻步骤在用户点击「停止」之前会一直保持运行。
    /// </summary>
    public bool LongRunning { get; set; }

    public LaunchStep Clone() => new()
    {
        Id = Id,
        Name = Name,
        Command = Command,
        LongRunning = LongRunning,
    };
}

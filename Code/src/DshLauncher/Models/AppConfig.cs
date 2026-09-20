namespace DshLauncher.Models;

/// <summary>
/// 持久化到 <c>%AppData%\DshLauncher\config.json</c> 的用户配置。
/// </summary>
public sealed class AppConfig
{
    /// <summary>最近一次使用的 DeepSeek Harness 仓库目录。</summary>
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>最近使用过的目录，最多保留 8 个。</summary>
    public List<string> RecentWorkspaces { get; set; } = new();

    /// <summary>三个固定步骤的命令行，默认值见 <c>CommandCatalog</c>。</summary>
    public List<LaunchStep> Steps { get; set; } = new();

    /// <summary>Web 服务端口，用于占用检测与「打开浏览器」。</summary>
    public int ServerPort { get; set; } = 3080;

    /// <summary>
    /// 服务就绪后是否由启动器打开浏览器。
    /// 默认关闭：<c>dsh web</c> 自身会打开浏览器，再开一次会出现两个标签页。
    /// </summary>
    public bool OpenBrowserWhenReady { get; set; }

    /// <summary>判定「服务已就绪」的正则，匹配到即认为启动成功。</summary>
    public string ReadyPattern { get; set; } = @"https?://(?:127\.0\.0\.1|localhost):\d+";

    /// <summary>日志区保留的最大行数，超出后从最旧的开始丢弃。</summary>
    public int MaxLogLines { get; set; } = 5000;

    /// <summary>日志区是否自动滚动到最新一行。</summary>
    public bool AutoScroll { get; set; } = true;

    /// <summary>
    /// 端口已被占用时是否仍允许执行「启动」。
    /// 默认关闭：端口被占通常说明已经有一个 DSH 在跑，再起一个实例会把两边都搞乱
    /// （Windows 的 SO_REUSEADDR 允许两个实例同时绑同一端口）。
    /// </summary>
    public bool AllowStartWhenPortBusy { get; set; }

    /// <summary>
    /// 关闭启动器时是否让已启动的 DSH 服务继续运行。
    /// 默认开启（用户要求：关掉启动器不要把服务一起停掉）。
    /// 关掉它则恢复「关窗顺手收干净、不留孤儿」的老行为。
    /// </summary>
    public bool KeepServiceRunningOnExit { get; set; } = true;

    public double WindowWidth { get; set; } = 1120;

    public double WindowHeight { get; set; } = 760;

    /// <summary>是否每次执行前自动在日志中打印环境自检信息。</summary>
    public bool PrintEnvironmentOnStart { get; set; } = true;

    /// <summary>
    /// 是否把日志同步写到 <c>%AppData%\DshLauncher\logs</c>。
    /// 一次运行一个文件（文件名带启动时间），便于事后完整复盘。
    /// </summary>
    public bool WriteLogFile { get; set; } = true;
}

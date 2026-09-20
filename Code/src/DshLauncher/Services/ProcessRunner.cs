using System.Diagnostics;
using System.IO;
using System.Text;
using DshLauncher.Models;

namespace DshLauncher.Services;

/// <summary>
/// 在工作目录下通过 <c>cmd.exe</c> 执行一条命令，并把标准输出 / 标准错误逐行回传。
/// </summary>
/// <remarks>
/// 之所以统一走 cmd.exe，而不是直接启动 <c>pnpm</c>：
/// <list type="bullet">
///   <item>直接启动 "pnpm" 时 CreateProcess 不会按 PATHEXT 展开到 pnpm.cmd；</item>
///   <item>PowerShell 里的 pnpm.ps1 会被执行策略拦截
///   （<c>无法加载文件 pnpm.ps1，因为在此系统上禁止运行脚本</c>）；</item>
///   <item>走 cmd.exe 得到的解析结果和用户手敲命令完全一致。</item>
/// </list>
/// 输出统一按 UTF-8 解码：pnpm / node / tsx 都是按 UTF-8 写管道的，
/// 否则中文日志会乱码。
/// </remarks>
public sealed class ProcessRunner : IDisposable
{
    private static readonly string ShellPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private Process? _process;
    private TaskCompletionSource<int>? _completion;
    private ProcessJob? _job;
    private string _source = "launcher";
    private bool _disposed;

    /// <summary>本次退出是否为「用户点了停止」引起，用于把退出日志判为正常收尾而不是失败。</summary>
    private volatile bool _stopRequested;

    /// <summary>当前跑的是不是常驻服务（启动步骤）。</summary>
    private volatile bool _currentStepIsService;

    /// <summary>
    /// 关闭启动器时是否保留常驻服务。由主视图模型从配置里同步过来。
    /// 只对常驻服务生效；清理 / 构建这类短步骤一律照旧收掉，免得关窗留下半拉子构建。
    /// </summary>
    /// <remarks>
    /// 默认 **false**（关窗即收）。这不是配置默认值——配置默认是 true（保留服务），
    /// 由 <c>MainViewModel</c> 构造时同步过来。两边默认值故意不同是因为
    /// 「没有 ViewModel 在管」时（比如单元测试）收干净更安全。
    /// </remarks>
    public bool KeepServiceRunningOnExit { get; set; }

    private bool ShouldKeepServiceRunning() => _currentStepIsService && KeepServiceRunningOnExit;

    /// <summary>任意线程触发（stdout/stderr 的后台读取线程）。</summary>
    public event Action<LogEntry>? LineWritten;

    /// <summary>进程结束时触发，参数为退出码；后台线程触发。</summary>
    public event Action<int>? ProcessExited;

    public int? ExitCode { get; private set; }

    /// <summary>本次执行的退出码；未启动时返回 -1。</summary>
    public Task<int> Completion => _completion?.Task ?? Task.FromResult(-1);

    public bool IsRunning
    {
        get
        {
            var process = _process;
            if (process is null) return false;

            try
            {
                return !process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // 未启动 / 已释放（ObjectDisposedException 也派生自它）。
                return false;
            }
        }
    }

    public void Start(LaunchStep step, string workingDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRunning)
        {
            throw new InvalidOperationException("已有进程正在运行，请先点击「停止」。");
        }

        _source = string.IsNullOrWhiteSpace(step.Id) ? "step" : step.Id;
        _stopRequested = false;
        _currentStepIsService = step.LongRunning;
        ExitCode = null;
        _completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var startInfo = new ProcessStartInfo(ShellPath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            // /d 跳过 AutoRun，/s 让 /c 后面的字符串按字面量处理（首尾各去掉一个引号）
            Arguments = "/d /s /c \"" + step.Command + "\"",
        };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) EmitStreamLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            // 注意：stderr 不等于错误。pnpm 的脚本横幅、rolldown 的提示、
            // MCP 服务器的 ready 消息都走 stderr，判定完全交给内容。
            if (e.Data is not null) EmitStreamLine(e.Data);
        };
        process.Exited += (_, _) => OnExited(process);

        _process = process;

        // 先把 Job 建好，Start 之后立刻收编 —— Job 只收编「之后派生」的后代。
        _job?.Dispose();
        _job = ProcessJob.TryCreate(killOnClose: !ShouldKeepServiceRunning());

        process.Start();

        var inJob = _job?.TryAssign(process) ?? false;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!inJob)
        {
            Emit(
                "提示：未能把进程收进 Job Object，停止时会退回按进程树强杀（深层后代可能漏掉）。",
                LogLevel.Warn);
        }

        Emit($"$ {step.Command}", LogLevel.System);
        Emit($"  工作目录：{workingDirectory}", LogLevel.System);
    }

    /// <summary>
    /// 终止整棵进程树。
    /// 优先用 Job Object（内核级归属，几层后代都跑不掉）；Job 不可用时才退回按进程树强杀。
    /// </summary>
    public async Task StopAsync()
    {
        var process = _process;
        if (process is null) return;

        var gateway = _completion;

        // 先打标记再动手：OnExited 是异步回调，可能赶在下面 Terminate 之后就触发，
        // 标记晚了那次退出就会被当成「进程自己失败」标红。
        _stopRequested = true;

        Emit("正在停止进程树…", LogLevel.System);

        if (_job is not null)
        {
            // 首选：结束整个 Job，里面无论多少层后代一次收干净。
            _job.Terminate();
        }
        else
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Kill 失败（权限、句柄竞争等）时退回 taskkill /T。
                await KillByTaskkillAsync(process.Id).ConfigureAwait(false);
            }
        }

        if (gateway is null) return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await gateway.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 超时也必须收尾：不能让状态永远悬着（否则界面一直停在「停止中」）。
            var exited = false;
            try
            {
                exited = process.HasExited;
            }
            catch (Exception)
            {
                // 忽略。
            }

            if (gateway.TrySetResult(-1))
            {
                Emit(
                    exited
                        ? "停止等待超时，但进程已确认结束。"
                        : "停止等待超时，进程可能仍在运行，请到任务管理器确认。",
                    exited ? LogLevel.Warn : LogLevel.Error);
            }

            if (exited && ReferenceEquals(_process, process)) _process = null;
        }
    }

    private void OnExited(Process process)
    {
        try
        {
            // 只做**有界**等待：DSH 会派生 MCP 等孙进程，它们继承了 stdout/stderr 管道，
            // 无参 WaitForExit() 会一直等管道关闭，实测能把「停止」卡死 15 秒以上，
            // 还会让「进程已退出」这条日志永远不出现。
            process.WaitForExit(2000);
        }
        catch (Exception)
        {
            // 忽略。
        }

        var code = -1;
        try
        {
            code = process.ExitCode;
        }
        catch (Exception)
        {
            // 取不到退出码时保持 -1。
        }

        ExitCode = code;

        if (_stopRequested)
        {
            // 是我们按用户要求结束的。这里的退出码来自 TerminateJobObject 指定的值，
            // 不是进程自己失败 —— 早期版本一律标红 ERROR，一次正常停止会在日志里
            // 留下两行假错误（真机日志里就那两行红字）。
            Emit($"已按要求结束进程（退出码 {code}，由停止操作指定）。", LogLevel.System);
        }
        else
        {
            Emit($"进程已退出，退出码 {code}", code == 0 ? LogLevel.System : LogLevel.Error);
        }

        if (ReferenceEquals(_process, process)) _process = null;

        try
        {
            process.Dispose();
        }
        catch (Exception)
        {
            // 忽略。
        }

        _completion?.TrySetResult(code);

        try
        {
            ProcessExited?.Invoke(code);
        }
        catch (Exception)
        {
            // 订阅方异常不应影响进程清理。
        }
    }

    private static async Task KillByTaskkillAsync(int processId)
    {
        try
        {
            var startInfo = new ProcessStartInfo("taskkill")
            {
                Arguments = $"/F /T /PID {processId}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var killer = Process.Start(startInfo);
            if (killer is null) return;

            using (killer)
            {
                await killer.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // 忽略。
        }
    }

    private void EmitStreamLine(string line)
    {
        var text = AnsiStripper.Strip(line);

        // 纯控制码 / 纯空白的行是进度刷新留下的噪声，直接丢弃。
        if (string.IsNullOrWhiteSpace(text)) return;

        Emit(text, LogClassifier.Classify(text));
    }

    private void Emit(string message, LogLevel level)
    {
        var handler = LineWritten;
        if (handler is null) return;

        handler(new LogEntry
        {
            Time = DateTime.Now,
            Source = _source,
            Level = level,
            Message = message,
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var process = _process;
        _process = null;

        // 关 Job 句柄。要不要连带结束里面的进程，取决于「关闭启动器是否保留服务」：
        // 保留 → 只销毁 Job，DSH 继续跑（用户要的）；不保留 → 一并收掉，不留孤儿。
        var keepRunning = ShouldKeepServiceRunning();
        _job?.Dispose(terminate: !keepRunning);
        _job = null;

        if (process is null) return;

        // 要保留服务时，绝不能再去杀进程树。
        if (keepRunning) return;

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // 忽略。
        }

        try
        {
            process.Dispose();
        }
        catch (Exception)
        {
            // 忽略。
        }
    }
}

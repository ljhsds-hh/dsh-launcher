using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DshLauncher.Models;
using DshLauncher.Services;
using Microsoft.Win32;

namespace DshLauncher.ViewModels;

/// <summary>
/// 启动器主视图模型：串起「选目录 → 清理 → 构建 → 启动 → 看日志」整条链路。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly ConfigService _configService = new();
    private readonly ProcessRunner _runner = new();
    private readonly LogBuffer _logBuffer;
    private readonly Dispatcher _dispatcher;
    private readonly LogFileWriter _logFile;
    private readonly Dictionary<string, StepItemViewModel> _steps = new(StringComparer.OrdinalIgnoreCase);

    private TaskCompletionSource<string>? _readySignal;
    private string? _detectedUrl;
    private CancellationTokenSource? _pipelineCts;

    /// <summary>本次启动是否新建了配置文件（首次运行），用于在日志里说明一句。</summary>
    private readonly bool _configFileCreated;

    /// <summary>后台线程会读，标记当前是否需要捕获就绪日志。</summary>
    private volatile bool _awaitingReady;

    /// <summary>标记「本次退出是用户主动停止」，用于把失败改判为已中止。</summary>
    private volatile bool _stopRequested;

    /// <summary>标记有 RunStepAsync 正在等待，避免退出事件与它抢状态。</summary>
    private volatile bool _runInFlight;

    public MainViewModel(LogFileWriter logFile)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _logFile = logFile;
        _config = _configService.Load();
        _logFile.Enabled = _config.WriteLogFile;
        _logBuffer = new LogBuffer(_config.MaxLogLines);

        _runner.LineWritten += OnLineWritten;
        _runner.ProcessExited += OnProcessExited;

        // 别漏了这行：ProcessRunner 里那个开关默认是 false（关窗就停），
        // 配置里默认是 true（关窗保留服务）。这里不同步的话，
        // 关窗仍会把服务收掉——实测踩过一次。
        _runner.KeepServiceRunningOnExit = _config.KeepServiceRunningOnExit;

        Steps = new ObservableCollection<StepItemViewModel>();

        // 显示顺序按真实执行顺序（清理 → 构建 → 启动）排，配置里顺序乱了也不会把
        // 序号和流程对错；配置里多出来的步骤照旧接在后面，不丢。
        var ordered = new List<LaunchStep>();
        foreach (var stepId in CommandCatalog.PipelineOrder)
        {
            var match = _config.Steps.FirstOrDefault(
                step => string.Equals(step.Id, stepId, StringComparison.OrdinalIgnoreCase));

            if (match is not null) ordered.Add(match);
        }

        ordered.AddRange(_config.Steps.Where(step => !ordered.Contains(step)));

        for (var i = 0; i < ordered.Count; i++)
        {
            var item = new StepItemViewModel(ordered[i]) { Index = i + 1 };
            Steps.Add(item);
            _steps[item.Id] = item;
        }

        LogView = CollectionViewSource.GetDefaultView(_logBuffer.Entries);
        LogView.Filter = FilterLogEntry;

        _autoScroll = _config.AutoScroll;
        _workspacePath = ResolveInitialWorkspace();
        _serverUrl = $"http://127.0.0.1:{_config.ServerPort}";
        _statusText = "就绪";

        // 配置是懒创建的（只有保存设置 / 关窗才写），第一次用的人点「配置文件」会看到
        // 一个空目录、以为工具坏了 —— 所以启动时先落一份。落盘前把"界面现在正显示的那个
        // 目录"一起写进去，否则这份新文件里 workspacePath 是空的，看着还是像坏的。
        _config.WorkspacePath = _workspacePath;
        _configFileCreated = _configService.SaveIfMissing(_config);

        RefreshWorkspaceState();

        PrintStartupSummary();
    }

    public ObservableCollection<StepItemViewModel> Steps { get; }

    public ICollectionView LogView { get; }

    public AppConfig Config => _config;

    /// <summary>标题区显示的版本号。</summary>
    public string Version => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public string ConfigFilePath => _configService.FilePath;

    /// <summary>本次会话的日志文件（一次运行一个文件）。</summary>
    public string LogFilePath => _logFile.CurrentFilePath;

    public string LogDirectory => _logFile.LogDirectory;

    public StepItemViewModel CleanStep => _steps[CommandCatalog.CleanStepId];

    public StepItemViewModel BuildStep => _steps[CommandCatalog.BuildStepId];

    public StepItemViewModel StartStep => _steps[CommandCatalog.StartStepId];

    [ObservableProperty]
    private string _workspacePath = string.Empty;

    [ObservableProperty]
    private string _statusText = "就绪";

    /// <summary>
    /// 状态栏上这条消息是不是"坏消息"，用来把状态胶囊染红。
    /// 由 <see cref="StatusText"/> 的 <c>⚠ </c> 前缀推导 —— 界面显示什么就按什么判，
    /// 不在别处再维护一份标志位，免得改了一处忘了另一处。
    /// </summary>
    [ObservableProperty]
    private bool _statusIsError;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>工作目录校验结论，直接驱动目录卡片上的对勾 / 叉号。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkspaceStateText))]
    private WorkspaceState _workspaceState = WorkspaceState.Unknown;

    /// <summary>校验细节（缺哪个文件），显示在目录卡片下方，省得去日志里翻。</summary>
    [ObservableProperty]
    private string _workspaceDetail = string.Empty;

    public string WorkspaceStateText => WorkspaceState switch
    {
        WorkspaceState.Valid => "目录校验通过",
        WorkspaceState.Invalid => "目录校验未通过",
        _ => "尚未校验",
    };

    [ObservableProperty]
    private int _pipelineIndex = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    [NotifyPropertyChangedFor(nameof(HasRunningProcess))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRunningProcess))]
    private bool _isServerRunning;

    public bool CanOperate => !IsBusy;

    /// <summary>
    /// 停止按钮**永远可点**：端口被别人占着时也要能一键把端口抢回来。
    /// 没东西可停时点它只会给一条提示。
    /// </summary>
    public bool CanStop => true;

    /// <summary>本启动器自己有没有正在跑的进程（关窗口确认框看这个，不看 CanStop）。</summary>
    public bool HasRunningProcess => IsBusy || IsServerRunning;

    public bool HasFilter => !string.IsNullOrEmpty(FilterText);

    // ---------------------------------------------------------------- 命令

    [RelayCommand]
    private void BrowseWorkspace()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 DeepSeek Harness 仓库目录",
            Multiselect = false,
        };

        if (Directory.Exists(WorkspacePath))
        {
            dialog.InitialDirectory = WorkspacePath;
        }

        if (dialog.ShowDialog() != true) return;

        Log(LogLevel.System, $"工作目录切换为：{dialog.FolderName}");
        WorkspacePath = dialog.FolderName;
        RememberWorkspace(dialog.FolderName);
        VerifyWorkspace();
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(_logFile.LogDirectory);
            Process.Start(new ProcessStartInfo(_logFile.LogDirectory) { UseShellExecute = true });
            Log(LogLevel.System, $"已打开日志目录：{_logFile.LogDirectory}");
        }
        catch (Exception ex)
        {
            ReportError($"打开日志目录失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 在资源管理器里打开**当前工作目录**（就是上面那个路径框指向的 DSH 仓库目录，
    /// 不是本工具自己的目录）。为了不让人和「选择目录…」以及右上角那几个工具目录看串，
    /// 按钮的 ToolTip 直接把要打开的路径显示出来。
    /// </summary>
    [RelayCommand]
    private void OpenWorkspaceFolder()
    {
        if (!Directory.Exists(WorkspacePath))
        {
            Log(LogLevel.Error, "工作目录不存在。");
            Notify("工作目录不存在", isError: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(WorkspacePath) { UseShellExecute = true });
            Log(LogLevel.System, $"已在资源管理器中打开工作目录：{WorkspacePath}");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"打开目录失败：{ex.Message}");
            Notify($"打开目录失败：{ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// 打开本工具自己的配置文件（<c>%AppData%\DshLauncher\config.json</c>）。
    /// 文件在启动时就已经保证存在（<see cref="ConfigService.SaveIfMissing"/>），
    /// 所以这里直接把**文件本身**选中，而不是只开一个目录让人自己找 ——
    /// 按钮叫「配置文件」，那就应该一眼看见那个文件。
    /// </summary>
    [RelayCommand]
    private void OpenConfigFile()
    {
        var file = _configService.FilePath;
        var directory = Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(directory)) return;

        try
        {
            Directory.CreateDirectory(directory);

            if (File.Exists(file))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
                Log(LogLevel.System, $"已定位到配置文件：{file}");
                StatusText = "已打开配置文件所在位置";
                return;
            }

            // 极端情况（文件被外部删掉且没写成功）：退化成打开目录，别让按钮点了没反应。
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            Log(LogLevel.Warn, $"配置文件不存在，已改为打开配置目录：{directory}");
            Notify("配置文件不存在，已打开配置目录", isError: true);
        }
        catch (Exception ex)
        {
            ReportError($"打开配置文件失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private void VerifyWorkspace()
    {
        RefreshWorkspaceState();

        var valid = WorkspaceState == WorkspaceState.Valid;
        Log(valid ? LogLevel.System : LogLevel.Warn, WorkspaceDetail);

        // 结果同时落在状态栏上：不依赖轻提示，看得见也留得住。
        StatusText = WorkspaceStateText;
        Notify(WorkspaceDetail, isError: !valid);
    }

    /// <summary>重新探测工作目录并刷新卡片上的校验结论。</summary>
    private void RefreshWorkspaceState()
    {
        WorkspaceState = WorkspaceProbe.LooksLikeDshRepository(WorkspacePath)
            ? WorkspaceState.Valid
            : WorkspaceState.Invalid;

        WorkspaceDetail = WorkspaceProbe.Describe(WorkspacePath);
    }

    [RelayCommand]
    private Task RunCleanAsync() => RunSingleStepAsync(CommandCatalog.CleanStepId);

    [RelayCommand]
    private Task RunBuildAsync() => RunSingleStepAsync(CommandCatalog.BuildStepId);

    [RelayCommand]
    private Task RunStartAsync() => RunSingleStepAsync(CommandCatalog.StartStepId);

    /// <summary>一键执行：清理 → 构建 → 启动，任一步失败即中断。</summary>
    [RelayCommand]
    private async Task RunPipelineAsync()
    {
        if (IsBusy)
        {
            Notify("已有任务在执行中，请等待完成或先停止", isError: true);
            return;
        }

        _pipelineCts?.Dispose();
        _pipelineCts = new CancellationTokenSource();
        var token = _pipelineCts.Token;

        Log(LogLevel.System, "===== 一键执行：清理 → 构建 → 启动 =====");

        foreach (var stepId in CommandCatalog.PipelineOrder)
        {
            if (token.IsCancellationRequested) break;

            var step = _steps[stepId];
            var succeeded = await RunStepAsync(step, token).ConfigureAwait(true);
            if (!succeeded) break;

            // 启动是常驻服务，跑起来之后流水线就结束了。
            if (step.Model.LongRunning) break;
        }
    }

    /// <summary>
    /// 停止。始终可点：
    /// <list type="number">
    ///   <item>本启动器自己拉起来的进程 → 走 Job Object，最干净；</item>
    ///   <item>本启动器没在跑，但配置端口被别人占着（比如手动起的、或上次没收干净的 DSH）
    ///   → 找到占用者并结束它整棵树，这就是「一键把端口抢回来」；</item>
    ///   <item>端口空着 → 只给一条提示，不做任何事。</item>
    /// </list>
    /// </summary>
    [RelayCommand]
    private async Task StopAsync()
    {
        _stopRequested = true;
        _pipelineCts?.Cancel();

        var port = _config.ServerPort;

        // 1) 自己拉起来的
        if (_runner.IsRunning)
        {
            Log(LogLevel.System, "用户请求停止…");
            await _runner.StopAsync().ConfigureAwait(true);
            FinishStop("已停止");
            return;
        }

        // 2) 没在跑：看看端口上有没有别人
        var ownerPid = PortProbe.FindOwnerProcessId(port);

        if (ownerPid is null)
        {
            Log(LogLevel.System, $"端口 {port} 没有被占用，本启动器也没有正在运行的进程，无需停止。");
            Notify($"端口 {port} 空闲，没有需要停止的进程");
            return;
        }

        var owner = Describe(ownerPid.Value);

        // 只对 Node/DSH 下手，别把别的程序随手杀了。
        if (!string.Equals(owner.Name, "node", StringComparison.OrdinalIgnoreCase))
        {
            Log(
                LogLevel.Warn,
                $"端口 {port} 被 {owner.Name} (PID {ownerPid}) 占用，它不是 Node / DSH 进程，为安全起见已拒绝结束。");
            Notify($"端口 {port} 被 {owner.Name} 占用，已拒绝结束");
            return;
        }

        Log(
            LogLevel.System,
            $"端口 {port} 被 {owner.Name} (PID {ownerPid}) 占用，开始结束它及其全部后代…");

        FinishStop($"已停止（端口 {port} 已释放）");

        var killed = await Task.Run(() => ProcessTree.KillTree(ownerPid.Value)).ConfigureAwait(true);

        // 杀完复查：端口还占着就说明没杀干净（或者立刻又有人抢了）。
        await Task.Delay(500).ConfigureAwait(true);

        if (PortProbe.IsListening(port))
        {
            Log(LogLevel.Error, $"已结束 {killed} 个进程，但端口 {port} 仍被占用，请到任务管理器确认。");
            Notify($"端口 {port} 仍被占用，未完全结束", isError: true);
        }
        else
        {
            Log(LogLevel.System, $"已结束 {killed} 个进程，端口 {port} 已释放。");
        }
    }

    /// <summary>停止流程统一的收尾：状态复位 + 运行中的步骤改判为已中止。</summary>
    private void FinishStop(string statusText)
    {
        IsBusy = false;
        IsServerRunning = false;
        PipelineIndex = -1;
        StatusText = statusText;

        foreach (var step in Steps.Where(step => step.State == StepState.Running))
        {
            step.State = StepState.Cancelled;
        }

        _stopRequested = false;
    }

    private static (string Name, string Display) Describe(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return (process.ProcessName, $"{process.ProcessName} (PID {processId})");
        }
        catch (Exception)
        {
            return ("未知进程", $"PID {processId}");
        }
    }

    [RelayCommand]
    private void OpenBrowser()
    {
        var url = string.IsNullOrWhiteSpace(ServerUrl)
            ? $"http://127.0.0.1:{_config.ServerPort}"
            : ServerUrl;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            Log(LogLevel.System, $"已打开 {url}");
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"打开浏览器失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _logBuffer.Clear();
        Log(LogLevel.System, "日志已清空。");
    }

    [RelayCommand]
    private void CopyLogs()
    {
        var text = BuildLogText();
        if (text.Length == 0)
        {
            Notify("当前没有日志", isError: true);
            return;
        }

        try
        {
            Clipboard.SetText(text);
            StatusText = "日志已复制到剪贴板";
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"复制日志失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private void ExportLogs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出日志",
            Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"dsh-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            DefaultExt = ".log",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, BuildLogText(), new UTF8Encoding(false));
            Log(LogLevel.System, $"日志已导出到 {dialog.FileName}");
            StatusText = "日志已导出";
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, $"导出日志失败：{ex.Message}");
            Notify("导出日志失败", isError: true);
        }
    }

    // ---------------------------------------------------------------- 执行

    private async Task RunSingleStepAsync(string stepId)
    {
        if (IsBusy)
        {
            Notify("已有任务在执行中，请等待完成或先停止", isError: true);
            return;
        }

        _stopRequested = false;
        await RunStepAsync(_steps[stepId], CancellationToken.None).ConfigureAwait(true);
    }

    private async Task<bool> RunStepAsync(StepItemViewModel step, CancellationToken token)
    {
        if (!WorkspaceProbe.LooksLikeDshRepository(WorkspacePath))
        {
            Log(LogLevel.Error, $"工作目录不可用：{WorkspaceProbe.Describe(WorkspacePath)}");
            Notify("请先选择有效的 DeepSeek Harness 仓库目录", isError: true);
            return false;
        }

        // 上一个进程还活着就先停掉：pnpm 派生的子进程会占住构建产物和端口。
        if (_runner.IsRunning)
        {
            Log(LogLevel.System, "检测到上一个进程仍在运行，先停止它…");
            _stopRequested = true;
            await _runner.StopAsync().ConfigureAwait(true);
            _stopRequested = false;
            await Task.Delay(400).ConfigureAwait(true);
        }

        // 端口已被占用通常意味着已经有一个 DSH 在跑。Windows 的 SO_REUSEADDR 允许
        // 两个实例同时绑同一端口，放任下去会出现「两个 DSH、谁也说不清谁在服务」的局面，
        // 所以默认直接中止，交回用户决定（设置里可放开）。
        if (step.Model.LongRunning && !_config.AllowStartWhenPortBusy
            && PortProbe.IsListening(_config.ServerPort))
        {
            step.Reset();
            step.State = StepState.Failed;
            PipelineIndex = Steps.IndexOf(step);
            StatusText = $"启动被中止：端口 {_config.ServerPort} 已被占用";

            Log(
                LogLevel.Error,
                $"端口 {_config.ServerPort} 已被占用，说明很可能已经有一个 DeepSeek Harness 在运行，"
                + "本次「启动」已中止以避免起第二个实例。"
                + "请先关闭那个实例；若确实要在端口被占用的情况下启动，"
                + "到「设置 → 服务」勾选「端口被占用时仍允许启动」。");

            Notify($"端口 {_config.ServerPort} 已被占用，「启动」已中止", isError: true);
            return false;
        }

        step.Reset();
        step.State = StepState.Running;
        PipelineIndex = Steps.IndexOf(step);
        IsBusy = true;
        StatusText = $"{step.Name}中…";
        Log(LogLevel.System, $"===== {step.Name} 开始 =====");

        _detectedUrl = null;
        _readySignal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _awaitingReady = step.Model.LongRunning;
        _stopRequested = false;
        _runInFlight = true;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _runner.Start(step.Model, WorkspacePath);
        }
        catch (Exception ex)
        {
            _awaitingReady = false;
            _runInFlight = false;
            stopwatch.Stop();
            step.State = StepState.Failed;
            step.Elapsed = FormatElapsed(stopwatch.Elapsed);
            IsBusy = false;
            StatusText = $"{step.Name}启动失败";
            Log(LogLevel.Error, $"无法启动进程：{ex.Message}");
            Notify($"无法启动：{ex.Message}", isError: true);
            return false;
        }

        var completion = _runner.Completion;

        if (step.Model.LongRunning)
        {
            var ready = await WaitForServerReadyAsync(completion, token).ConfigureAwait(true);
            _runInFlight = false;
            stopwatch.Stop();
            step.Elapsed = FormatElapsed(stopwatch.Elapsed);
            IsBusy = false;

            if (ready)
            {
                step.State = StepState.Succeeded;
                IsServerRunning = true;

                var url = string.IsNullOrWhiteSpace(_detectedUrl)
                    ? $"http://127.0.0.1:{_config.ServerPort}"
                    : _detectedUrl;
                ServerUrl = url;
                Log(LogLevel.System, $"===== 服务已就绪：{url} =====");

                // 顺序要紧：Notify 也是写 StatusText，放在后面会把"服务运行中"
                // 这条常驻状态顶掉。状态栏现在是常驻状态指示器（胶囊变绿），
                // 所以最后落地的必须是状态本身。
                Notify($"DeepSeek Harness 已启动：{url}");
                StatusText = $"服务运行中 · {url}";

                if (_config.OpenBrowserWhenReady) OpenBrowser();
                return true;
            }

            var exitCode = await completion.ConfigureAwait(true);

            if (_stopRequested)
            {
                step.State = StepState.Cancelled;
                StatusText = "已停止";
                return false;
            }

            step.State = exitCode == 0 ? StepState.Cancelled : StepState.Failed;
            StatusText = $"启动失败（退出码 {exitCode}）";
            Log(LogLevel.Error, $"===== 启动失败，退出码 {exitCode} =====");
            Notify("启动失败，请查看日志", isError: true);
            return false;
        }

        var code = await completion.ConfigureAwait(true);
        _runInFlight = false;
        stopwatch.Stop();
        step.Elapsed = FormatElapsed(stopwatch.Elapsed);
        IsBusy = false;

        if (_stopRequested || token.IsCancellationRequested)
        {
            step.State = StepState.Cancelled;
            StatusText = $"{step.Name}已中止";
            return false;
        }

        if (code == 0)
        {
            step.State = StepState.Succeeded;
            StatusText = $"{step.Name}完成 · {step.Elapsed}";
            Log(LogLevel.System, $"===== {step.Name} 完成，用时 {step.Elapsed} =====");
            return true;
        }

        step.State = StepState.Failed;
        StatusText = $"{step.Name}失败（退出码 {code}）";
        Log(LogLevel.Error, $"===== {step.Name} 失败，退出码 {code} =====");
        Notify($"{step.Name}失败，请查看日志", isError: true);
        return false;
    }

    /// <summary>
    /// 等常驻服务就绪：命中就绪日志 → 成功；进程提前退出 → 失败；
    /// 超时但进程还活着 → 按已启动处理（有些启动路径不会打印地址）。
    /// </summary>
    private async Task<bool> WaitForServerReadyAsync(Task<int> completion, CancellationToken token)
    {
        var signal = _readySignal;
        if (signal is null) return false;

        var timeout = Task.Delay(TimeSpan.FromSeconds(180), token);
        var finished = await Task.WhenAny(signal.Task, completion, timeout).ConfigureAwait(true);

        _awaitingReady = false;

        if (finished == signal.Task) return true;

        if (_stopRequested || token.IsCancellationRequested) return false;

        if (finished == timeout)
        {
            if (!_runner.IsRunning)
            {
                Log(LogLevel.Error, "等待服务就绪超时，且进程已经退出。");
                return false;
            }

            Log(LogLevel.Warn, "等待就绪日志超时，但进程仍在运行，按已启动处理。");
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- 事件

    private void OnLineWritten(LogEntry entry)
    {
        // 后台线程：界面由 LogBuffer 按 100ms 批量刷新，文件由 LogFileWriter 后台落盘。
        _logBuffer.Enqueue(entry);
        _logFile.Write(entry);

        if (!_awaitingReady) return;
        if (entry.Level == LogLevel.Error) return;

        try
        {
            var match = Regex.Match(entry.Message, _config.ReadyPattern);
            if (!match.Success) return;

            _detectedUrl = match.Value;
            _readySignal?.TrySetResult(match.Value);
        }
        catch (ArgumentException)
        {
            // 用户填了非法正则：放弃就绪判定，交给超时兜底。
        }
    }

    private void OnProcessExited(int exitCode)
    {
        _dispatcher.InvokeAsync(() =>
        {
            IsServerRunning = false;

            if (_runInFlight) return;

            if (StartStep.State == StepState.Succeeded)
            {
                StartStep.State = exitCode == 0 ? StepState.Cancelled : StepState.Failed;
                StartStep.Elapsed = string.Empty;
                StatusText = $"服务已停止（退出码 {exitCode}）";
                Log(LogLevel.System, "服务进程已结束。");
            }
        });
    }

    partial void OnFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasFilter));
        LogView.Refresh();
    }

    partial void OnStatusTextChanged(string value)
        => StatusIsError = value.StartsWith("⚠", StringComparison.Ordinal);

    /// <summary>
    /// 手输路径时不碰磁盘，只把结论置回「尚未校验」。
    /// 工作目录可能是网络路径，逐字符探测会把界面卡住；等用户点「检测」
    /// 或者用「浏览…」选完目录时再真正下结论。
    /// </summary>
    partial void OnWorkspacePathChanged(string value)
    {
        WorkspaceState = WorkspaceState.Unknown;
        WorkspaceDetail = "点「检测」确认这个目录是不是 DeepSeek Harness 仓库。";
    }

    private bool FilterLogEntry(object item)
    {
        if (item is not LogEntry entry) return false;
        if (string.IsNullOrWhiteSpace(FilterText)) return true;

        return entry.Message.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || entry.Source.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || entry.LevelText.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- 设置与持久化

    /// <summary>供界面层在出错时回写日志并提示。</summary>
    public void ReportError(string message)
    {
        Log(LogLevel.Error, message);
        Notify(message, isError: true);
    }

    public void ApplySettings(SettingsViewModel settings)
    {
        settings.ApplyTo(_config);

        AutoScroll = _config.AutoScroll;
        _logBuffer.MaxLines = _config.MaxLogLines;
        _logFile.Enabled = _config.WriteLogFile;
        _runner.KeepServiceRunningOnExit = _config.KeepServiceRunningOnExit;

        foreach (var step in Steps)
        {
            step.RefreshFromModel();
        }

        if (!IsServerRunning)
        {
            ServerUrl = $"http://127.0.0.1:{_config.ServerPort}";
        }

        _configService.Save(_config);

        Log(LogLevel.System, "----- 设置已更新 -----");
        Log(LogLevel.System, $"清理 = 「{_config.Steps.First(s => s.Id == CommandCatalog.CleanStepId).Command}」");
        Log(LogLevel.System, $"构建 = 「{_config.Steps.First(s => s.Id == CommandCatalog.BuildStepId).Command}」");
        Log(LogLevel.System, $"启动 = 「{_config.Steps.First(s => s.Id == CommandCatalog.StartStepId).Command}」");
        Log(LogLevel.System, $"端口 {_config.ServerPort}，就绪正则 {_config.ReadyPattern}");
        Log(LogLevel.System, $"自动滚动 {(_config.AutoScroll ? "开" : "关")}，日志上限 {_config.MaxLogLines} 行，写日志文件 {(_config.WriteLogFile ? "开" : "关")}");
        Log(LogLevel.System, $"设置已保存：{_configService.FilePath}");

        StatusText = "设置已保存";
    }

    /// <summary>窗口关闭时保存状态，下次打开直接回到上次的目录与尺寸。</summary>
    public void PersistState(double width, double height)
    {
        _config.WorkspacePath = WorkspacePath;
        _config.WindowWidth = width;
        _config.WindowHeight = height;
        _config.AutoScroll = AutoScroll;
        _configService.Save(_config);
    }

    /// <summary>
    /// 关闭窗口前的收尾。
    /// 是否保留已启动的 DSH 服务由 <see cref="AppConfig.KeepServiceRunningOnExit"/> 决定：
    /// 保留时只摘掉日志，让服务继续在后台跑。
    /// </summary>
    public void Shutdown(string reason = "窗口关闭")
    {
        _pipelineCts?.Cancel();
        _pipelineCts?.Dispose();
        _pipelineCts = null;

        var keepRunning = _config.KeepServiceRunningOnExit && _runner.IsRunning;
        _runner.KeepServiceRunningOnExit = _config.KeepServiceRunningOnExit;

        _runner.LineWritten -= OnLineWritten;
        _runner.ProcessExited -= OnProcessExited;
        _runner.Dispose();

        Log(LogLevel.System, $"===== 会话结束（{reason}）=====");

        if (keepRunning)
        {
            Log(
                LogLevel.System,
                "按设置保留了 DSH 服务：启动器已退出，服务继续运行。"
                + $"要停它，重新打开启动器点「停止」即可（会按端口 {_config.ServerPort} 找到它）。");
        }

        Log(LogLevel.System, $"本次日志：{_logFile.CurrentFilePath}");

        // 走完 Dispose 才会把队列刷干净，所以必须放在最后。
        _logFile.Dispose();
    }

    private void RememberWorkspace(string path)
    {
        _config.RecentWorkspaces.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        _config.RecentWorkspaces.Insert(0, path);

        while (_config.RecentWorkspaces.Count > 8)
        {
            _config.RecentWorkspaces.RemoveAt(_config.RecentWorkspaces.Count - 1);
        }

        _config.WorkspacePath = path;
    }

    private string ResolveInitialWorkspace()
    {
        if (WorkspaceProbe.LooksLikeDshRepository(_config.WorkspacePath)) return _config.WorkspacePath;

        foreach (var recent in _config.RecentWorkspaces)
        {
            if (WorkspaceProbe.LooksLikeDshRepository(recent)) return recent;
        }

        return WorkspaceProbe.FindDefaultWorkspace() ?? _config.WorkspacePath;
    }

    private void PrintStartupSummary()
    {
        Log(LogLevel.System, "===== 会话开始 =====");
        Log(LogLevel.System, $"启动器：DshLauncher v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}");
        Log(LogLevel.System, $"运行环境：{Environment.OSVersion.VersionString}（{(Environment.Is64BitOperatingSystem ? "x64" : "x86")}）, .NET {Environment.Version}");
        Log(LogLevel.System, $"机器名：{Environment.MachineName}，用户：{Environment.UserName}");
        Log(LogLevel.System, $"配置文件：{_configService.FilePath}{(_configFileCreated ? "（首次运行，已写入默认配置）" : string.Empty)}");
        Log(LogLevel.System, $"日志文件：{(_logFile.Enabled ? _logFile.CurrentFilePath : "（未开启写入文件）")}");
        Log(LogLevel.System, $"工作目录：{WorkspacePath}");
        Log(LogLevel.System, WorkspaceProbe.Describe(WorkspacePath));

        var occupied = PortProbe.IsListening(_config.ServerPort);
        Log(LogLevel.System, $"端口 {_config.ServerPort}：{(occupied ? "已被占用" : "空闲")}");
        Log(LogLevel.System, $"命令：清理 = 「{CleanStep.Command}」 / 构建 = 「{BuildStep.Command}」 / 启动 = 「{StartStep.Command}」");
    }

    private string BuildLogText()
    {
        var builder = new StringBuilder();
        foreach (var entry in _logBuffer.Entries)
        {
            builder.AppendLine(
                $"{entry.Time:yyyy-MM-dd HH:mm:ss} {entry.SourceText} {entry.LevelText,-5} {entry.Message}");
        }

        return builder.ToString();
    }

    private void Log(LogLevel level, string message)
    {
        var entry = new LogEntry
        {
            Time = DateTime.Now,
            Source = "launcher",
            Level = level,
            Message = message,
        };

        _logBuffer.Enqueue(entry);
        _logFile.Write(entry);
    }

    /// <summary>
    /// 轻提示统一落在状态栏上。
    /// </summary>
    /// <remarks>
    /// 原来用 HandyControl 的 Growl 弹浮层，但它把提示**当子元素**塞进注册的 Panel，
    /// 注册成内容 Grid 时提示全落到 Row 0、正好压在「DSH 目录」那一行上，界面看着就崩了；
    /// 换成独立浮层后 UIA 又完全看不到提示元素，无法验证它到底渲染成什么样。
    /// 状态栏本来就一直在显示当前状态，改成写这里：不弹浮层、不影响布局、看得见也留得住。
    /// </remarks>
    private void Notify(string message, bool isError = false)
        => StatusText = isError ? $"⚠ {message}" : message;

    private static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalSeconds < 60
            ? $"{elapsed.TotalSeconds:F1}s"
            : $"{(int)elapsed.TotalMinutes}m{elapsed.Seconds:D2}s";
}

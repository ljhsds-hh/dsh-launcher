using CommunityToolkit.Mvvm.ComponentModel;
using DshLauncher.Models;
using DshLauncher.Services;

namespace DshLauncher.ViewModels;

/// <summary>
/// 设置对话框的可编辑副本：只有点「保存」后才写回 <see cref="AppConfig"/>，
/// 中途关闭不会污染当前配置。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _cleanCommand = string.Empty;

    [ObservableProperty]
    private string _buildCommand = string.Empty;

    [ObservableProperty]
    private string _startCommand = string.Empty;

    [ObservableProperty]
    private double _serverPort = 3080;

    [ObservableProperty]
    private bool _allowStartWhenPortBusy;

    [ObservableProperty]
    private bool _keepServiceRunningOnExit = true;

    [ObservableProperty]
    private string _readyPattern = string.Empty;

    [ObservableProperty]
    private bool _openBrowserWhenReady;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private double _maxLogLines = 5000;

    [ObservableProperty]
    private bool _writeLogFile = true;

    public SettingsViewModel(AppConfig config)
    {
        CleanCommand = GetCommand(config, CommandCatalog.CleanStepId);
        BuildCommand = GetCommand(config, CommandCatalog.BuildStepId);
        StartCommand = GetCommand(config, CommandCatalog.StartStepId);
        ServerPort = config.ServerPort;
        AllowStartWhenPortBusy = config.AllowStartWhenPortBusy;
        KeepServiceRunningOnExit = config.KeepServiceRunningOnExit;
        ReadyPattern = config.ReadyPattern;
        OpenBrowserWhenReady = config.OpenBrowserWhenReady;
        AutoScroll = config.AutoScroll;
        MaxLogLines = config.MaxLogLines;
        WriteLogFile = config.WriteLogFile;
    }

    public string ConfigFilePath => new ConfigService().FilePath;

    public string LogDirectoryPath => LogFileWriter.DefaultLogDirectory;

    public void ApplyTo(AppConfig config)
    {
        SetCommand(config, CommandCatalog.CleanStepId, CleanCommand);
        SetCommand(config, CommandCatalog.BuildStepId, BuildCommand);
        SetCommand(config, CommandCatalog.StartStepId, StartCommand);

        config.ServerPort = ServerPort is > 0 and <= 65535 ? (int)ServerPort : 3080;
        config.AllowStartWhenPortBusy = AllowStartWhenPortBusy;
        config.KeepServiceRunningOnExit = KeepServiceRunningOnExit;
        config.ReadyPattern = string.IsNullOrWhiteSpace(ReadyPattern)
            ? @"https?://(?:127\.0\.0\.1|localhost):\d+"
            : ReadyPattern.Trim();
        config.OpenBrowserWhenReady = OpenBrowserWhenReady;
        config.AutoScroll = AutoScroll;
        config.MaxLogLines = Math.Max(200, (int)MaxLogLines);
        config.WriteLogFile = WriteLogFile;
    }

    private static string GetCommand(AppConfig config, string stepId)
        => config.Steps.FirstOrDefault(step => step.Id == stepId)?.Command ?? string.Empty;

    private static void SetCommand(AppConfig config, string stepId, string command)
    {
        var step = config.Steps.FirstOrDefault(item => item.Id == stepId);
        if (step is null) return;

        var trimmed = command.Trim();
        if (trimmed.Length > 0) step.Command = trimmed;
    }
}

using System.Windows;
using System.Windows.Threading;
using DshLauncher.Models;
using DshLauncher.Services;

namespace DshLauncher;

public partial class App : Application
{
    private LogFileWriter? _logFile;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        base.OnStartup(e);

        // 日志写入器先建好：视图模型与未处理异常都往同一个文件里写。
        _logFile = new LogFileWriter();

        var viewModel = new ViewModels.MainViewModel(_logFile);
        var window = new Views.MainWindow(viewModel);

        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("界面线程未处理异常", e.Exception);
        Report("界面线程异常", e.Exception);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogException("后台线程未处理异常", exception);
            Report("后台线程异常", exception);
        }
    }

    private void LogException(string title, Exception exception)
    {
        _logFile?.Write(new LogEntry
        {
            Time = DateTime.Now,
            Source = "launcher",
            Level = LogLevel.Error,
            Message = $"{title}：{exception}",
        });
    }

    private static void Report(string title, Exception exception)
    {
        try
        {
            MessageBox.Show(
                $"{title}：\n{exception.Message}\n\n{exception.StackTrace}",
                "DshLauncher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // 连报错窗口都弹不出来时只能放弃。
        }
    }
}

using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DshLauncher.ViewModels;

namespace DshLauncher.Views;

public partial class MainWindow : HandyControl.Controls.Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        Width = _viewModel.Config.WindowWidth;
        Height = _viewModel.Config.WindowHeight;

        // 日志追加时自动滚到底部（用户可以在界面上关掉）。
        ((INotifyCollectionChanged)LogList.Items).CollectionChanged += OnLogItemsChanged;
    }

    private void OnLogItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        if (!_viewModel.AutoScroll) return;

        var count = LogList.Items.Count;
        if (count == 0) return;

        try
        {
            LogList.ScrollIntoView(LogList.Items[count - 1]);
        }
        catch (InvalidOperationException)
        {
            // 正在虚拟化回收时可能取不到容器，忽略即可。
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsViewModel(_viewModel.Config);
        var dialog = new SettingsWindow(settings) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.ApplySettings(settings);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 只有「本启动器自己起的进程还在跑」**且**「设置里要求关窗时一起停」才弹确认。
        // 默认设置是保留服务，直接关窗即可，不打断用户。
        if (_viewModel.HasRunningProcess && !_viewModel.Config.KeepServiceRunningOnExit)
        {
            var answer = MessageBox.Show(
                "DeepSeek Harness 进程仍在运行。\n\n关闭启动器会一并结束它，确定关闭吗？",
                "DshLauncher",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.OK)
            {
                e.Cancel = true;
                return;
            }
        }

        _viewModel.PersistState(Width, Height);
        _viewModel.Shutdown($"窗口关闭（{DateTime.Now:HH:mm:ss}）");

        ((INotifyCollectionChanged)LogList.Items).CollectionChanged -= OnLogItemsChanged;

        base.OnClosing(e);
    }
}

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using DshLauncher.Models;

namespace DshLauncher.Services;

/// <summary>
/// 日志缓冲：后台线程只负责入队，UI 线程按固定节奏批量取出。
/// 构建期间每秒可能产生上百行输出，逐行刷新界面会让窗口卡死。
/// </summary>
public sealed class LogBuffer
{
    private readonly ConcurrentQueue<LogEntry> _pending = new();
    private readonly DispatcherTimer _timer;
    private int _maxLines;

    public LogBuffer(int maxLines)
    {
        _maxLines = Math.Max(200, maxLines);

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _timer.Tick += (_, _) => Flush();
        _timer.Start();
    }

    /// <summary>只在 UI 线程读取。</summary>
    public ObservableCollection<LogEntry> Entries { get; } = new();

    public int MaxLines
    {
        get => _maxLines;
        set => _maxLines = Math.Max(200, value);
    }

    /// <summary>线程安全。</summary>
    public void Enqueue(LogEntry entry) => _pending.Enqueue(entry);

    /// <summary>只能在 UI 线程调用。</summary>
    public void Clear()
    {
        while (_pending.TryDequeue(out _))
        {
            // 丢弃尚未刷出的内容。
        }

        Entries.Clear();
    }

    private void Flush()
    {
        if (_pending.IsEmpty) return;

        var appended = 0;
        while (appended < 3000 && _pending.TryDequeue(out var entry))
        {
            Entries.Add(entry);
            appended++;
        }

        var excess = Entries.Count - _maxLines;
        for (var i = 0; i < excess; i++)
        {
            Entries.RemoveAt(0);
        }
    }
}

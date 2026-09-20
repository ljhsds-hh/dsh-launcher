using System.IO;
using System.Text;
using System.Threading.Channels;
using DshLauncher.Models;

namespace DshLauncher.Services;

/// <summary>
/// 把界面上出现的每一行日志同步落到文件，做到「一次运行 = 一个完整日志文件」。
/// </summary>
/// <remarks>
/// 关键点：
/// <list type="bullet">
///   <item>一个会话一个文件（文件名带启动时间），关掉程序后这个文件就是这次运行的完整记录；</item>
///   <item>写入走后台线程 + 无界 Channel，<b>绝不阻塞</b>读进程输出的线程（构建时每秒上百行）；</item>
///   <item>排空即 flush，程序被强杀时最多丢最后不到一秒的内容；</item>
///   <item>任何写入异常都吞掉——记日志失败不能影响主流程。</item>
/// </list>
/// </remarks>
public sealed class LogFileWriter : IDisposable
{
    /// <summary>保留最近多少个日志文件，更旧的启动时清掉。</summary>
    private const int RetainedFiles = 14;

    /// <summary>默认日志目录：<c>%AppData%\DshLauncher\logs</c>。</summary>
    public static string DefaultLogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DshLauncher",
        "logs");

    private readonly Channel<string> _channel;
    private readonly Task _worker;
    private bool _disposed;

    /// <summary>已被本进程占用的日志路径，避免同一秒启动的实例撞名。</summary>
    private static readonly HashSet<string> ReservedPaths = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object ReservedGate = new();

    /// <param name="directoryPath">日志目录；传 null 时用 <see cref="DefaultLogDirectory"/>。</param>
    /// <param name="enabled">是否真的写文件。</param>
    public LogFileWriter(string? directoryPath = null, bool enabled = true)
    {
        Enabled = enabled;
        LogDirectory = string.IsNullOrWhiteSpace(directoryPath) ? DefaultLogDirectory : directoryPath;

        CurrentFilePath = BuildFilePath();

        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        _worker = Task.Run(DrainAsync);
        Prune();
    }

    public bool Enabled { get; set; }

    public string LogDirectory { get; }

    /// <summary>本次会话的日志文件全路径。</summary>
    public string CurrentFilePath { get; }

    /// <summary>后台写盘线程最近一次失败的原因（正常时为 null），便于排查「日志文件不完整」。</summary>
    public Exception? LastError { get; private set; }

    public void Write(LogEntry entry)
    {
        if (!Enabled || _disposed) return;

        var line = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{entry.Time:yyyy-MM-dd HH:mm:ss.fff} {entry.SourceText} {entry.LevelText,-5} {entry.Message}");

        _channel.Writer.TryWrite(line);
    }

    /// <summary>写一行启动器自身的日志（不需要构造 LogEntry 的场合）。</summary>
    public void WriteSystem(string message) => Write(new LogEntry
    {
        Time = DateTime.Now,
        Source = "launcher",
        Level = LogLevel.System,
        Message = message,
    });

    /// <summary>删除日志目录下的全部日志文件。返回删除的个数。</summary>
    public static int ClearAll(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return 0;

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(directoryPath, "DshLauncher-*.log"))
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch (IOException)
            {
                // 正在被占用就跳过。
            }
            catch (UnauthorizedAccessException)
            {
                // 权限不足就跳过。
            }
        }

        return removed;
    }

    private string BuildFilePath()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        lock (ReservedGate)
        {
            var path = Path.Combine(LogDirectory, $"DshLauncher-{stamp}.log");

            // 同一秒内起两个实例时不互相覆盖：文件可能还没落盘，所以还要看已预订的路径。
            var suffix = 1;
            while (File.Exists(path) || ReservedPaths.Contains(path))
            {
                path = Path.Combine(LogDirectory, $"DshLauncher-{stamp}-{suffix}.log");
                suffix++;
            }

            ReservedPaths.Add(path);
            return path;
        }
    }

    private void Prune()
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return;

            var stale = Directory.EnumerateFiles(LogDirectory, "DshLauncher-*.log")
                .Where(file => !string.Equals(file, CurrentFilePath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(RetainedFiles - 1)
                .ToArray();

            foreach (var file in stale) File.Delete(file);
        }
        catch (Exception)
        {
            // 清理旧日志失败不影响启动。
        }
    }

    private async Task DrainAsync()
    {
        StreamWriter? writer = null;

        try
        {
            // 注意：不能用 ChannelReader.Count（本通道实现不支持，会抛 NotSupportedException）。
            // 用 WaitToRead + TryRead 把「当前可读的全读出来」，然后 flush 一次。
            while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var line))
                {
                    // 关闭写入时直接丢弃：连文件都不要建，避免留下空文件。
                    if (!Enabled) continue;

                    writer ??= CreateWriter();
                    await writer.WriteLineAsync(line).ConfigureAwait(false);
                }

                if (writer is not null) await writer.FlushAsync().ConfigureAwait(false);
            }

            if (writer is not null) await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 写日志失败绝不能影响主流程，但要把原因记下来。
            LastError = ex;
        }
        finally
        {
            try
            {
                writer?.Flush();
                writer?.Dispose();
            }
            catch (Exception)
            {
                // 忽略。
            }
        }
    }

    private StreamWriter CreateWriter()
    {
        Directory.CreateDirectory(LogDirectory);

        return new StreamWriter(
            new FileStream(CurrentFilePath, FileMode.Create, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false))
        {
            AutoFlush = false,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _channel.Writer.TryComplete();

        try
        {
            _worker.Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception)
        {
            // 忽略。
        }
    }
}

using System.IO;
using System.Text;
using DshLauncher.Models;
using DshLauncher.Services;
using Xunit;

namespace DshLauncher.Tests;

public sealed class LogFileWriterTests
{
    private static LogEntry Entry(string message, LogLevel level = LogLevel.Info, string source = "clean")
        => new()
        {
            Time = new DateTime(2024, 3, 1, 9, 5, 7, 123),
            Source = source,
            Level = level,
            Message = message,
        };

    [Fact]
    public void 写入的行按_时间_来源_级别_内容排列()
    {
        using var temp = new TempDirectory();
        var writer = new LogFileWriter(temp.Path);

        writer.Write(Entry("CLEAN-OK"));
        writer.Dispose();

        var text = File.ReadAllText(writer.CurrentFilePath, Encoding.UTF8);

        Assert.Contains("2024-03-01 09:05:07.123", text);
        Assert.Contains("[clean]", text);
        Assert.Contains("INFO", text);
        Assert.Contains("CLEAN-OK", text);
    }

    [Fact]
    public void 多次写入全部落盘且保持顺序()
    {
        using var temp = new TempDirectory();
        var writer = new LogFileWriter(temp.Path);

        for (var i = 0; i < 50; i++) writer.Write(Entry($"line-{i:00}"));
        writer.Dispose();

        var lines = File.ReadAllLines(writer.CurrentFilePath, Encoding.UTF8);

        Assert.Equal(50, lines.Length);
        Assert.Contains("line-00", lines[0]);
        Assert.Contains("line-49", lines[49]);
    }

    [Fact]
    public void 文件名带启动时间且以_DshLauncher_开头()
    {
        using var temp = new TempDirectory();
        var writer = new LogFileWriter(temp.Path);

        var name = Path.GetFileName(writer.CurrentFilePath);

        Assert.StartsWith("DshLauncher-", name);
        Assert.EndsWith(".log", name);
        Assert.Equal(temp.Path, writer.LogDirectory);

        writer.Dispose();
    }

    [Fact]
    public void 关闭写入时不产生日志文件()
    {
        using var temp = new TempDirectory();
        var writer = new LogFileWriter(temp.Path, enabled: false);

        writer.Write(Entry("should-not-be-written"));
        writer.Dispose();

        Assert.False(File.Exists(writer.CurrentFilePath));
    }

    [Fact]
    public void 运行中关掉写入不会写入后续内容()
    {
        using var temp = new TempDirectory();
        var writer = new LogFileWriter(temp.Path);

        writer.Write(Entry("before-disable"));
        Thread.Sleep(200);

        writer.Enabled = false;
        writer.Write(Entry("after-disable"));
        writer.Dispose();

        var text = File.ReadAllText(writer.CurrentFilePath, Encoding.UTF8);

        Assert.Contains("before-disable", text);
        Assert.DoesNotContain("after-disable", text);
    }

    [Fact]
    public void 中文内容按_UTF8_写入不乱码()
    {
        using var temp = new TempDirectory();
        var writer = new LogFileWriter(temp.Path);

        writer.Write(Entry("清理完成 · 用时 1.2s"));
        writer.Dispose();

        var text = File.ReadAllText(writer.CurrentFilePath, Encoding.UTF8);

        Assert.Contains("清理完成 · 用时 1.2s", text);
    }

    [Fact]
    public void 日志目录不存在时会自动创建()
    {
        using var temp = new TempDirectory();
        var nested = Path.Combine(temp.Path, "a", "b", "logs");
        var writer = new LogFileWriter(nested);

        writer.Write(Entry("hello"));
        writer.Dispose();

        Assert.True(Directory.Exists(nested));
        Assert.True(File.Exists(writer.CurrentFilePath));
    }

    [Fact]
    public void WriteSystem_标记为启动器来源()
    {
        using var temp = new TempDirectory();
        var writer = new LogFileWriter(temp.Path);

        writer.WriteSystem("===== 会话开始 =====");
        writer.Dispose();

        var text = File.ReadAllText(writer.CurrentFilePath, Encoding.UTF8);

        Assert.Contains("[launcher]", text);
        Assert.Contains("SYS", text);
        Assert.Contains("===== 会话开始 =====", text);
    }

    [Fact]
    public void ClearAll_删除全部日志文件并保留其它文件()
    {
        using var temp = new TempDirectory();
        var writer = new LogFileWriter(temp.Path);
        writer.Write(Entry("x"));
        writer.Dispose();

        File.WriteAllText(Path.Combine(temp.Path, "keep-me.txt"), "unrelated");

        var removed = LogFileWriter.ClearAll(temp.Path);

        Assert.Equal(1, removed);
        Assert.Empty(Directory.GetFiles(temp.Path, "DshLauncher-*.log"));
        Assert.True(File.Exists(Path.Combine(temp.Path, "keep-me.txt")));
    }

    [Fact]
    public void ClearAll_对不存在的目录返回零()
    {
        Assert.Equal(0, LogFileWriter.ClearAll(@"Z:\definitely-not-here"));
    }

    [Fact]
    public void 默认目录在_AppData_下()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DshLauncher",
            "logs");

        Assert.Equal(expected, LogFileWriter.DefaultLogDirectory);
    }

    [Fact]
    public void 同一秒内建两个写入器不会互相覆盖()
    {
        using var temp = new TempDirectory();
        var first = new LogFileWriter(temp.Path);
        var second = new LogFileWriter(temp.Path);

        Assert.NotEqual(first.CurrentFilePath, second.CurrentFilePath);

        first.Dispose();
        second.Dispose();
    }
}

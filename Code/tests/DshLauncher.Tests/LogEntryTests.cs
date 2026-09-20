using DshLauncher.Models;
using Xunit;

namespace DshLauncher.Tests;

public sealed class LogEntryTests
{
    [Theory]
    [InlineData(LogLevel.Info, "INFO")]
    [InlineData(LogLevel.Warn, "WARN")]
    [InlineData(LogLevel.Error, "ERROR")]
    [InlineData(LogLevel.System, "SYS")]
    public void LevelText_映射到短标签(LogLevel level, string expected)
    {
        Assert.Equal(expected, new LogEntry { Level = level }.LevelText);
    }

    [Fact]
    public void TimeText_是时分秒()
    {
        var entry = new LogEntry { Time = new DateTime(2024, 3, 1, 9, 5, 7) };

        Assert.Equal("09:05:07", entry.TimeText);
    }

    [Fact]
    public void SourceText_带方括号()
    {
        Assert.Equal("[build]", new LogEntry { Source = "build" }.SourceText);
    }

    [Fact]
    public void 默认来源是启动器自身()
    {
        Assert.Equal("launcher", new LogEntry().Source);
    }
}

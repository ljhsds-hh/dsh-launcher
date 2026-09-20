using DshLauncher.Services;
using Xunit;

namespace DshLauncher.Tests;

public sealed class AnsiStripperTests
{
    private const string Esc = "\u001b";

    [Fact]
    public void 去掉颜色转义()
    {
        Assert.Equal("[PLUGIN_TIMINGS] hello", AnsiStripper.Strip($"{Esc}[33m[PLUGIN_TIMINGS] {Esc}[0mhello"));
    }

    [Fact]
    public void 去掉真实构建日志里的反色_WARN()
    {
        var input = $"{Esc}[43m{Esc}[30m WARN {Esc}[39m{Esc}[49m {Esc}[36minlineDynamicImports{Esc}[39m option is deprecated";

        Assert.Equal(" WARN  inlineDynamicImports option is deprecated", AnsiStripper.Strip(input));
    }

    [Fact]
    public void 去掉光标控制与清行序列()
    {
        Assert.Equal("progress 100%", AnsiStripper.Strip($"{Esc}[2Kprogress 100%{Esc}[1G"));
    }

    [Fact]
    public void 去掉_OSC_序列()
    {
        Assert.Equal("title", AnsiStripper.Strip($"{Esc}]0;window title\u0007title"));
    }

    [Fact]
    public void 普通文本原样返回()
    {
        Assert.Equal("clean: removed 284 paths", AnsiStripper.Strip("clean: removed 284 paths"));
    }

    [Fact]
    public void 中文原样保留()
    {
        Assert.Equal("清理完成 · 用时 1.2s", AnsiStripper.Strip("清理完成 · 用时 1.2s"));
    }

    [Fact]
    public void 去掉行尾回车与空白()
    {
        Assert.Equal("done", AnsiStripper.Strip("done\r\n"));
        Assert.Equal("done", AnsiStripper.Strip("done   "));
    }

    [Fact]
    public void 保留行首缩进()
    {
        Assert.Equal("    - tsdown:deps (52%)", AnsiStripper.Strip("    - tsdown:deps (52%)"));
    }

    [Fact]
    public void 去掉制表符以外的控制字符_保留制表符()
    {
        Assert.Equal("a\tb", AnsiStripper.Strip("a\tb"));
        Assert.Equal("ab", AnsiStripper.Strip("a\u0008b"));
    }

    [Fact]
    public void 纯转义序列会变成空串()
    {
        Assert.Equal(string.Empty, AnsiStripper.Strip($"{Esc}[0m"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 空输入返回空串(string? input)
    {
        Assert.Equal(string.Empty, AnsiStripper.Strip(input));
    }
}

using DshLauncher.Models;
using DshLauncher.Services;
using Xunit;

namespace DshLauncher.Tests;

public sealed class LogClassifierTests
{
    [Theory]
    [InlineData("hello world")]
    [InlineData("tsc 输出")]
    [InlineData("   ")]
    [InlineData("")]
    public void 普通输出是_Info(string line)
    {
        Assert.Equal(LogLevel.Info, LogClassifier.Classify(line));
    }

    // ── 回归：真实构建日志里的这些行曾被误判为 ERROR（一次构建 593 行假错误）──

    [Theory]
    [InlineData("$ tsx scripts/build.ts")]
    [InlineData("$ pnpm run build:lib:host && pnpm run build:lib:client")]
    [InlineData("$ node --max-old-space-size=4096 ./node_modules/typescript/bin/tsc -b tsconfig.host.json")]
    [InlineData("[PLUGIN_TIMINGS] Your build spent significant time in plugin `tsdown:deps`. See https://rolldown.rs/options/checks#plugintimings")]
    [InlineData("[tavily-mcp] ready. API key loaded")]
    [InlineData("Context7 Documentation MCP Server v4.1.1 running on stdio")]
    [InlineData("clean: removed 284 paths")]
    [InlineData("dsh web: http://127.0.0.1:3080/?token=abc")]
    public void 构建与启动的常规输出是_Info(string line)
    {
        Assert.Equal(LogLevel.Info, LogClassifier.Classify(line));
    }

    [Theory]
    [InlineData("src/a.ts(3,1): error TS2304: Cannot find name 'x'.")]
    [InlineData("error: cannot find module 'foo'")]
    [InlineData("npm ERR! code ELIFECYCLE")]
    [InlineData("ERROR: something broke")]
    [InlineData("Error: listen EADDRINUSE: address already in use 127.0.0.1:3080")]
    [InlineData("spawn cmd.exe ENOENT")]
    [InlineData("Exception in thread main")]
    [InlineData("构建失败")]
    [InlineData("发生错误")]
    public void 真错误判为_Error(string line)
    {
        Assert.Equal(LogLevel.Error, LogClassifier.Classify(line));
    }

    [Theory]
    [InlineData(" WARN  `noExternal` is deprecated. Use `deps.alwaysBundle` instead.")]
    [InlineData(" WARN  inlineDynamicImports option is deprecated, please use codeSplitting: false")]
    [InlineData("native/system/packages/linux-x64 | [WARN] Unsupported platform")]
    [InlineData("警告：端口被占用")]
    public void 警告判为_Warn(string line)
    {
        Assert.Equal(LogLevel.Warn, LogClassifier.Classify(line));
    }

    [Fact]
    public void 错误标记优先于警告标记()
    {
        Assert.Equal(LogLevel.Error, LogClassifier.Classify("WARN then Error: boom"));
    }

    [Fact]
    public void 小写的_errors_统计行不再误报()
    {
        // 「0 errors」这类统计行不该标红；大写整词的 ERROR 才是真错误。
        Assert.Equal(LogLevel.Info, LogClassifier.Classify("0 errors"));
        Assert.Equal(LogLevel.Error, LogClassifier.Classify("ERROR"));
    }

    [Fact]
    public void 大小写不敏感的具体错误片段()
    {
        Assert.Equal(LogLevel.Error, LogClassifier.Classify("erRoR: boom"));
        Assert.Equal(LogLevel.Warn, LogClassifier.Classify("WaRnInG happened"));
    }
}

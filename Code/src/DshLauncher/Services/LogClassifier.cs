using System.Text.RegularExpressions;
using DshLauncher.Models;

namespace DshLauncher.Services;

/// <summary>
/// 按内容判定日志级别。
/// </summary>
/// <remarks>
/// <b>不再按输出流判定。</b>早期版本把「来自标准错误」直接当成错误，结果一次正常构建
/// 刷出 593 行假 ERROR：pnpm 的脚本横幅、rolldown 的 <c>[PLUGIN_TIMINGS]</c> 提示、
/// MCP 服务器的 ready 消息、以及大量空行全都是走 stderr 的。
/// 现在只认内容，真正的成败由退出码表达。
/// </remarks>
public static partial class LogClassifier
{
    /// <summary>明确表示出错的片段。</summary>
    [GeneratedRegex(
        @"error\s+TS\d+|error:|\bERR!|ELIFECYCLE|ENOENT|EADDRINUSE|EACCES|EPERM|Cannot find|Module not found|Unhandled|Exception|Traceback|失败|错误",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ErrorMarker();

    /// <summary>整词大写的 ERROR —— 工具打印真错误时的惯用写法。</summary>
    [GeneratedRegex(@"\bERROR\b", RegexOptions.Compiled)]
    private static partial Regex UpperCaseError();

    /// <summary>警告。</summary>
    [GeneratedRegex(@"\bWARN(ING)?\b|警告|deprecat", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex WarningMarker();

    /// <param name="line">已去掉 ANSI 转义的一行输出。</param>
    public static LogLevel Classify(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return LogLevel.Info;

        if (ErrorMarker().IsMatch(line) || UpperCaseError().IsMatch(line)) return LogLevel.Error;
        if (WarningMarker().IsMatch(line)) return LogLevel.Warn;

        return LogLevel.Info;
    }
}

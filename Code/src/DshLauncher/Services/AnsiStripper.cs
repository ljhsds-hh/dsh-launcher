using System.Text.RegularExpressions;

namespace DshLauncher.Services;

/// <summary>
/// 去掉命令行输出里的 ANSI 转义序列。
/// </summary>
/// <remarks>
/// pnpm / vite / rolldown / tsdown / 各家 MCP 服务器都会往输出里塞颜色与光标控制码，
/// 直接落盘会变成一堆 <c>[33m</c>、<c>[43m[30m</c> 之类的垃圾，界面和日志都没法看。
/// </remarks>
public static partial class AnsiStripper
{
    /// <summary>CSI（<c>ESC [ … 字母</c>）、OSC（<c>ESC ] … BEL</c>）与两位转义。</summary>
    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\)|[@-Z\\_-])", RegexOptions.Compiled)]
    private static partial Regex EscapeSequence();

    /// <summary>其余 C0/DEL 控制字符（保留 \t）。</summary>
    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled)]
    private static partial Regex ControlCharacter();

    public static string Strip(string? line)
    {
        if (string.IsNullOrEmpty(line)) return string.Empty;

        var text = EscapeSequence().Replace(line, string.Empty);
        text = ControlCharacter().Replace(text, string.Empty);

        // 行尾的空白没有意义（进度刷新会留下一堆）。
        return text.TrimEnd();
    }
}

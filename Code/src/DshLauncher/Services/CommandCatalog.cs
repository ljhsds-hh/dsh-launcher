using DshLauncher.Models;

namespace DshLauncher.Services;

/// <summary>
/// 默认命令与步骤定义。
/// 命令对应 DeepSeek Harness 仓库 package.json 中真实存在的脚本：
/// <c>clean</c> → tsx scripts/clean.ts，<c>build</c> → tsx scripts/build.ts，
/// <c>dsh web</c> → node --import tsx/esm apps/cli/src/bin.ts web。
/// </summary>
public static class CommandCatalog
{
    public const string CleanStepId = "clean";
    public const string BuildStepId = "build";
    public const string StartStepId = "start";

    /// <summary>「一键执行」的顺序：清理 → 构建 → 启动。</summary>
    public static IReadOnlyList<string> PipelineOrder { get; } = new[] { CleanStepId, BuildStepId, StartStepId };

    public static List<LaunchStep> CreateDefaultSteps() => new()
    {
        new LaunchStep
        {
            Id = CleanStepId,
            Name = "清理",
            Command = "pnpm run clean",
            LongRunning = false,
        },
        new LaunchStep
        {
            Id = BuildStepId,
            Name = "构建",
            Command = "pnpm run build",
            LongRunning = false,
        },
        new LaunchStep
        {
            Id = StartStepId,
            Name = "启动",
            Command = "pnpm dsh web",
            LongRunning = true,
        },
    };

    /// <summary>
    /// 用默认步骤补齐缺失项，同时保留用户已经改过的命令，
    /// 这样启动器升级不会覆盖用户自定义的命令行。
    /// </summary>
    public static List<LaunchStep> MergeWithDefaults(List<LaunchStep>? existing)
    {
        var defaults = CreateDefaultSteps();
        if (existing is null || existing.Count == 0) return defaults;

        foreach (var fallback in defaults)
        {
            var match = existing.FirstOrDefault(
                step => string.Equals(step.Id, fallback.Id, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                existing.Add(fallback);
                continue;
            }

            if (string.IsNullOrWhiteSpace(match.Name)) match.Name = fallback.Name;
            if (string.IsNullOrWhiteSpace(match.Command)) match.Command = fallback.Command;
        }

        return existing;
    }
}

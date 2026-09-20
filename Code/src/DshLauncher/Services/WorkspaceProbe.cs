using System.IO;

namespace DshLauncher.Services;

/// <summary>判断一个目录是否是一份可用的 DeepSeek Harness 源码仓库。</summary>
public static class WorkspaceProbe
{
    /// <summary>常见候选目录，用于首次启动时自动填充。</summary>
    private static readonly string[] Candidates =
    {
        @"D:\Work\DeepSeekDesktop",
    };

    public static bool LooksLikeDshRepository(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;

        return File.Exists(Path.Combine(path, "package.json"))
            && File.Exists(Path.Combine(path, "apps", "cli", "src", "bin.ts"));
    }

    /// <summary>校验结果说明，用于日志与状态提示。</summary>
    public static string Describe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "尚未选择目录";
        if (!Directory.Exists(path)) return "目录不存在";

        var hasPackageJson = File.Exists(Path.Combine(path, "package.json"));
        var hasCliEntry = File.Exists(Path.Combine(path, "apps", "cli", "src", "bin.ts"));

        if (hasPackageJson && hasCliEntry) return "已识别为 DeepSeek Harness 仓库";

        var missing = new List<string>();
        if (!hasPackageJson) missing.Add("package.json");
        if (!hasCliEntry) missing.Add(@"apps\cli\src\bin.ts");

        return $"目录不像 DSH 仓库，缺少：{string.Join("、", missing)}";
    }

    public static string? FindDefaultWorkspace()
    {
        foreach (var candidate in Candidates)
        {
            if (LooksLikeDshRepository(candidate)) return candidate;
        }

        return null;
    }
}

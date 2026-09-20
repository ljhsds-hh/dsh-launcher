using System.IO;
using DshLauncher.Services;
using Xunit;

namespace DshLauncher.Tests;

public sealed class WorkspaceProbeTests
{
    [Fact]
    public void 空路径与不存在的目录都判定为否()
    {
        Assert.False(WorkspaceProbe.LooksLikeDshRepository(null));
        Assert.False(WorkspaceProbe.LooksLikeDshRepository("   "));
        Assert.False(WorkspaceProbe.LooksLikeDshRepository(@"Z:\definitely-not-here"));
    }

    [Fact]
    public void 完整的仓库结构判定为是()
    {
        using var temp = new TempDirectory();
        var repo = temp.CreateFakeRepository();

        Assert.True(WorkspaceProbe.LooksLikeDshRepository(repo));
    }

    [Fact]
    public void 缺少入口文件判定为否()
    {
        using var temp = new TempDirectory();
        var repo = temp.CreateFakeRepository();
        File.Delete(Path.Combine(repo, "apps", "cli", "src", "bin.ts"));

        Assert.False(WorkspaceProbe.LooksLikeDshRepository(repo));
    }

    [Fact]
    public void 缺少_package_json_判定为否()
    {
        using var temp = new TempDirectory();
        var repo = temp.CreateFakeRepository();
        File.Delete(Path.Combine(repo, "package.json"));

        Assert.False(WorkspaceProbe.LooksLikeDshRepository(repo));
    }

    [Fact]
    public void 描述能指出缺失的文件()
    {
        using var temp = new TempDirectory();
        var repo = temp.CreateFakeRepository();
        File.Delete(Path.Combine(repo, "apps", "cli", "src", "bin.ts"));

        var description = WorkspaceProbe.Describe(repo);

        Assert.Contains("bin.ts", description);
    }

    [Fact]
    public void 描述对未选择目录给出提示()
    {
        Assert.Contains("尚未选择", WorkspaceProbe.Describe(null));
        Assert.Contains("尚未选择", WorkspaceProbe.Describe("   "));
    }

    [Fact]
    public void 描述对不存在的目录给出提示()
    {
        Assert.Contains("不存在", WorkspaceProbe.Describe(@"Z:\definitely-not-here"));
    }

    [Fact]
    public void 描述对合法仓库给出确认()
    {
        using var temp = new TempDirectory();
        var repo = temp.CreateFakeRepository();

        Assert.Contains("DeepSeek Harness", WorkspaceProbe.Describe(repo));
    }
}

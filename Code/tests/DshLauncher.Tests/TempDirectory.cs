using System.IO;

namespace DshLauncher.Tests;

/// <summary>测试用的临时目录，Dispose 时递归删除。</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "dshlauncher-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>在临时目录里造一份「像 DSH 仓库」的目录结构。</summary>
    public string CreateFakeRepository(string name = "repo")
    {
        var root = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.Combine(root, "apps", "cli", "src"));
        File.WriteAllText(System.IO.Path.Combine(root, "package.json"), "{\"name\":\"fake-dsh\"}");
        File.WriteAllText(System.IO.Path.Combine(root, "apps", "cli", "src", "bin.ts"), "// fake");
        return root;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响测试结论。
        }
    }
}

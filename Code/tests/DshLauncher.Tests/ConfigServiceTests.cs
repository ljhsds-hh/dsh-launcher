using System.IO;
using DshLauncher.Models;
using DshLauncher.Services;
using Xunit;

namespace DshLauncher.Tests;

public sealed class ConfigServiceTests
{
    [Fact]
    public void 保存后读回保留自定义命令与目录()
    {
        using var temp = new TempDirectory();
        var config = new AppConfig { WorkspacePath = @"D:\Work\DeepSeekDesktop" };
        config.Steps = CommandCatalog.CreateDefaultSteps();
        config.Steps.First(step => step.Id == "clean").Command = "pnpm run clean --force";

        new ConfigService(temp.Path).Save(config);
        var loaded = new ConfigService(temp.Path).Load();

        Assert.Equal(@"D:\Work\DeepSeekDesktop", loaded.WorkspacePath);
        Assert.Equal("pnpm run clean --force", loaded.Steps.First(step => step.Id == "clean").Command);
    }

    [Fact]
    public void 保存为可读_JSON_中文不转义()
    {
        using var temp = new TempDirectory();
        var config = new AppConfig { WorkspacePath = @"D:\test" };
        config.Steps = CommandCatalog.CreateDefaultSteps();
        new ConfigService(temp.Path).Save(config);

        var text = File.ReadAllText(Path.Combine(temp.Path, "config.json"), System.Text.Encoding.UTF8);

        Assert.Contains("清理", text);
        Assert.DoesNotContain("\\u", text);
    }

    [Fact]
    public void 配置文件不存在时返回默认值()
    {
        using var temp = new TempDirectory();

        var loaded = new ConfigService(temp.Path).Load();

        Assert.Equal(3, loaded.Steps.Count);
        Assert.Equal(3080, loaded.ServerPort);
    }

    // 配置原本是懒创建的，第一次用的人点「配置文件」会看到空目录 —— 所以启动时要先落一份。
    [Fact]
    public void 首次启动会把默认配置落盘()
    {
        using var temp = new TempDirectory();
        var service = new ConfigService(temp.Path);
        var config = service.Load();

        Assert.False(File.Exists(service.FilePath));

        var created = service.SaveIfMissing(config);

        Assert.True(created);
        Assert.True(File.Exists(service.FilePath));
        Assert.Contains("steps", File.ReadAllText(service.FilePath, System.Text.Encoding.UTF8));
    }

    [Fact]
    public void 已有配置时不会覆盖用户改过的内容()
    {
        using var temp = new TempDirectory();
        var service = new ConfigService(temp.Path);
        service.Save(new AppConfig { WorkspacePath = @"D:\keep-me" });

        var loaded = service.Load();
        var created = service.SaveIfMissing(loaded);

        Assert.False(created);
        Assert.Equal(@"D:\keep-me", new ConfigService(temp.Path).Load().WorkspacePath);
    }

    [Fact]
    public void 配置目录不存在时也能落盘()
    {
        using var temp = new TempDirectory();
        var service = new ConfigService(Path.Combine(temp.Path, "nested", "DshLauncher"));

        var created = service.SaveIfMissing(new AppConfig());

        Assert.True(created);
        Assert.True(File.Exists(service.FilePath));
    }

    [Fact]
    public void 配置文件损坏时回退默认值不抛异常()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "config.json"), "{ this is not json ");

        var loaded = new ConfigService(temp.Path).Load();

        Assert.Equal(3, loaded.Steps.Count);
        Assert.Equal(3080, loaded.ServerPort);
    }

    [Fact]
    public void 归一化修正越界的端口()
    {
        var zero = new AppConfig { ServerPort = 0 };
        ConfigService.Normalize(zero);
        Assert.Equal(3080, zero.ServerPort);

        var tooBig = new AppConfig { ServerPort = 70000 };
        ConfigService.Normalize(tooBig);
        Assert.Equal(3080, tooBig.ServerPort);

        var valid = new AppConfig { ServerPort = 8080 };
        ConfigService.Normalize(valid);
        Assert.Equal(8080, valid.ServerPort);
    }

    [Fact]
    public void 归一化修正过小的日志上限()
    {
        var config = new AppConfig { MaxLogLines = 10 };
        ConfigService.Normalize(config);
        Assert.Equal(5000, config.MaxLogLines);
    }

    [Fact]
    public void 归一化修正过小的窗口尺寸()
    {
        var config = new AppConfig { WindowWidth = 100, WindowHeight = 100 };
        ConfigService.Normalize(config);

        Assert.Equal(1120, config.WindowWidth);
        Assert.Equal(760, config.WindowHeight);
    }

    [Fact]
    public void 归一化补齐空的就绪正则()
    {
        var config = new AppConfig { ReadyPattern = "   " };
        ConfigService.Normalize(config);

        Assert.False(string.IsNullOrWhiteSpace(config.ReadyPattern));
        Assert.Contains("127", config.ReadyPattern);
    }

    [Fact]
    public void 归一化把空的最近目录变成空列表()
    {
        var config = new AppConfig { RecentWorkspaces = null! };
        ConfigService.Normalize(config);

        Assert.NotNull(config.RecentWorkspaces);
        Assert.Empty(config.RecentWorkspaces);
    }

    [Fact]
    public void 归一化为空步骤补默认值()
    {
        var config = new AppConfig { Steps = new List<LaunchStep>() };
        ConfigService.Normalize(config);

        Assert.Equal(3, config.Steps.Count);
    }

    [Fact]
    public void 默认端口是_3080()
    {
        Assert.Equal(3080, new AppConfig().ServerPort);
    }
}

using DshLauncher.Models;
using DshLauncher.Services;
using DshLauncher.ViewModels;
using Xunit;

namespace DshLauncher.Tests;

public sealed class SettingsViewModelTests
{
    private static AppConfig CreateConfig()
    {
        var config = new AppConfig();
        config.Steps = CommandCatalog.CreateDefaultSteps();
        return config;
    }

    [Fact]
    public void 构造时从配置读入当前值()
    {
        var config = CreateConfig();
        config.ServerPort = 8080;
        config.AutoScroll = false;
        config.MaxLogLines = 1234;
        config.OpenBrowserWhenReady = true;

        var settings = new SettingsViewModel(config);

        Assert.Equal(8080, settings.ServerPort);
        Assert.False(settings.AutoScroll);
        Assert.Equal(1234, settings.MaxLogLines);
        Assert.True(settings.OpenBrowserWhenReady);
        Assert.Equal("pnpm run clean", settings.CleanCommand);
        Assert.Equal("pnpm run build", settings.BuildCommand);
        Assert.Equal("pnpm dsh web", settings.StartCommand);
    }

    [Fact]
    public void 应用设置会写回三条命令()
    {
        var config = CreateConfig();
        var settings = new SettingsViewModel(config)
        {
            CleanCommand = "clean-x",
            BuildCommand = "build-x",
            StartCommand = "start-x",
        };

        settings.ApplyTo(config);

        Assert.Equal("clean-x", config.Steps.First(step => step.Id == "clean").Command);
        Assert.Equal("build-x", config.Steps.First(step => step.Id == "build").Command);
        Assert.Equal("start-x", config.Steps.First(step => step.Id == "start").Command);
    }

    [Fact]
    public void 空白命令不会覆盖原命令()
    {
        var config = CreateConfig();
        var settings = new SettingsViewModel(config)
        {
            CleanCommand = "   ",
            BuildCommand = string.Empty,
        };

        settings.ApplyTo(config);

        Assert.Equal("pnpm run clean", config.Steps.First(step => step.Id == "clean").Command);
        Assert.Equal("pnpm run build", config.Steps.First(step => step.Id == "build").Command);
    }

    [Fact]
    public void 命令两端空白会被裁掉()
    {
        var config = CreateConfig();
        var settings = new SettingsViewModel(config) { CleanCommand = "  pnpm run clean --force  " };

        settings.ApplyTo(config);

        Assert.Equal("pnpm run clean --force", config.Steps.First(step => step.Id == "clean").Command);
    }

    [Fact]
    public void 非法端口回落_3080()
    {
        var config = CreateConfig();
        var settings = new SettingsViewModel(config) { ServerPort = 0 };
        settings.ApplyTo(config);
        Assert.Equal(3080, config.ServerPort);

        var settings2 = new SettingsViewModel(config) { ServerPort = 99999 };
        settings2.ApplyTo(config);
        Assert.Equal(3080, config.ServerPort);
    }

    [Fact]
    public void 空白就绪正则回落默认值()
    {
        var config = CreateConfig();
        var settings = new SettingsViewModel(config) { ReadyPattern = "  " };

        settings.ApplyTo(config);

        Assert.False(string.IsNullOrWhiteSpace(config.ReadyPattern));
        Assert.Contains("127", config.ReadyPattern);
    }

    [Fact]
    public void 日志上限被夹到至少_200()
    {
        var config = CreateConfig();
        var settings = new SettingsViewModel(config) { MaxLogLines = 5 };

        settings.ApplyTo(config);

        Assert.Equal(200, config.MaxLogLines);
    }

    [Fact]
    public void 应用设置会同步开关项()
    {
        var config = CreateConfig();
        var settings = new SettingsViewModel(config)
        {
            AutoScroll = false,
            OpenBrowserWhenReady = true,
        };

        settings.ApplyTo(config);

        Assert.False(config.AutoScroll);
        Assert.True(config.OpenBrowserWhenReady);
    }

    [Fact]
    public void 端口占用放行开关默认关闭并会写回()
    {
        var config = CreateConfig();
        Assert.False(config.AllowStartWhenPortBusy);

        var settings = new SettingsViewModel(config);
        Assert.False(settings.AllowStartWhenPortBusy);

        settings.AllowStartWhenPortBusy = true;
        settings.ApplyTo(config);

        Assert.True(config.AllowStartWhenPortBusy);
    }

    [Fact]
    public void 配置路径指向配置文件本身()
    {
        var settings = new SettingsViewModel(CreateConfig());

        Assert.EndsWith("config.json", settings.ConfigFilePath);
    }
}

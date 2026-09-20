using DshLauncher.Models;
using DshLauncher.Services;
using Xunit;

namespace DshLauncher.Tests;

public sealed class CommandCatalogTests
{
    [Fact]
    public void CreateDefaultSteps_返回清理构建启动三步()
    {
        var steps = CommandCatalog.CreateDefaultSteps();

        Assert.Equal(3, steps.Count);
        Assert.Equal(new[] { "clean", "build", "start" }, steps.Select(step => step.Id).ToArray());
        Assert.Equal(new[] { "清理", "构建", "启动" }, steps.Select(step => step.Name).ToArray());
    }

    [Fact]
    public void 默认命令对应_DSH_仓库真实脚本()
    {
        var steps = CommandCatalog.CreateDefaultSteps();

        Assert.Equal("pnpm run clean", steps[0].Command);
        Assert.Equal("pnpm run build", steps[1].Command);
        Assert.Equal("pnpm dsh web", steps[2].Command);
    }

    [Fact]
    public void 只有启动步骤是常驻进程()
    {
        var steps = CommandCatalog.CreateDefaultSteps();

        Assert.False(steps[0].LongRunning);
        Assert.False(steps[1].LongRunning);
        Assert.True(steps[2].LongRunning);
    }

    [Fact]
    public void 一键执行顺序是清理构建启动()
    {
        Assert.Equal(new[] { "clean", "build", "start" }, CommandCatalog.PipelineOrder.ToArray());
    }

    [Fact]
    public void 空集合补齐为默认步骤()
    {
        Assert.Equal(3, CommandCatalog.MergeWithDefaults(null).Count);
        Assert.Equal(3, CommandCatalog.MergeWithDefaults(new List<LaunchStep>()).Count);
    }

    [Fact]
    public void 合并时保留用户自定义命令()
    {
        var existing = CommandCatalog.CreateDefaultSteps();
        existing.First(step => step.Id == "build").Command = "pnpm run build:lib";

        var merged = CommandCatalog.MergeWithDefaults(existing);

        Assert.Equal("pnpm run build:lib", merged.First(step => step.Id == "build").Command);
    }

    [Fact]
    public void 合并时补回缺失的步骤()
    {
        var existing = new List<LaunchStep>
        {
            new() { Id = "clean", Name = "清理", Command = "custom-clean" },
        };

        var merged = CommandCatalog.MergeWithDefaults(existing);

        Assert.Equal(3, merged.Count);
        Assert.Contains(merged, step => step.Id == "build");
        Assert.Contains(merged, step => step.Id == "start");
    }

    [Fact]
    public void 空命令行回填默认值()
    {
        var existing = CommandCatalog.CreateDefaultSteps();
        existing.First(step => step.Id == "start").Command = "   ";

        var merged = CommandCatalog.MergeWithDefaults(existing);

        Assert.Equal("pnpm dsh web", merged.First(step => step.Id == "start").Command);
    }

    [Fact]
    public void 合并忽略_Id_大小写()
    {
        var existing = new List<LaunchStep>
        {
            new() { Id = "CLEAN", Name = "清理", Command = "my-clean" },
        };

        var merged = CommandCatalog.MergeWithDefaults(existing);

        Assert.Equal("my-clean", merged.First(step => step.Id == "CLEAN").Command);
    }

    [Fact]
    public void Clone_是深拷贝()
    {
        var step = new LaunchStep { Id = "clean", Name = "清理", Command = "pnpm run clean" };

        var clone = step.Clone();
        clone.Command = "changed";

        Assert.Equal("pnpm run clean", step.Command);
        Assert.Equal(step.Id, clone.Id);
    }
}

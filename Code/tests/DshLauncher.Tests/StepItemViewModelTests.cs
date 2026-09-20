using System.ComponentModel;
using DshLauncher.Models;
using DshLauncher.ViewModels;
using Xunit;

namespace DshLauncher.Tests;

public sealed class StepItemViewModelTests
{
    private static LaunchStep CleanStep() => new()
    {
        Id = "clean",
        Name = "清理",
        Command = "pnpm run clean",
    };

    [Fact]
    public void 初始状态是待执行且只显示名称()
    {
        var step = new StepItemViewModel(CleanStep());

        Assert.Equal(StepState.Waiting, step.State);
        Assert.Equal("清理", step.DisplayText);
        Assert.Equal("pnpm run clean", step.Command);
    }

    [Theory]
    [InlineData(StepState.Running, "清理 · 执行中")]
    [InlineData(StepState.Failed, "清理 · 失败")]
    [InlineData(StepState.Cancelled, "清理 · 已中止")]
    public void 各状态映射到步骤条文案(StepState state, string expected)
    {
        var step = new StepItemViewModel(CleanStep()) { State = state };

        Assert.Equal(expected, step.DisplayText);
    }

    [Fact]
    public void 成功但没有耗时不显示时间()
    {
        var step = new StepItemViewModel(CleanStep()) { State = StepState.Succeeded };

        Assert.Equal("清理 · 完成", step.DisplayText);
    }

    [Fact]
    public void 成功并带上耗时()
    {
        var step = new StepItemViewModel(CleanStep())
        {
            State = StepState.Succeeded,
            Elapsed = "3.2s",
        };

        Assert.Equal("清理 · 完成 3.2s", step.DisplayText);
    }

    [Fact]
    public void 状态变化会通知_DisplayText()
    {
        var step = new StepItemViewModel(CleanStep());
        var raised = new List<string?>();
        step.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        step.State = StepState.Running;

        Assert.Contains(nameof(StepItemViewModel.DisplayText), raised);
    }

    [Fact]
    public void 耗时变化会通知_DisplayText()
    {
        var step = new StepItemViewModel(CleanStep()) { State = StepState.Succeeded };
        var raised = new List<string?>();
        step.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        step.Elapsed = "1.0s";

        Assert.Contains(nameof(StepItemViewModel.DisplayText), raised);
    }

    [Fact]
    public void Reset_回到待执行()
    {
        var step = new StepItemViewModel(CleanStep())
        {
            State = StepState.Failed,
            Elapsed = "9.9s",
        };

        step.Reset();

        Assert.Equal(StepState.Waiting, step.State);
        Assert.Equal(string.Empty, step.Elapsed);
        Assert.Equal("清理", step.DisplayText);
    }

    [Fact]
    public void RefreshFromModel_跟随模型命令变化()
    {
        var model = CleanStep();
        var step = new StepItemViewModel(model);
        var raised = new List<string?>();
        step.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        model.Command = "pnpm run clean --force";
        step.RefreshFromModel();

        Assert.Equal("pnpm run clean --force", step.Command);
        Assert.Contains(nameof(StepItemViewModel.Command), raised);
    }

    [Fact]
    public void 实现_INotifyPropertyChanged()
    {
        Assert.IsAssignableFrom<INotifyPropertyChanged>(new StepItemViewModel(CleanStep()));
    }

    [Theory]
    [InlineData(StepState.Waiting, "等待中")]
    [InlineData(StepState.Running, "执行中")]
    [InlineData(StepState.Succeeded, "完成")]
    [InlineData(StepState.Failed, "失败")]
    [InlineData(StepState.Cancelled, "已中止")]
    public void 状态短文案不带步骤名_避免和卡片上的名字重复(StepState state, string expected)
    {
        var step = new StepItemViewModel(CleanStep()) { State = state };

        Assert.Equal(expected, step.StateText);
        Assert.DoesNotContain("清理", step.StateText);
    }

    [Fact]
    public void 状态变化会通知_StateText()
    {
        var step = new StepItemViewModel(CleanStep());
        var raised = new List<string?>();
        step.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        step.State = StepState.Running;

        Assert.Contains(nameof(StepItemViewModel.StateText), raised);
    }

    [Fact]
    public void 序号补零成两位_给流程卡片用()
    {
        var step = new StepItemViewModel(CleanStep()) { Index = 2 };

        Assert.Equal("02", step.IndexText);
    }
}

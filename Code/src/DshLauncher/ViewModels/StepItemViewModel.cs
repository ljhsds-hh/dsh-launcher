using CommunityToolkit.Mvvm.ComponentModel;
using DshLauncher.Models;

namespace DshLauncher.ViewModels;

/// <summary>界面上的一个步骤（清理 / 构建 / 启动）。</summary>
public sealed partial class StepItemViewModel : ObservableObject
{
    public StepItemViewModel(LaunchStep model) => Model = model;

    public LaunchStep Model { get; }

    public string Id => Model.Id;

    public string Name => Model.Name;

    public string Command => Model.Command;

    /// <summary>在流程里的位置（1 起），由 <c>MainViewModel</c> 按真实执行顺序写入。</summary>
    public int Index { get; set; }

    /// <summary>两位序号，流程卡片左上角那个「01 / 02 / 03」。</summary>
    public string IndexText => Index.ToString("D2");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private StepState _state = StepState.Waiting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private string _elapsed = string.Empty;

    /// <summary>
    /// 只描述状态的短文案。卡片上「名字」和「状态」分两处显示，
    /// 所以这里不能像 <see cref="DisplayText"/> 那样把名字也带上，否则会重复。
    /// </summary>
    public string StateText => State switch
    {
        StepState.Running => "执行中",
        StepState.Succeeded => "完成",
        StepState.Failed => "失败",
        StepState.Cancelled => "已中止",
        _ => "等待中",
    };

    /// <summary>
    /// 步骤条上显示的文案。
    /// <c>StepBarItem.Status</c> 是只读的（由 StepBar.StepIndex 推导），
    /// 所以「失败 / 已中止 / 用时」这些信息通过内容回显。
    /// </summary>
    public string DisplayText => State switch
    {
        StepState.Running => $"{Name} · 执行中",
        StepState.Succeeded => Elapsed.Length > 0 ? $"{Name} · 完成 {Elapsed}" : $"{Name} · 完成",
        StepState.Failed => $"{Name} · 失败",
        StepState.Cancelled => $"{Name} · 已中止",
        _ => Name,
    };

    public void Reset()
    {
        State = StepState.Waiting;
        Elapsed = string.Empty;
    }

    public void RefreshFromModel()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Command));
        OnPropertyChanged(nameof(DisplayText));
    }
}

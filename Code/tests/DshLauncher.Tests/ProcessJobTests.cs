using DshLauncher.Services;
using Xunit;

namespace DshLauncher.Tests;

public sealed class ProcessJobTests
{
    [Fact]
    public void 能创建_Job_并设置成功()
    {
        // TryCreate 内部依赖 JOBOBJECT_EXTENDED_LIMIT_INFORMATION 的结构体布局，
        // 布局写错会导致 SetInformationJobObject 失败、TryCreate 返回 null —— 这条就能拦住。
        using var job = ProcessJob.TryCreate(killOnClose: true);

        Assert.NotNull(job);
    }

    [Fact]
    public void 不带_KILL_ON_JOB_CLOSE_也能创建()
    {
        // 「关闭启动器保留服务」走的就是这条路：关句柄只销毁 Job，进程继续跑。
        using var job = ProcessJob.TryCreate(killOnClose: false);

        Assert.NotNull(job);
    }

    [Fact]
    public void 不结束式释放不会抛异常()
    {
        var job = ProcessJob.TryCreate(killOnClose: false);
        Assert.NotNull(job);

        job.Dispose(terminate: false);
        job.Dispose(terminate: false);
    }

    [Fact]
    public void 重复_Dispose_不会抛异常()
    {
        var job = ProcessJob.TryCreate(killOnClose: true);
        Assert.NotNull(job);

        job.Dispose();
        job.Dispose();
    }

    [Fact]
    public void 已经_Dispose_之后再_Terminate_不会抛异常()
    {
        var job = ProcessJob.TryCreate(killOnClose: true);
        Assert.NotNull(job);

        job.Dispose();
        job.Terminate();
    }

    [Fact]
    public void 未收编任何进程时_Terminate_不会抛异常()
    {
        using var job = ProcessJob.TryCreate(killOnClose: true);
        Assert.NotNull(job);

        job.Terminate();
    }

    [Fact]
    public void 处置已经结束的进程不会抛异常()
    {
        using var job = ProcessJob.TryCreate(killOnClose: true);
        Assert.NotNull(job);

        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            Arguments = "/c exit",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(process);
        process.WaitForExit();

        // 进程已退出时收编会失败，但必须安静返回 false 而不是抛。
        Assert.False(job.TryAssign(process));
    }
}

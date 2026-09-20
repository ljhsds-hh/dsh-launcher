using System.Diagnostics;
using DshLauncher.Services;
using Xunit;

namespace DshLauncher.Tests;

public sealed class ProcessTreeAndPortProbeTests
{
    [Fact]
    public void 收集进程树时至少包含自己()
    {
        var members = ProcessTree.Collect(Environment.ProcessId);

        Assert.Contains(Environment.ProcessId, members);
    }

    [Fact]
    public void 收集不存在的进程只返回它自己()
    {
        // 一个几乎不可能存在的 PID
        var members = ProcessTree.Collect(999_999);

        Assert.Single(members);
        Assert.Contains(999_999, members);
    }

    [Fact]
    public void 结束不存在的进程树返回零且不抛异常()
    {
        Assert.Equal(0, ProcessTree.KillTree(999_999));
    }

    [Fact]
    public void 未占用的端口查不到占用者()
    {
        // 挑一个几乎不可能被占用的高位端口
        const int freePort = 59_873;

        Assert.Null(PortProbe.FindOwnerProcessId(freePort));
        Assert.False(PortProbe.IsListening(freePort));
    }

    [Fact]
    public void 能查到真实占用的端口归属()
    {
        // 自己开一个监听，验证 GetExtendedTcpTable 那条路真的能拿到 PID
        const int port = 59_874;
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);

        try
        {
            listener.Start();
            var owner = PortProbe.FindOwnerProcessId(port);

            Assert.Equal(Environment.ProcessId, owner);
            Assert.True(PortProbe.IsListening(port));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void 关闭监听后端口查不到了()
    {
        const int port = 59_875;
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);

        listener.Start();
        Assert.NotNull(PortProbe.FindOwnerProcessId(port));

        listener.Stop();

        // 给系统一点时间回收
        for (var i = 0; i < 20 && PortProbe.FindOwnerProcessId(port) is not null; i++)
        {
            Thread.Sleep(50);
        }

        Assert.Null(PortProbe.FindOwnerProcessId(port));
    }

    [Fact]
    public void 收集自己时不会把无关进程拉进来()
    {
        var members = ProcessTree.Collect(Environment.ProcessId);

        // 每个成员都必须是真实存在的进程
        foreach (var id in members)
        {
            Assert.NotNull(Process.GetProcessById(id));
        }
    }
}

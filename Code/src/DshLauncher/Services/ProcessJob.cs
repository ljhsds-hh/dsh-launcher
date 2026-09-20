using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DshLauncher.Services;

/// <summary>
/// Windows Job Object 封装。
/// </summary>
/// <remarks>
/// <b>为什么不用 <c>Process.Kill(entireProcessTree: true)</c>：</b>
/// 实测在 <c>cmd → node → cmd → node</c> 这种四层树上，它只杀掉中间两层，
/// 最深的那个 node 会活下来（真机表现为「点了停止，网页照样能打开」）。
/// 原因是一次性的父进程快照走不到那么深、也扛不住父进程先死导致的重新挂载。
/// <para>
/// Job Object 是内核级的归属关系：把最外层的进程塞进 Job 之后，它派生的所有后代
/// 都自动落在同一个 Job 里，不管再派生多少层、父进程死没死，
/// 一次 <see cref="Terminate"/> 全部收干净。
/// </para>
/// <para>
/// <b>关于 <c>KILL_ON_JOB_CLOSE</c>：</b>加上它，关闭 Job 句柄（含启动器自身退出）时
/// 系统会把 Job 里的进程一起干掉——这是「关掉启动器就不留孤儿」的安全网。
/// 但用户明确要求「关掉启动器、DSH 继续跑」，所以服务类步骤按配置**不加**这个标志；
/// 不加时关闭句柄只是销毁 Job，里面的进程会继续活着（这正是我们要的）。
/// 短步骤（清理 / 构建）始终加，避免关窗时留下半拉子构建。
/// </para>
/// </remarks>
public sealed class ProcessJob : IDisposable
{
    /// <summary>JobObjectInfoClass 里的 ExtendedLimitInformation。</summary>
    private const int ExtendedLimitInformationClass = 9;

    /// <summary>句柄关闭时结束 Job 内所有进程。</summary>
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private readonly bool _killOnClose;
    private IntPtr _handle;
    private bool _disposed;

    private ProcessJob(IntPtr handle, bool killOnClose)
    {
        _handle = handle;
        _killOnClose = killOnClose;
    }

    /// <param name="killOnClose">
    /// 句柄关闭时是否连带结束 Job 内所有进程。
    /// 传 true 用于「关掉启动器就别留孤儿」，传 false 用于「关掉启动器、服务继续跑」。
    /// </param>
    /// <summary>创建 Job；底层调用失败时返回 null，调用方退回到老的杀树逻辑。</summary>
    public static ProcessJob? TryCreate(bool killOnClose)
    {
        var handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) return null;

        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    // 不加就是 0：关句柄只销毁 Job，里面的进程继续活着。
                    LimitFlags = killOnClose ? JobObjectLimitKillOnJobClose : 0u,
                },
            };

            Marshal.StructureToPtr(information, buffer, false);

            if (!SetInformationJobObject(handle, ExtendedLimitInformationClass, buffer, (uint)size))
            {
                CloseHandle(handle);
                return null;
            }
        }
        catch (Exception)
        {
            CloseHandle(handle);
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return new ProcessJob(handle, killOnClose);
    }

    /// <summary>
    /// 把进程收进 Job。<b>必须紧跟在 Start() 之后调用</b>：
    /// 进程已有的子进程不会被追溯收编，之后再派生的才会。
    /// </summary>
    public bool TryAssign(Process process)
    {
        if (_disposed || _handle == IntPtr.Zero) return false;

        try
        {
            return AssignProcessToJobObject(_handle, process.Handle);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>结束 Job 内的全部进程（含所有层级的后代）。</summary>
    public void Terminate()
    {
        if (_disposed || _handle == IntPtr.Zero) return;

        try
        {
            TerminateJobObject(_handle, 1);
        }
        catch (Exception)
        {
            // 忽略。
        }
    }

    /// <summary>默认语义：结束 Job 内所有进程后再关句柄（不留孤儿）。</summary>
    public void Dispose() => Dispose(terminate: true);

    /// <param name="terminate">
    /// true = 先结束 Job 内所有进程再关句柄（「关闭启动器也停服务」）；
    /// false = 只关句柄，里面进程继续跑（「关闭启动器、服务保留」）。
    /// 注意：若创建时带了 KILL_ON_JOB_CLOSE，关句柄无论如何都会连带结束进程。
    /// </param>
    public void Dispose(bool terminate)
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle == IntPtr.Zero) return;

        if (terminate || _killOnClose)
        {
            // 关句柄也会触发 KILL_ON_JOB_CLOSE，这里显式来一次更直接。
            Terminate();
        }
        else
        {
            // 刻意不结束：只销毁 Job，里面的 DSH 继续跑。
        }

        CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    // ------------------------------------------------------------------ 原生定义

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr job,
        int informationClass,
        IntPtr information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

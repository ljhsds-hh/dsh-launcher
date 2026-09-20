using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DshLauncher.Services;

/// <summary>
/// 结束一棵**已经存在**的进程树（不是本启动器拉起来的那种）。
/// </summary>
/// <remarks>
/// 为什么不用 <c>Process.Kill(entireProcessTree: true)</c>：实测它在多层树上会漏
/// （`cmd → node → cmd → node` 只杀掉中间两层）。这里的做法是
/// <b>先把整棵树的 PID 一次性收集完，再逐个结束</b>——
/// 父进程先死会导致子进程被重新挂载到别的父进程下，边杀边找一定会漏，
/// 所以必须「先收完名单再动手」。
/// </remarks>
public static class ProcessTree
{
    private const int ProcessQueryLimitedInformation = 0x1000;

    /// <summary>返回 root 及其全部后代的 PID（快照式，可在不结束任何进程的情况下调用）。</summary>
    public static IReadOnlyCollection<int> Collect(int rootPid)
    {
        var parents = SnapshotParents();
        var started = SnapshotStartTimes();
        var members = new HashSet<int> { rootPid };

        bool grew;
        do
        {
            grew = false;
            foreach (var (pid, parent) in parents)
            {
                if (members.Contains(pid)) continue;
                if (!members.Contains(parent)) continue;

                // 防 PID 复用：进程死掉后 PID 会被回收，新进程可能带着一条指向
                // 陌生父进程的陈旧 ParentProcessId。子进程不可能比父进程先诞生，
                // 用启动时间就能把这种假链接剔掉。
                if (!StartedAfter(pid, parent, started)) continue;

                members.Add(pid);
                grew = true;
            }
        }
        while (grew);

        return members;
    }

    private static bool StartedAfter(int child, int parent, Dictionary<int, DateTime> started)
    {
        if (!started.TryGetValue(child, out var childStart)) return true;
        if (!started.TryGetValue(parent, out var parentStart)) return true;

        return childStart >= parentStart;
    }

    private static Dictionary<int, DateTime> SnapshotStartTimes()
    {
        var map = new Dictionary<int, DateTime>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                map[process.Id] = process.StartTime;
            }
            catch (Exception)
            {
                // 读不到就开始时间未知，交给调用方按「无法判断」处理。
            }
            finally
            {
                process.Dispose();
            }
        }

        return map;
    }

    /// <summary>先收完整棵树，再由深到浅逐个结束。返回成功结束的进程数。</summary>
    public static int KillTree(int rootPid)
    {
        var parents = SnapshotParents();
        var ordered = DepthFirstOrder(rootPid, parents);

        var killed = 0;

        foreach (var pid in ordered)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
                killed++;
            }
            catch (ArgumentException)
            {
                // 已经不在了。
            }
            catch (Exception)
            {
                // 权限不足等：继续处理剩下的，最后用端口是否释放来判断结果。
            }
        }

        return killed;
    }

    /// <summary>按「离 root 越远越先结束」排序。</summary>
    private static List<int> DepthFirstOrder(int rootPid, Dictionary<int, int> parents)
    {
        var children = new Dictionary<int, List<int>>();
        foreach (var (pid, parent) in parents)
        {
            if (!children.TryGetValue(parent, out var list))
            {
                list = new List<int>();
                children[parent] = list;
            }

            list.Add(pid);
        }

        var ordered = new List<int>();
        var stack = new Stack<int>();
        stack.Push(rootPid);

        while (stack.Count > 0)
        {
            var pid = stack.Pop();
            ordered.Add(pid);

            if (!children.TryGetValue(pid, out var kids)) continue;
            foreach (var kid in kids) stack.Push(kid);
        }

        // 上面的顺序是「由浅到深」，反过来即由深到浅。
        ordered.Reverse();
        return ordered;
    }

    private static Dictionary<int, int> SnapshotParents()
    {
        var map = new Dictionary<int, int>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var parent = GetParentProcessId(process.Id);
                if (parent > 0) map[process.Id] = parent;
            }
            catch (Exception)
            {
                // 有些系统进程读不到，跳过。
            }
            finally
            {
                process.Dispose();
            }
        }

        return map;
    }

    private static int GetParentProcessId(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return -1;

        var size = Marshal.SizeOf<ProcessBasicInformation>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            var status = NtQueryInformationProcess(handle, 0, buffer, size, out _);
            if (status != 0) return -1;

            var information = Marshal.PtrToStructure<ProcessBasicInformation>(buffer);
            return information.InheritedFromUniqueProcessId.ToInt32();
        }
        catch (Exception)
        {
            return -1;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2A;
        public IntPtr Reserved2B;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int informationClass,
        IntPtr information,
        int informationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

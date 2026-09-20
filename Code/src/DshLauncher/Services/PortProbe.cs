using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DshLauncher.Services;

/// <summary>端口占用探测。</summary>
public static class PortProbe
{
    private const int AddressFamilyInterNetwork = 2;
    private const int AddressFamilyInterNetworkV6 = 23;
    private const int TcpTableOwnerPidListener = 3;
    private const uint ErrorInsufficientBuffer = 122;

    private const int Tcp4RowSize = 24;
    private const int Tcp4LocalPortOffset = 8;
    private const int Tcp4OwningPidOffset = 20;

    private const int Tcp6RowSize = 56;
    private const int Tcp6LocalPortOffset = 20;
    private const int Tcp6OwningPidOffset = 52;

    public static bool IsListening(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == port);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 找到正在监听该端口的进程 PID；没有占用时返回 null。
    /// 用 <c>GetExtendedTcpTable</c> 直接向 IP Helper 要，不依赖 WMI，也不起子进程。
    /// </summary>
    public static int? FindOwnerProcessId(int port)
    {
        return Query(port, AddressFamilyInterNetwork, Tcp4RowSize, Tcp4LocalPortOffset, Tcp4OwningPidOffset)
               ?? Query(port, AddressFamilyInterNetworkV6, Tcp6RowSize, Tcp6LocalPortOffset, Tcp6OwningPidOffset);
    }

    private static int? Query(int port, int addressFamily, int rowSize, int portOffset, int pidOffset)
    {
        var size = 0;

        var result = GetExtendedTcpTable(
            IntPtr.Zero, ref size, false, addressFamily, TcpTableOwnerPidListener, 0);

        if (result != ErrorInsufficientBuffer || size <= 0) return null;

        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            result = GetExtendedTcpTable(
                buffer, ref size, false, addressFamily, TcpTableOwnerPidListener, 0);

            if (result != 0) return null;

            var count = Marshal.ReadInt32(buffer);
            var row = IntPtr.Add(buffer, 4);

            for (var i = 0; i < count; i++, row = IntPtr.Add(row, rowSize))
            {
                var rawPort = Marshal.ReadInt32(row, portOffset);
                var localPort = (ushort)IPAddress.NetworkToHostOrder((short)rawPort);

                if (localPort != port) continue;

                var owningPid = Marshal.ReadInt32(row, pidOffset);
                if (owningPid > 0) return owningPid;
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        bool order,
        int addressFamily,
        int tableClass,
        int reserved);
}

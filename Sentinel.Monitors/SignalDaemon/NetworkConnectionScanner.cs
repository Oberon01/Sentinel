using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Sentinel.Monitors.SignalDaemon;

public sealed class NetworkConnectionScanner
{
    private readonly ILogger<NetworkConnectionScanner> _logger;
    private readonly Dictionary<int, string> _pidToName = new();
    private DateTimeOffset _pidCacheTime = DateTimeOffset.MinValue;
    private static readonly TimeSpan PidCacheTtl = TimeSpan.FromSeconds(5);

    public NetworkConnectionScanner(ILogger<NetworkConnectionScanner> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<NetworkConnectionInfo> GetConnections(bool onlyEstablished = true)
    {
        RefreshPidCache();
        var result = new List<NetworkConnectionInfo>();

        int bufLen = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufLen, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL);

        var buf = Marshal.AllocHGlobal(bufLen);
        try
        {
            if (GetExtendedTcpTable(buf, ref bufLen, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL) != 0)
                return result;

            int numRows = Marshal.ReadInt32(buf);
            int offset = sizeof(int);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < numRows; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(buf + offset + i * rowSize);

                if (row.RemoteAddr == 0) continue;
                if (onlyEstablished && row.State != TcpEstablished) continue;

                var localIp = new IPAddress(row.LocalAddr).ToString();
                var remoteIp = new IPAddress(row.RemoteAddr).ToString();
                // Ports are stored in network byte order (big-endian) in the lower 16 bits
                int localPort = (int)(((row.LocalPort & 0xFF) << 8) | (row.LocalPort >> 8));
                int remotePort = (int)(((row.RemotePort & 0xFF) << 8) | (row.RemotePort >> 8));

                var pid = (int)row.OwningPid;
                _pidToName.TryGetValue(pid, out var name);

                result.Add(new NetworkConnectionInfo(
                    localIp, localPort, remoteIp, remotePort,
                    row.State == TcpEstablished, pid, name ?? ""));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate TCP connections");
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        return result;
    }

    private void RefreshPidCache()
    {
        if (DateTimeOffset.UtcNow - _pidCacheTime < PidCacheTtl) return;
        _pidToName.Clear();
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try { _pidToName[p.Id] = p.ProcessName; }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate processes");
        }
        _pidCacheTime = DateTimeOffset.UtcNow;
    }

    private const uint TcpEstablished = 5;
    private const int AF_INET = 2;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int dwOutBufLen, bool sort,
        int ipVersion, TCP_TABLE_CLASS tableClass, uint reserved = 0);

    private enum TCP_TABLE_CLASS { TCP_TABLE_OWNER_PID_ALL = 5 }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }
}
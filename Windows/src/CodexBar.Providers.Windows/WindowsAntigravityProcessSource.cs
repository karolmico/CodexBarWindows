using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using CodexBar.Core.Providers.Antigravity;

namespace CodexBar.Providers.Windows;

/// <summary>
/// Windows implementation of the Antigravity process seam: WMI for command lines (the language
/// server passes its CSRF token there) and the IP helper TCP table for loopback listeners.
/// </summary>
public sealed class WindowsAntigravityProcessSource : IAntigravityProcessSource
{
    public Task<IReadOnlyList<AntigravityProcessInfo>> ListProcessesAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<AntigravityProcessInfo>>(
            () =>
            {
                var processes = new List<AntigravityProcessInfo>();
                using var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process");
                using var results = searcher.Get();
                foreach (ManagementBaseObject item in results)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item["CommandLine"] is not string commandLine || commandLine.Length == 0)
                    {
                        continue;
                    }

                    var lower = commandLine.ToLowerInvariant();
                    if (!lower.Contains("antigravity", StringComparison.Ordinal)
                        && !lower.Contains("language_server", StringComparison.Ordinal)
                        && !lower.Contains("language-server", StringComparison.Ordinal)
                        && !lower.Contains("agy", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (item["ProcessId"] is uint pid)
                    {
                        processes.Add(new AntigravityProcessInfo((int)pid, commandLine));
                    }
                }

                return processes;
            },
            cancellationToken);

    public Task<IReadOnlyList<int>> ListeningPortsAsync(int pid, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<int>>(
            () => TcpTable.ListeningPorts(pid),
            cancellationToken);

    private static class TcpTable
    {
        private const int AfInet = 2;
        private const int TcpTableOwnerPidListener = 3;
        private const int ErrorInsufficientBuffer = 122;

        public static IReadOnlyList<int> ListeningPorts(int pid)
        {
            var size = 0;
            var result = GetExtendedTcpTable(
                IntPtr.Zero,
                ref size,
                false,
                AfInet,
                TcpTableOwnerPidListener,
                0);
            if (result != ErrorInsufficientBuffer || size <= sizeof(int))
            {
                return [];
            }

            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(
                    buffer,
                    ref size,
                    false,
                    AfInet,
                    TcpTableOwnerPidListener,
                    0);
                if (result != 0)
                {
                    return [];
                }

                var count = Marshal.ReadInt32(buffer);
                var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
                var ports = new List<int>();
                var rowPointer = IntPtr.Add(buffer, sizeof(int));
                for (var index = 0; index < count; index++)
                {
                    var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(
                        IntPtr.Add(rowPointer, index * rowSize));
                    if (row.OwningPid != pid || row.State != 2)
                    {
                        continue;
                    }

                    var address = new IPAddress(row.LocalAddress);
                    if (!IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any))
                    {
                        continue;
                    }

                    var port = (ushort)(((row.LocalPort & 0xFF) << 8) | ((row.LocalPort >> 8) & 0xFF));
                    if (port > 0 && !ports.Contains(port))
                    {
                        ports.Add(port);
                    }
                }

                return ports;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int GetExtendedTcpTable(
            IntPtr tcpTable,
            ref int size,
            bool order,
            int addressFamily,
            int tableClass,
            uint reserved);

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcpRowOwnerPid
        {
            public int State;
            public uint LocalAddress;
            public uint LocalPort;
            public uint RemoteAddress;
            public uint RemotePort;
            public int OwningPid;
        }
    }
}

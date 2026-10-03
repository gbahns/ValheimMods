using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace DiagnoseServerLag
{
    /// <summary>
    /// How much physical memory the machine has left, once a second.
    ///
    /// This exists to answer one question the mod could not: is a machine slow because it is out of
    /// memory and paging to disk? That produces a distinctive shape - long frames at *low* CPU,
    /// because the process is blocked on a disk rather than computing - but telling it apart from
    /// an ordinary stall needs to know whether memory was actually short at the time, and nothing
    /// here measured that. Process.WorkingSet64 reads zero under this Mono, so even the game's own
    /// footprint was unavailable; free physical memory is both more reliable and the better
    /// question, since paging is a property of the machine rather than of one process.
    ///
    /// Windows via GlobalMemoryStatusEx, Linux via /proc/meminfo, matching SystemCpu. Reported as
    /// megabytes free, or -1 when it cannot be had, so missing never reads as "no memory left".
    /// </summary>
    internal static class SystemMemory
    {
        internal static bool Readable { get; private set; } = true;

        private static bool _linux;
        private static bool _probed;

        /// <summary>Free physical memory in MB, or -1 when unavailable.</summary>
        internal static int FreeMB()
        {
            if (!Readable) return -1;
            try
            {
                if (!_probed) { _probed = true; _linux = File.Exists("/proc/meminfo"); }
                int mb = _linux ? ReadLinux() : ReadWindows();
                if (mb < 0) { Readable = false; return -1; }
                return mb;
            }
            catch
            {
                Readable = false;
                return -1;
            }
        }

        // MemAvailable is the kernel's own estimate of what can be had without swapping, which is
        // the figure that matters here - MemFree ignores reclaimable cache and reads alarmingly low
        // on a perfectly healthy machine.
        private static int ReadLinux()
        {
            foreach (var line in File.ReadAllLines("/proc/meminfo"))
            {
                if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal)) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long kb))
                    return (int)(kb / 1024L);
                return -1;
            }
            return -1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            internal uint dwLength;
            internal uint dwMemoryLoad;
            internal ulong ullTotalPhys;
            internal ulong ullAvailPhys;
            internal ulong ullTotalPageFile;
            internal ulong ullAvailPageFile;
            internal ulong ullTotalVirtual;
            internal ulong ullAvailVirtual;
            internal ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private static int ReadWindows()
        {
            var m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref m)) return -1;
            return (int)(m.ullAvailPhys / (1024UL * 1024UL));
        }
    }
}

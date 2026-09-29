using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace DiagnoseServerLag
{
    /// <summary>
    /// How busy the whole machine is, not just this process.
    ///
    /// Everything else here measures Valheim. That is the right thing to measure for "is the game
    /// heavy", and the wrong thing entirely for "is something else eating my computer" - which is
    /// the question that actually comes up. A stall with Valheim's own CPU unchanged is the
    /// signature of the game pausing itself; a stall with Valheim's CPU *dropping* while the
    /// machine stays pegged is the signature of something else taking the processor away. Without a
    /// machine-wide figure those two look identical, and the difference decides whether the answer
    /// is "close your other programs" or "this is the game".
    ///
    /// It was added because a player's stalls could not be attributed: his CPU held steady through
    /// them, which ruled out the background process he suspected, but nothing on hand could confirm
    /// what the rest of his machine was doing.
    ///
    /// Two implementations, because the clients are Windows and the dedicated server is Linux:
    /// GetSystemTimes for one, /proc/stat for the other. Both report cumulative counters, so the
    /// figure is the change between samples; the first reading after startup has nothing to compare
    /// against and is reported as unavailable rather than guessed.
    /// </summary>
    internal static class SystemCpu
    {
        /// <summary>False once a platform turns out not to answer; it is not asked again.</summary>
        internal static bool Readable { get; private set; } = true;

        internal static string UnreadableReason { get; private set; } = "";

        private static bool _linux;
        private static bool _probed;
        private static bool _haveBaseline;
        private static ulong _prevIdle, _prevTotal;

        /// <summary>
        /// The machine's busy share since the previous call, 0 to 1, or -1 when it cannot be had.
        /// Call once a second; the value is the average across that interval.
        /// </summary>
        internal static float Read()
        {
            if (!Readable) return -1f;
            try
            {
                if (!_probed)
                {
                    _probed = true;
                    _linux = File.Exists("/proc/stat");
                }

                ulong idle, total;
                bool ok = _linux ? ReadLinux(out idle, out total) : ReadWindows(out idle, out total);
                if (!ok) { Unreadable("the platform's CPU counters did not answer"); return -1f; }

                if (!_haveBaseline)
                {
                    _haveBaseline = true;
                    _prevIdle = idle; _prevTotal = total;
                    return -1f;                    // nothing to difference against yet
                }

                ulong dIdle = idle >= _prevIdle ? idle - _prevIdle : 0UL;
                ulong dTotal = total >= _prevTotal ? total - _prevTotal : 0UL;
                _prevIdle = idle; _prevTotal = total;
                if (dTotal == 0UL) return -1f;

                float busy = 1f - (float)dIdle / dTotal;
                return busy < 0f ? 0f : (busy > 1f ? 1f : busy);
            }
            catch (Exception e)
            {
                Unreadable(e.Message);
                return -1f;
            }
        }

        private static void Unreadable(string why)
        {
            if (!Readable) return;
            Readable = false;
            UnreadableReason = why;
            DiagnoseServerLagMod.Log.LogInfo(
                $"[DiagnoseServerLag] Machine-wide CPU is not available here ({why}); " +
                "this process's own CPU is unaffected. Said once per session.");
        }

        // ── Linux ─────────────────────────────────────────────────────────────────
        // The first line of /proc/stat is cumulative jiffies across all cores:
        //   cpu  user nice system idle iowait irq softirq steal guest guest_nice
        // iowait counts as idle: the processor was available, this machine was waiting on a disk.
        private static bool ReadLinux(out ulong idle, out ulong total)
        {
            idle = 0UL; total = 0UL;
            foreach (var line in File.ReadAllLines("/proc/stat"))
            {
                if (!line.StartsWith("cpu ", StringComparison.Ordinal)) continue;
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 1; i < parts.Length; i++)
                {
                    if (!ulong.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong v)) break;
                    total += v;
                    if (i == 4 || i == 5) idle += v;         // idle, iowait
                }
                return total > 0UL;
            }
            return false;
        }

        // ── Windows ───────────────────────────────────────────────────────────────
        // GetSystemTimes returns three FILETIMEs, each two DWORDs, which marshal as a long.
        // The kernel figure INCLUDES the idle figure, so the total is kernel + user.
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        private static bool ReadWindows(out ulong idle, out ulong total)
        {
            idle = 0UL; total = 0UL;
            if (!GetSystemTimes(out long i, out long k, out long u)) return false;
            if (i < 0 || k < 0 || u < 0) return false;
            idle = (ulong)i;
            total = (ulong)k + (ulong)u;
            return total > 0UL;
        }
    }
}

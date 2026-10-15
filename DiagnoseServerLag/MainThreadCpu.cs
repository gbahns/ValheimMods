using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace DiagnoseServerLag
{
    /// <summary>
    /// How much processor time the main thread actually burned, as opposed to how long its frames
    /// took.
    ///
    /// This is the fallback for the question FrameTimings was added to answer and cannot: was a
    /// long frame long because the main thread had too much to do, or because it was standing
    /// still waiting for something? Unity's FrameTimingManager answers that directly, but only in
    /// a player built with frame timing statistics enabled - and Valheim's is not. It returns no
    /// timings at all, which is a decision made when the game shipped and cannot be changed from a
    /// mod.
    ///
    /// So the split is reconstructed from the one side that can always be measured. Compare this
    /// against the frame time:
    ///
    ///   main-thread CPU per frame close to the frame time    the thread was busy - computation
    ///   main-thread CPU per frame far below the frame time   the thread was waiting - the
    ///                                                        graphics card, vsync, a lock or the
    ///                                                        disk, but not computation
    ///
    /// It does not name WHAT was waited on, where FrameTimingManager would have. It does separate
    /// the two cases that matter for deciding whether turning graphics settings down can help,
    /// which is the decision that kept coming up.
    ///
    /// Why not the figure already recorded: CpuMsPerSec is the whole process across every thread -
    /// main, render, and Unity's job workers. During the 27-second stall burst that prompted all
    /// this it went DOWN 40% while frame time doubled, which sounds conclusive until you divide by
    /// the frame count and find process CPU per frame went UP 26%. Summed across threads the
    /// figure simply cannot distinguish "the main thread waited" from "the workers did more".
    /// Only the main thread's own total can.
    ///
    /// Two implementations, matching SystemCpu, because the clients are Windows and the dedicated
    /// server is Linux:
    ///
    /// - Windows: GetCurrentThreadId, called once from the main thread, then GetThreadTimes on a
    ///   handle kept open. Deliberately not Process.Threads, which snapshots every thread in the
    ///   process - Valheim has dozens - to read one of them.
    /// - Linux: the main thread's task id equals the process id, so /proc/self/task/&lt;pid&gt;/stat
    ///   is the right file without having to identify anything.
    ///
    /// Both are cumulative counters, so the reported figure is the change since the previous
    /// sample; the first reading has nothing to compare against and reports unavailable rather
    /// than inventing a baseline.
    ///
    /// MUST be first called from the main thread - that is what GetCurrentThreadId identifies. The
    /// only caller is Sampler.Commit, reached from the plugin's Update.
    /// </summary>
    internal static class MainThreadCpu
    {
        internal static bool Readable { get; private set; } = true;
        internal static string UnreadableReason { get; private set; } = "";

        private const uint ThreadQueryInformation = 0x0040;

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint access, bool inheritHandle, uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit,
                                                  out long kernel, out long user);

        private static bool _probed;
        private static bool _linux;
        private static IntPtr _thread = IntPtr.Zero;
        private static string _statPath;

        private static bool _haveBaseline;
        private static double _prevMs;
        private static float _prevAt;

        /// <summary>
        /// Milliseconds of main-thread CPU per second of wall clock since the previous call, or
        /// -1 when it cannot be read. 1000 means the main thread was busy the entire second.
        /// </summary>
        internal static float MsPerSec(float now)
        {
            if (!Readable) return -1f;
            if (!_probed) Probe();
            if (!Readable) return -1f;

            double totalMs;
            if (!TryTotalMs(out totalMs)) return -1f;

            if (!_haveBaseline)
            {
                _haveBaseline = true;
                _prevMs = totalMs;
                _prevAt = now;
                return -1f;          // nothing to difference against yet
            }

            float span = now - _prevAt;
            double usedMs = totalMs - _prevMs;
            _prevMs = totalMs;
            _prevAt = now;

            // A span at or below zero means the clock did not advance between calls, and a
            // negative use means the counter moved backwards - neither is a measurement.
            if (span <= 0f || usedMs < 0.0) return -1f;
            return (float)(usedMs / span);
        }

        private static void Probe()
        {
            _probed = true;
            try
            {
                _linux = Directory.Exists("/proc/self/task");
                if (_linux)
                {
                    // On Linux the main thread's task id IS the process id, which is why this
                    // needs no searching and no guessing about thread names.
                    int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                    _statPath = "/proc/self/task/" + pid.ToString(CultureInfo.InvariantCulture) + "/stat";
                    if (!File.Exists(_statPath)) Unreadable("no " + _statPath);
                    return;
                }

                uint tid = GetCurrentThreadId();
                _thread = OpenThread(ThreadQueryInformation, false, tid);
                if (_thread == IntPtr.Zero)
                    Unreadable("OpenThread failed for the main thread (error " +
                               Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + ")");
            }
            catch (Exception e)
            {
                Unreadable(e.GetType().Name + ": " + e.Message);
            }
        }

        private static bool TryTotalMs(out double ms)
        {
            ms = 0.0;
            try
            {
                if (_linux)
                {
                    // Fields 14 and 15 of /proc/<tid>/stat are utime and stime in clock ticks.
                    // They are counted from field 1, and the process name in field 2 can itself
                    // contain spaces and brackets - so the split starts after the last ')'.
                    string text = File.ReadAllText(_statPath);
                    int close = text.LastIndexOf(')');
                    if (close < 0) return false;
                    string[] rest = text.Substring(close + 1)
                        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    // rest[0] is field 3 (state), so utime is rest[11] and stime rest[12].
                    if (rest.Length < 13) return false;
                    double utime = double.Parse(rest[11], CultureInfo.InvariantCulture);
                    double stime = double.Parse(rest[12], CultureInfo.InvariantCulture);
                    // 100 ticks per second is the kernel's USER_HZ on every platform this runs on.
                    ms = (utime + stime) * 10.0;
                    return true;
                }

                long creation, exit, kernel, user;
                if (!GetThreadTimes(_thread, out creation, out exit, out kernel, out user)) return false;
                // Both are 100-nanosecond units.
                ms = (kernel + user) / 10000.0;
                return true;
            }
            catch (Exception e)
            {
                Unreadable(e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        private static void Unreadable(string why)
        {
            if (!Readable) return;
            Readable = false;
            UnreadableReason = why;
            DiagnoseServerLagMod.Log.LogInfo(
                "[DiagnoseServerLag] main-thread CPU time is not available - " + why +
                ". Frame times are still measured; only the split between computing and waiting " +
                "is missing, so cpu_main_ms stays blank in captures.");
        }
    }
}

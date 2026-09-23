using System;
using System.Diagnostics;

namespace DiagnoseServerLag
{
    /// <summary>
    /// What this process is costing the machine it runs on.
    ///
    /// This exists because tick time cannot measure a capped server, and most dedicated servers are
    /// capped. bahnsheim holds 33.3 ms per tick with almost no variance, and that single number is
    /// equally consistent with three milliseconds of work followed by thirty of sleep, or with
    /// thirty-three milliseconds of work and nothing left over. They are the same measurement and
    /// opposite situations: one has ten times the headroom it needs, the other is one player away
    /// from falling over. A frame limiter makes wall-clock tick length say nothing about load until
    /// the moment the load exceeds it, at which point the server is already failing.
    ///
    /// CPU time consumed says it directly, and keeps saying it as the server approaches trouble
    /// rather than after it arrives. It is also the one measurement that compares honestly between
    /// two different machines, which is what it was added for: wall-clock tick length is a property
    /// of the configured cap, while CPU seconds per wall second is a property of the work and the
    /// hardware doing it.
    ///
    /// Garbage collections are measured alongside because a collection pause is a stall the tick
    /// average hides: it happens inside one tick and is gone, and on a server with a large heap the
    /// gen2 collections are exactly the hitches players feel as a world-wide freeze.
    ///
    /// Every reading is guarded and latched. Mono on Linux does implement all of this, but a
    /// diagnostic that throws on an unusual host is worse than one that admits it cannot see.
    /// </summary>
    internal static class Machine
    {
        /// <summary>Cores the runtime believes it has, for turning CPU time into a share of the machine.</summary>
        internal static int ProcessorCount { get; private set; } = 1;

        /// <summary>False once any process counter has thrown; the report then says so rather than showing zeros.</summary>
        internal static bool Readable { get; private set; } = true;

        internal static string UnreadableReason { get; private set; } = "";

        private static Process _self;
        private static TimeSpan _lastCpu;
        private static float _lastCpuAt;
        private static int _lastGen0, _lastGen1, _lastGen2;
        private static bool _primed;

        internal static void Reset()
        {
            _primed = false;
            _self = null;
        }

        /// <summary>
        /// Fills in the machine-level part of a sample.
        ///
        /// The first call only primes the counters: CPU time is a running total, so the first
        /// reading has no previous one to subtract from and would otherwise report every CPU second
        /// since the process started as though it had all happened in this one second.
        /// </summary>
        internal static void Fill(ref Sample s, float now)
        {
            if (!Readable) return;
            try
            {
                if (_self == null)
                {
                    _self = Process.GetCurrentProcess();
                    ProcessorCount = Math.Max(1, Environment.ProcessorCount);
                }

                TimeSpan cpu = _self.TotalProcessorTime;
                int g0 = GC.CollectionCount(0);
                int g1 = GC.CollectionCount(1);
                int g2 = GC.CollectionCount(2);

                if (_primed)
                {
                    float elapsed = now - _lastCpuAt;
                    if (elapsed > 0.001f)
                    {
                        // Milliseconds of CPU burned per second of wall clock. 1000 means one core
                        // fully busy; ProcessorCount * 1000 means the whole machine.
                        s.CpuMsPerSec = (float)((cpu - _lastCpu).TotalMilliseconds / elapsed);
                        s.HasCpu = true;
                    }
                    s.Gc0 = g0 - _lastGen0;
                    s.Gc1 = g1 - _lastGen1;
                    s.Gc2 = g2 - _lastGen2;
                }
                _primed = true;
                _lastCpu = cpu;
                _lastCpuAt = now;
                _lastGen0 = g0;
                _lastGen1 = g1;
                _lastGen2 = g2;

                // Refresh() is what makes WorkingSet64 re-read rather than hand back the value
                // cached when the Process object was created.
                _self.Refresh();
                s.WorkingSetBytes = _self.WorkingSet64;
                s.HeapBytes = GC.GetTotalMemory(false);
            }
            catch (Exception e)
            {
                Readable = false;
                UnreadableReason = e.Message;
                DiagnoseServerLagMod.Log.LogInfo(
                    $"[DiagnoseServerLag] Process counters are not available on this machine ({e.Message}). " +
                    "CPU and memory will be reported as not measurable; everything else still works.");
            }
        }

        /// <summary>CPU as a share of one core, where 1.0 is one core fully busy.</summary>
        internal static float CoreShare(float cpuMsPerSec) => cpuMsPerSec / 1000f;

        /// <summary>CPU as a share of the whole machine, where 1.0 is every core fully busy.</summary>
        internal static float MachineShare(float cpuMsPerSec) => cpuMsPerSec / (1000f * Math.Max(1, ProcessorCount));

        /// <summary>
        /// How many times the current load the server could carry before the cap runs out.
        ///
        /// Valheim's simulation is effectively single-threaded, so the ceiling that matters is one
        /// core, not the machine: a server using 18% of a core has room for roughly five times its
        /// present work however many cores sit idle next to it. Deliberately reported against one
        /// core for that reason, and it is an estimate - work does not scale perfectly linearly with
        /// players or objects - but it is the right order of magnitude and the right question.
        /// </summary>
        internal static float Headroom(float cpuMsPerSec)
        {
            float share = CoreShare(cpuMsPerSec);
            return share <= 0.0001f ? 0f : 1f / share;
        }
    }
}

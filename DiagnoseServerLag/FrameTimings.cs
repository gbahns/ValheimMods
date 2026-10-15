using System;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>
    /// How a frame's time was divided between the processor and the graphics card.
    ///
    /// Everything else in this mod measures how LONG a frame took. That is enough to say a machine
    /// is slow and never enough to say why, because the two cases that matter look identical from
    /// the outside: a main thread with too much to compute, and a main thread sitting idle waiting
    /// for the graphics card to finish. Both read as a long frame. Only one of them is fixed by
    /// turning graphics settings down.
    ///
    /// It was added after a 27-second stretch where a client's frame time doubled, its total
    /// processor use FELL by 40%, and every other measurement in the capture - objects, creatures,
    /// pieces, heap, free memory, send queue, send rate, bytes in - was identical before, during
    /// and after. Nothing recorded could say what the machine had been waiting for, and the whole
    /// question of whether a 41 ms frame was processor-bound or card-bound had been answered by
    /// inference from how the frame responded to resolution changes rather than by measuring it.
    ///
    /// Unity's FrameTimingManager answers it directly. It is a double buffer the engine fills on
    /// its own: CaptureFrameTimings takes a snapshot of what has completed, GetLatestTimings hands
    /// back the most recent frames, and each one carries the main thread's time, the render
    /// thread's time and the card's time for that frame. Reading it costs one call per second here
    /// and no instrumentation of the game at all.
    ///
    /// The catch, and the reason every field below can report "unavailable": the engine only fills
    /// these in when the player build was made with frame timing statistics enabled. That is a
    /// decision Iron Gate made when they shipped, it cannot be changed from a mod, and when it is
    /// off the API still exists and still succeeds - it just returns zeros. So a zero here is
    /// genuinely ambiguous between "the card did no work" and "nobody is counting", and the two are
    /// told apart the only way they can be: a frame whose parts are ALL zero was not measured,
    /// because a frame that really happened took a nonzero amount of time somewhere. See Available.
    /// </summary>
    internal static class FrameTimings
    {
        /// <summary>
        /// Whether this build fills the timings in at all. Null until the first frame has been
        /// looked at, false once a frame has come back entirely zero, true after a real reading.
        ///
        /// Checked rather than assumed because the failure is silent. If this is false the columns
        /// stay blank and the report says so once, instead of printing a plausible-looking 0.0 ms
        /// that would read as "the graphics card is doing nothing" - which, on a machine that is
        /// card-bound, is the exact opposite of the truth.
        /// </summary>
        internal static bool? Available { get; private set; }

        /// <summary>Last second's averages in milliseconds, or -1 where nothing could be read.</summary>
        internal static float GpuMs { get; private set; } = -1f;
        internal static float CpuMainMs { get; private set; } = -1f;
        internal static float CpuRenderMs { get; private set; } = -1f;

        // Sized for one second of frames at a generous rate. Allocated once: this runs every
        // second for the life of the process, and the rest of the mod allocates nothing after
        // construction either.
        private const int Window = 256;
        private static readonly FrameTiming[] _timings = new FrameTiming[Window];

        private static bool _reported;

        /// <summary>
        /// Samples the frames that completed since the last call. Called once per second from
        /// Commit, not once per frame: the engine keeps its own history, so asking more often
        /// gathers the same numbers at more cost.
        /// </summary>
        internal static void Sample()
        {
            GpuMs = CpuMainMs = CpuRenderMs = -1f;
            try
            {
                FrameTimingManager.CaptureFrameTimings();
                uint got = FrameTimingManager.GetLatestTimings((uint)Window, _timings);
                if (got == 0)
                {
                    // Not "no frames happened" - frames obviously happened. An empty return is how
                    // the engine reports that it is not keeping these at all. This used to return
                    // without saying so, which left the columns blank and the log silent, and the
                    // first capture that needed them could not tell a missing feature from a bug
                    // in this file.
                    Note(false, "the engine returns no frame timings at all, so this build was made " +
                                "with frame timing statistics disabled");
                    return;
                }

                double gpu = 0, main = 0, render = 0;
                int counted = 0;
                for (uint i = 0; i < got && i < Window; i++)
                {
                    var t = _timings[i];
                    // A frame with nothing in any of the three is not a fast frame, it is an
                    // unmeasured one. Averaging it in would drag a real reading toward zero in
                    // exactly the mixed case - some frames counted, some not - that is hardest to
                    // notice afterwards.
                    if (t.cpuMainThreadFrameTime <= 0.0 && t.cpuRenderThreadFrameTime <= 0.0 &&
                        t.gpuFrameTime <= 0.0) continue;
                    gpu += t.gpuFrameTime;
                    main += t.cpuMainThreadFrameTime;
                    render += t.cpuRenderThreadFrameTime;
                    counted++;
                }

                if (counted == 0)
                {
                    Note(false, "this build reports frame timings as all-zero, so frame timing statistics are off in it");
                    return;
                }

                GpuMs = (float)(gpu / counted);
                CpuMainMs = (float)(main / counted);
                CpuRenderMs = (float)(render / counted);
                Note(true, "");
            }
            catch (Exception e)
            {
                // Any surprise at all - a renamed API, a platform that refuses - is reported as
                // unavailable rather than retried every second for the rest of the session.
                Note(false, e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Note(bool available, string why)
        {
            if (Available == available) return;
            // Only the first verdict is logged. A build with the statistics off says so once; one
            // that has them on says nothing, because a working measurement is not news.
            if (Available == null && available) { Available = true; return; }
            Available = available;
            if (available || _reported) return;
            _reported = true;
            DiagnoseServerLagMod.Log.LogInfo(
                "[DiagnoseServerLag] per-frame GPU and main-thread times are not available - " + why +
                ". Frame times are still measured; only the split between processor and graphics " +
                "card is missing, so gpu_ms and cpu_main_ms stay blank in captures.");
        }
    }
}

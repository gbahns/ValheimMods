using System;
using System.Collections.Generic;

namespace DiagnoseServerLag
{
    /// <summary>
    /// One machine's window, in the shape it travels to the server in for a group capture.
    ///
    /// Deliberately a per-second series and not a summary. A summary from each client would say who
    /// had a bad time; only the seconds say whether they had it *together*, and that is the entire
    /// question a group capture exists to answer. Four clients that each stalled twice is a very
    /// different finding depending on whether those were the same two seconds or eight different
    /// ones - the first is the server or the path everyone shares, the second is four machines with
    /// four unrelated local problems, and no amount of averaging separates them.
    ///
    /// Every field travels, compressed. Layout 1 sent five columns, on the reasoning that nobody
    /// would line the rest up by hand. The first real group capture disproved that within minutes:
    /// it established that the stalls were local and then could not say what the machine had been
    /// doing during them, which took a second command on a second machine to answer.
    ///
    /// That second command does not scale to other people. Every machine is recording all the time
    /// and dsl_bench reads backwards, so a teammate asked later can still cover the same minute -
    /// but it means four people running commands and sending four files, and a client that has
    /// logged off has taken its ring with it for good, since the history lives in memory. One
    /// command from the admin, while everyone is still connected, has to come back with everything.
    ///
    /// The payload objection does not survive either. The window is recorded before any of it is
    /// sent, so the transfer cannot contaminate the measurement it carries, and a series of
    /// slowly-changing numbers compresses hard.
    /// </summary>
    internal sealed class ClientSeries
    {
        // 1: five columns, uncompressed. 2: every field, compressed. Both are still read,
        // because clients update on their own schedule and an old one should be diminished
        // rather than refused.
        private const byte Layout = 5;

        /// <summary>One second, as much of it as the group view needs.</summary>
        internal struct Second
        {
            internal long UtcTicks;
            internal float FrameMaxMs;
            internal int Stalls;
            internal float CpuMsPerSec;
            internal int Collections;
        }

        internal long Uid;
        internal string Name = "";
        internal string CpuName = "";
        internal int Cores;
        internal bool HasCpu;
        internal readonly List<Second> Seconds = new List<Second>();

        /// <summary>The full per-second record. Empty when an older client sent layout 1.</summary>
        internal readonly List<Sample> Samples = new List<Sample>();

        /// <summary>Whether this client sent every field or only the correlation columns.</summary>
        internal bool Full;

        /// <summary>Median frame time over the window, which the summary line reports.</summary>
        internal float FrameMedianMs;
        internal int TotalStalls;
        internal float CpuMedianMsPerSec;
        internal int RoundTripMs;

        /// <summary>Creatures this machine was simulating, at the end of the window.</summary>
        internal int OwnedAI;
        internal int NearbyAI;

        internal ZPackage Pack()
        {
            var pkg = new ZPackage();
            pkg.Write(Layout);
            pkg.Write(Uid);
            pkg.Write(Name ?? "");
            pkg.Write(CpuName ?? "");
            pkg.Write(Cores);
            pkg.Write(HasCpu);
            pkg.Write(FrameMedianMs);
            pkg.Write(TotalStalls);
            pkg.Write(CpuMedianMsPerSec);
            pkg.Write(RoundTripMs);
            pkg.Write(OwnedAI);
            pkg.Write(NearbyAI);
            // The series goes in compressed: it is the bulk of the message, and it is the part
            // that squeezes, being mostly slowly-changing or repeated numbers.
            var inner = new ZPackage();
            inner.Write(Samples.Count);
            foreach (var s in Samples) SampleWire.Write(inner, s);
            pkg.WriteCompressed(inner);
            return pkg;
        }

        internal static ClientSeries Unpack(ZPackage pkg)
        {
            if (pkg == null) return null;
            try
            {
                var c = new ClientSeries();
                byte layout = pkg.ReadByte();
                if (layout < 1) return null;
                c.Uid = pkg.ReadLong();
                c.Name = pkg.ReadString();
                c.CpuName = pkg.ReadString();
                c.Cores = pkg.ReadInt();
                c.HasCpu = pkg.ReadBool();
                c.FrameMedianMs = pkg.ReadSingle();
                c.TotalStalls = pkg.ReadInt();
                c.CpuMedianMsPerSec = pkg.ReadSingle();
                c.RoundTripMs = pkg.ReadInt();
                if (layout >= 3)
                {
                    c.OwnedAI = pkg.ReadInt();
                    c.NearbyAI = pkg.ReadInt();
                }
                if (layout >= 2)
                {
                    var inner = pkg.ReadCompressedPackage();
                    int n = inner.ReadInt();
                    // A client cannot be allowed to make the server allocate whatever it likes.
                    if (n < 0 || n > 20000) return null;
                    for (int i = 0; i < n; i++) c.Samples.Add(SampleWire.Read(inner, layout));
                    c.Full = true;
                    c.FillSecondsFromSamples();
                }
                else
                {
                    int n = pkg.ReadInt();
                    if (n < 0 || n > 20000) return null;
                    for (int i = 0; i < n; i++)
                    {
                        c.Seconds.Add(new Second
                        {
                            UtcTicks = pkg.ReadLong(),
                            FrameMaxMs = pkg.ReadSingle(),
                            Stalls = pkg.ReadInt(),
                            CpuMsPerSec = pkg.ReadSingle(),
                            Collections = pkg.ReadInt(),
                        });
                    }
                }
                return c;
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogWarning($"[DiagnoseServerLag] Could not read a client series: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Mirrors the full samples into the correlation view, so everything downstream reads one
        /// shape whether the client sent layout 1 or layout 2.
        /// </summary>
        private void FillSecondsFromSamples()
        {
            Seconds.Clear();
            foreach (var s in Samples)
                Seconds.Add(new Second
                {
                    UtcTicks = s.UtcTicks,
                    FrameMaxMs = s.FrameMsMax,
                    Stalls = s.Stalls,
                    CpuMsPerSec = s.CpuMsPerSec,
                    Collections = Machine.Collections(s),
                });
        }

        /// <summary>Builds this machine's own series for the last <paramref name="seconds"/>.</summary>
        internal static ClientSeries FromLocal(int seconds)
        {
            var window = Sampler.History.Recent(seconds);
            var c = new ClientSeries
            {
                Uid = ZDOMan.GetSessionID(),
                Name = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "(no character)",
                CpuName = UnityEngine.SystemInfo.processorType,
                Cores = Machine.ProcessorCount,
                RoundTripMs = UnityEngine.Mathf.RoundToInt(LagNetwork.RoundTripMs),
            };

            foreach (var s in window)
            {
                if (s.HasCpu) c.HasCpu = true;
                c.TotalStalls += s.Stalls;
                c.Samples.Add(s);
            }
            c.Full = true;
            c.FillSecondsFromSamples();
            if (window.Count > 0)
            {
                c.OwnedAI = window[window.Count - 1].OwnedAI;
                c.NearbyAI = window[window.Count - 1].NearbyAI;
            }
            c.FrameMedianMs = Stats.Median(window, x => x.FrameMsAvg);
            c.CpuMedianMsPerSec = Stats.Median(window, x => x.CpuMsPerSec);
            return c;
        }
    }
}

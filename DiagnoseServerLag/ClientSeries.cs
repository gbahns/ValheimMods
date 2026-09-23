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
    /// Only the columns correlation needs are sent. The full record stays in each machine's own CSV,
    /// where anyone who wants it can go and read it; shipping all twenty-six columns from every
    /// client would multiply the payload for data nobody is going to line up by hand.
    /// </summary>
    internal sealed class ClientSeries
    {
        private const byte Layout = 1;

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

        /// <summary>Median frame time over the window, which the summary line reports.</summary>
        internal float FrameMedianMs;
        internal int TotalStalls;
        internal float CpuMedianMsPerSec;
        internal int RoundTripMs;

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
            pkg.Write(Seconds.Count);
            foreach (var s in Seconds)
            {
                pkg.Write(s.UtcTicks);
                pkg.Write(s.FrameMaxMs);
                pkg.Write(s.Stalls);
                pkg.Write(s.CpuMsPerSec);
                pkg.Write(s.Collections);
            }
            return pkg;
        }

        internal static ClientSeries Unpack(ZPackage pkg)
        {
            if (pkg == null) return null;
            try
            {
                var c = new ClientSeries();
                if (pkg.ReadByte() < 1) return null;
                c.Uid = pkg.ReadLong();
                c.Name = pkg.ReadString();
                c.CpuName = pkg.ReadString();
                c.Cores = pkg.ReadInt();
                c.HasCpu = pkg.ReadBool();
                c.FrameMedianMs = pkg.ReadSingle();
                c.TotalStalls = pkg.ReadInt();
                c.CpuMedianMsPerSec = pkg.ReadSingle();
                c.RoundTripMs = pkg.ReadInt();
                int n = pkg.ReadInt();
                // A client cannot be allowed to make the server allocate whatever it likes. The
                // cap is the longest window the mod can hold, with room to spare.
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
                return c;
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogWarning($"[DiagnoseServerLag] Could not read a client series: {e.Message}");
                return null;
            }
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
                c.Seconds.Add(new Second
                {
                    UtcTicks = s.UtcTicks,
                    FrameMaxMs = s.FrameMsMax,
                    Stalls = s.Stalls,
                    CpuMsPerSec = s.CpuMsPerSec,
                    Collections = Machine.Collections(s),
                });
            }
            c.FrameMedianMs = Stats.Median(window, x => x.FrameMsAvg);
            c.CpuMedianMsPerSec = Stats.Median(window, x => x.CpuMsPerSec);
            return c;
        }
    }
}

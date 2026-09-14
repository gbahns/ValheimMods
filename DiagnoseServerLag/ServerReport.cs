using System;
using System.Collections.Generic;

namespace DiagnoseServerLag
{
    /// <summary>
    /// What the server knows about itself, in the shape it travels to a client in.
    ///
    /// This is the half of the diagnosis a client cannot reach on its own, and the reason the mod
    /// is installed on both ends. A client watching its own frame times can tell that the game
    /// feels bad; only these numbers can tell it whether the server was keeping up at the time.
    ///
    /// The layout is versioned because a server and a client are updated separately in practice -
    /// the server gets a new DLL when someone remembers to deploy it - so a newer client will meet
    /// older servers for as long as this mod exists. Readers stop at the version they understand
    /// and keep whatever came before it, so an old server stays useful instead of unreadable.
    /// </summary>
    internal sealed class ServerReport
    {
        private const byte Layout = 1;

        /// <summary>Time.unscaledTime on the receiving client when this arrived.</summary>
        internal float ReceivedAt;

        internal bool Dedicated;

        // The newest second measured on the server.
        internal float TickMsAvg;
        internal float TickMsMax;
        internal int Stalls;

        // The same, over the server's baseline window, so a client can tell a one-second hitch
        // from a server that has been struggling for a minute.
        internal float BaselineTickMs;
        internal float WorstTickMs;
        internal int WindowSeconds;
        internal int StallsInWindow;

        internal int Zdos;
        internal int ZdosSent;
        internal int ZdosRecv;
        internal int PeerCount;
        internal int WorstSendQueue;
        internal int TotalSendRate;

        /// <summary>Empty when the server is configured not to share it, or the asker is not allowed it.</summary>
        internal List<PeerSample> Peers = new List<PeerSample>();

        /// <summary>Set when the server declined to include the peer table, so the panel can say why.</summary>
        internal bool PeerDetailWithheld;

        internal ZPackage Pack()
        {
            var pkg = new ZPackage();
            pkg.Write(Layout);
            pkg.Write(Dedicated);

            pkg.Write(TickMsAvg);
            pkg.Write(TickMsMax);
            pkg.Write(Stalls);

            pkg.Write(BaselineTickMs);
            pkg.Write(WorstTickMs);
            pkg.Write(WindowSeconds);
            pkg.Write(StallsInWindow);

            pkg.Write(Zdos);
            pkg.Write(ZdosSent);
            pkg.Write(ZdosRecv);
            pkg.Write(PeerCount);
            pkg.Write(WorstSendQueue);
            pkg.Write(TotalSendRate);

            pkg.Write(PeerDetailWithheld);
            pkg.Write(Peers.Count);
            foreach (var p in Peers)
            {
                pkg.Write(p.Uid);
                pkg.Write(p.Name ?? "");
                pkg.Write(p.Ping);
                pkg.Write(p.HasPing);
                pkg.Write(p.Quality);
                pkg.Write(p.SendQueue);
                pkg.Write(p.SendRate);
                pkg.Write(p.DistanceFromCenter);
            }
            return pkg;
        }

        internal static ServerReport Unpack(ZPackage pkg)
        {
            if (pkg == null) return null;
            try
            {
                var r = new ServerReport();
                byte layout = pkg.ReadByte();
                if (layout < 1) return null;

                r.Dedicated = pkg.ReadBool();

                r.TickMsAvg = pkg.ReadSingle();
                r.TickMsMax = pkg.ReadSingle();
                r.Stalls = pkg.ReadInt();

                r.BaselineTickMs = pkg.ReadSingle();
                r.WorstTickMs = pkg.ReadSingle();
                r.WindowSeconds = pkg.ReadInt();
                r.StallsInWindow = pkg.ReadInt();

                r.Zdos = pkg.ReadInt();
                r.ZdosSent = pkg.ReadInt();
                r.ZdosRecv = pkg.ReadInt();
                r.PeerCount = pkg.ReadInt();
                r.WorstSendQueue = pkg.ReadInt();
                r.TotalSendRate = pkg.ReadInt();

                r.PeerDetailWithheld = pkg.ReadBool();
                int n = pkg.ReadInt();
                // A corrupt or truncated package must not be able to ask for a huge allocation.
                if (n < 0 || n > 256) return null;
                for (int i = 0; i < n; i++)
                {
                    r.Peers.Add(new PeerSample
                    {
                        Uid = pkg.ReadLong(),
                        Name = pkg.ReadString(),
                        Ping = pkg.ReadInt(),
                        HasPing = pkg.ReadBool(),
                        Quality = pkg.ReadSingle(),
                        SendQueue = pkg.ReadInt(),
                        SendRate = pkg.ReadInt(),
                        DistanceFromCenter = pkg.ReadSingle(),
                    });
                }
                return r;
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogWarning($"[DiagnoseServerLag] Could not read a server report: {e.Message}");
                return null;
            }
        }

        /// <summary>Builds the report from what this server has measured. Server side only.</summary>
        internal static ServerReport FromLocal(bool includePeerDetail)
        {
            var r = new ServerReport { Dedicated = Sampler.IsDedicatedHere };

            if (Sampler.TryNewest(out var newest))
            {
                r.TickMsAvg = newest.FrameMsAvg;
                r.TickMsMax = newest.FrameMsMax;
                r.Stalls = newest.Stalls;
                r.Zdos = newest.Zdos;
                r.ZdosSent = newest.ZdosSent;
                r.ZdosRecv = newest.ZdosRecv;
                r.PeerCount = newest.Peers;
                r.WorstSendQueue = newest.SendQueue;
                r.TotalSendRate = newest.SendRate;
            }

            var window = Sampler.BaselineWindow();
            r.WindowSeconds = window.Count;
            r.BaselineTickMs = Stats.Median(window, x => x.FrameMsAvg);
            r.WorstTickMs = Stats.Max(window, x => x.FrameMsMax);
            foreach (var s in window) r.StallsInWindow += s.Stalls;

            if (includePeerDetail) r.Peers.AddRange(Sampler.Peers);
            else r.PeerDetailWithheld = true;

            return r;
        }
    }
}

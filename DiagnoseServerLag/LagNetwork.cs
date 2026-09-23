using System;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>How much this client knows about the other end.</summary>
    internal enum ServerModule
    {
        /// <summary>Nothing asked yet, or the first answer has not had time to arrive.</summary>
        Unknown,
        /// <summary>The server answered. Full diagnosis available.</summary>
        Present,
        /// <summary>Asked repeatedly and heard nothing: the server is not running this mod.</summary>
        Absent,
        /// <summary>We are the server. Its numbers are read directly, no asking involved.</summary>
        Local,
    }

    /// <summary>
    /// Carries the server's own measurements to the client that is asking.
    ///
    /// Request and response rather than an unsolicited broadcast, so a server with nobody looking
    /// sends nothing at all. That matters more here than in most mods: a diagnostic that adds
    /// steady traffic to a connection would be adding to the problem it exists to explain, and
    /// worse, would make its own effect part of every measurement it took.
    ///
    /// A server that does not run this mod simply never answers, which is not an error to handle
    /// but the finding itself - the client says so in the panel and falls back to the half of the
    /// diagnosis it can make alone.
    /// </summary>
    internal static class LagNetwork
    {
        private const string RpcRequest = "DSL_Request";   // client -> server
        private const string RpcReport  = "DSL_Report";    // server -> the client that asked
        private const string RpcCapture = "DSL_Capture";       // admin client -> server: take a capture
        private const string RpcCaptureResult = "DSL_CaptureResult";   // server -> that client, as text

        /// <summary>Asked this many times with no answer before the server is called unmodded.</summary>
        private const int SilenceBeforeAbsent = 3;

        private static ZRoutedRpc _registeredOn;
        private static float _lastAskedAt = -999f;
        private static int _unanswered;

        // ── latency the mod measures for itself ─────────────────────────────────────
        //
        // Neither socket Valheim gives a dedicated server can report latency. ZPlayFabSocket
        // inherits GetConnectionQuality from ZNetStats, the stub that hardcodes ping and quality to
        // zero, and ZSteamSocket reaches for the client Steam interface, which a server process
        // never initialises because SteamAPI.Init lives in the client-only SteamManager. So the
        // only honest way to a real number is to time something the mod already sends.
        //
        // The request/reply pair is exactly that: a sequence number goes out, comes back on the
        // report, and the gap is the round trip. It measures slightly more than the wire - a frame
        // of server processing rides along - and that is arguably the more useful figure, since it
        // is how long an action actually takes to be acknowledged. Reported as a round trip rather
        // than as a ping so the two are never confused.

        private static int _seq;
        private static int _awaitingSeq = -1;
        private static float _awaitingSince;

        /// <summary>Smoothed round trip to the server, in milliseconds. 0 until one has completed.</summary>
        internal static float RoundTripMs { get; private set; }

        /// <summary>Server side: the last round trip each player reported, by peer uid.</summary>
        private static readonly System.Collections.Generic.Dictionary<long, float> _peerRtt =
            new System.Collections.Generic.Dictionary<long, float>();

        /// <summary>Server side: what this player last measured, or 0 if they never told us.</summary>
        internal static float PeerRoundTripMs(long uid) =>
            _peerRtt.TryGetValue(uid, out float ms) ? ms : 0f;

        /// <summary>
        /// Folds a completed round trip into the smoothed figure.
        ///
        /// Smoothed rather than taken raw because a single sample carries whatever the server was
        /// doing that frame, and a latency readout that jumped forty milliseconds every second
        /// would be read as jitter that is not there. The jitter measurement wants the opposite, so
        /// the per-second samples the sampler records are what it works from.
        /// </summary>
        private static void RecordRoundTrip(float ms)
        {
            if (ms <= 0f || ms > 10000f) return;
            RoundTripMs = RoundTripMs <= 0f ? ms : (RoundTripMs * 2f + ms) / 3f;
        }

        internal static ServerReport Latest { get; private set; }
        internal static ServerModule Module { get; private set; } = ServerModule.Unknown;

        /// <summary>Seconds since the newest server report, or -1 if there has never been one.</summary>
        internal static float ReportAge =>
            Latest == null ? -1f : Time.unscaledTime - Latest.ReceivedAt;

        /// <summary>
        /// Registers both RPCs, once per ZRoutedRpc instance.
        ///
        /// Each session builds a new instance, which is what makes a reference comparison the
        /// right test for "needs registering again". Two details of the game's own code matter:
        /// ZRoutedRpc.Register adds to a dictionary with Add, so registering a name twice on one
        /// instance throws, and the singleton is never cleared on shutdown, so the old instance is
        /// still handed out after leaving a world. Remembering the instance even when registration
        /// fails keeps a bad case from throwing on every frame.
        /// </summary>
        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;
            _registeredOn = rpc;
            try
            {
                rpc.Register<ZPackage>(RpcRequest, RPC_Request);
                rpc.Register<ZPackage>(RpcReport, RPC_Report);
                rpc.Register<ZPackage>(RpcCapture, RPC_Capture);
                rpc.Register<ZPackage>(RpcCaptureResult, RPC_CaptureResult);
                DiagnoseServerLagMod.Log.LogInfo("[DiagnoseServerLag] Diagnostic RPCs registered.");
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogError($"[DiagnoseServerLag] Could not register the diagnostic RPCs: {e}");
            }
        }

        /// <summary>Forgets the world we just left. Registration is left alone; see Register.</summary>
        internal static void Reset()
        {
            Latest = null;
            Module = ServerModule.Unknown;
            _lastAskedAt = -999f;
            _unanswered = 0;
            RoundTripMs = 0f;
            _awaitingSeq = -1;
            _peerRtt.Clear();
        }

        internal static void Update()
        {
            Register();
            if (ZNet.instance == null) return;

            // Hosting: the server half is this process, so there is nothing to ask and no delay
            // between the two halves of the diagnosis.
            //
            // Rebuilt once a second rather than every frame. The measurements only change once a
            // second anyway, and building a report walks the baseline window into a new list - on a
            // dedicated server, at its uncapped tick rate, that would be hundreds of throwaway
            // lists a second from the mod whose entire purpose is to find out what is loading the
            // server.
            if (Sampler.IsServerHere)
            {
                Module = ServerModule.Local;
                // Kept up to date on a headless server too, with nobody to show it to, because the
                // capture a client asks for is built from it. An earlier version of this comment
                // claimed it was what made dsl_why work on the dedicated server's own console; that
                // console does not exist. Valheim's dedicated server reads nothing from stdin -
                // verified by sending the real server a plain "save" and watching it do nothing -
                // so every Terminal.ConsoleCommand in this mod is reachable only from a client.
                if (Latest != null && Time.unscaledTime - Latest.ReceivedAt < 1f) return;
                Latest = ServerReport.FromLocal(includePeerDetail: true);
                Latest.ReceivedAt = Time.unscaledTime;
                return;
            }

            float every = LagPanel.IsOpen
                ? DslConfig.WatchSeconds.Value
                : DslConfig.BackgroundSeconds.Value;
            // Zero in the background means "ask only while I am looking", which leaves a client
            // that never opens the panel sending nothing.
            if (every <= 0f) return;
            if (Time.unscaledTime - _lastAskedAt < every) return;
            Ask();
        }

        /// <summary>Asks the server for its side of the picture.</summary>
        internal static void Ask()
        {
            var rpc = ZRoutedRpc.instance;
            var znet = ZNet.instance;
            if (rpc == null || znet == null || znet.IsServer()) return;

            _lastAskedAt = Time.unscaledTime;
            if (Module != ServerModule.Present && ++_unanswered >= SilenceBeforeAbsent)
                Module = ServerModule.Absent;

            // The overload with no target is the game's own "send to the server": it resolves the
            // server's peer id internally. Naming a target here would mean reproducing that lookup,
            // and getting it wrong by falling back to Everybody would broadcast a request to every
            // player rather than failing quietly.
            //
            // The payload carries a sequence number, echoed back on the report to close the round
            // trip, and whatever we last measured - the server keeps that per player so an admin's
            // Players table can show a real latency for everyone running the mod, which no socket
            // on a dedicated server can provide.
            var pkg = new ZPackage();
            pkg.Write(++_seq);
            pkg.Write(RoundTripMs);
            _awaitingSeq = _seq;
            _awaitingSince = Time.unscaledTime;
            rpc.InvokeRoutedRPC(RpcRequest, pkg);
        }

        /// <summary>A client wants our numbers. Only a server answers.</summary>
        private static void RPC_Request(long sender, ZPackage pkg)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                if (!DslConfig.AnswerClients.Value) return;

                // Both fields are optional: a client older than 0.3.2 sends an empty package, and
                // reading past the end would throw where doing nothing is correct.
                int seq = 0;
                try
                {
                    seq = pkg.ReadInt();
                    float theirRtt = pkg.ReadSingle();
                    if (theirRtt > 0f && theirRtt < 10000f) _peerRtt[sender] = theirRtt;
                }
                catch { /* an older client, or no payload */ }

                var report = ServerReport.FromLocal(includePeerDetail: MaySeePeerDetail(znet, sender));
                report.Echo = seq;
                ZRoutedRpc.instance?.InvokeRoutedRPC(sender, RpcReport, report.Pack());
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogWarning($"[DiagnoseServerLag] Could not answer a diagnostic request: {e.Message}");
            }
        }

        /// <summary>The server answered.</summary>
        private static void RPC_Report(long sender, ZPackage pkg)
        {
            var report = ServerReport.Unpack(pkg);
            if (report == null) return;
            report.ReceivedAt = Time.unscaledTime;
            // Only the reply to the request still outstanding closes a round trip; a late or
            // duplicated one would otherwise be timed from the wrong send.
            if (report.Echo != 0 && report.Echo == _awaitingSeq)
            {
                RecordRoundTrip((Time.unscaledTime - _awaitingSince) * 1000f);
                _awaitingSeq = -1;
            }
            Latest = report;
            Module = ServerModule.Present;
            _unanswered = 0;
        }

        /// <summary>
        /// Asks the server to take a capture of itself and send back the summary.
        ///
        /// This exists because a Valheim dedicated server has no console to type into. Console
        /// commands registered with Terminal.ConsoleCommand only ever reach the in-game console,
        /// which needs a client; the server process reads nothing from stdin, verified against the
        /// real server by sending it a plain "save" and watching nothing happen. So dsl_bench, which
        /// was written for exactly that machine, could not be run on it at all - the measurement had
        /// to be reachable from a client or it was unreachable.
        ///
        /// Admin only: it writes a file on the server, and that is not something any player passing
        /// through should be able to ask for repeatedly.
        /// </summary>
        internal static void AskCapture(int seconds)
        {
            var rpc = ZRoutedRpc.instance;
            var znet = ZNet.instance;
            if (rpc == null || znet == null) return;

            // Hosting: the server is this process, so there is nobody to ask.
            if (znet.IsServer())
            {
                string local = Commands.Bench(seconds, out string localPath);
                Print(local + (localPath == null ? "" : "\nwrote " + localPath));
                return;
            }

            var pkg = new ZPackage();
            pkg.Write(seconds);
            rpc.InvokeRoutedRPC(RpcCapture, pkg);
        }

        /// <summary>A client asked us to capture. Server side only, and only for an admin.</summary>
        private static void RPC_Capture(long sender, ZPackage pkg)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;

                int seconds = 120;
                try { seconds = pkg.ReadInt(); } catch { /* an older client sent nothing */ }
                seconds = Mathf.Clamp(seconds, 5, Sampler.History.Capacity);

                if (!IsAdmin(znet, sender))
                {
                    ZRoutedRpc.instance?.InvokeRoutedRPC(sender, RpcCaptureResult,
                        Wrap("Capturing the server needs admin rights."));
                    return;
                }

                string text = Commands.Bench(seconds, out string path);
                if (path != null) text += "\nwrote " + path + " on the server";
                DiagnoseServerLagMod.Log.LogInfo("[DiagnoseServerLag] capture requested by a client\n" + text);
                ZRoutedRpc.instance?.InvokeRoutedRPC(sender, RpcCaptureResult, Wrap(text));
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogWarning($"[DiagnoseServerLag] Could not take a capture: {e.Message}");
            }
        }

        /// <summary>The server sent back what it measured.</summary>
        private static void RPC_CaptureResult(long sender, ZPackage pkg)
        {
            string text;
            try { text = pkg.ReadString(); }
            catch { return; }
            if (string.IsNullOrEmpty(text)) return;
            Print(text);
        }

        private static ZPackage Wrap(string text)
        {
            var pkg = new ZPackage();
            pkg.Write(text ?? "");
            return pkg;
        }

        /// <summary>
        /// Puts a block of text where the person who asked for it will see it.
        ///
        /// The reply arrives long after the console command that triggered it has returned, so it
        /// cannot be handed back through the command's own context. The console is where it was
        /// asked for and where it is wanted; the log keeps it after the console scrolls.
        /// </summary>
        private static void Print(string text)
        {
            DiagnoseServerLagMod.Log.LogInfo("[DiagnoseServerLag] " + text);
            try { Console.instance?.AddString(text); }
            catch { /* no console open; the log still has it */ }
            DiagnoseServerLagMod.Message("Server capture ready - see the console (F5)");
        }

        /// <summary>Whether this asker may make the server do work and write a file.</summary>
        private static bool IsAdmin(ZNet znet, long sender)
        {
            if (sender == ZDOMan.GetSessionID()) return true;        // the hosting player
            var peer = znet.GetPeer(sender);
            if (peer?.m_socket == null) return false;
            string host = peer.m_socket.GetHostName();
            return !string.IsNullOrEmpty(host) && znet.IsAdmin(host);
        }

        /// <summary>
        /// Whether this asker gets the per-player table.
        ///
        /// The aggregate numbers are what diagnose the server and go to everyone; the per-player
        /// rows name who is on a bad line and roughly where they are standing, which is a
        /// different thing to hand out. Admins always get them because an admin is the person who
        /// would act on them, and a server that wants the whole group debugging together can set
        /// Share Peer Detail and give them to everyone.
        /// </summary>
        private static bool MaySeePeerDetail(ZNet znet, long sender)
        {
            if (DslConfig.SharePeerDetail.Value) return true;
            if (sender == ZDOMan.GetSessionID()) return true;        // the hosting player
            var peer = znet.GetPeer(sender);
            if (peer?.m_socket == null) return false;
            string host = peer.m_socket.GetHostName();
            return !string.IsNullOrEmpty(host) && znet.IsAdmin(host);
        }
    }
}

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

        /// <summary>Asked this many times with no answer before the server is called unmodded.</summary>
        private const int SilenceBeforeAbsent = 3;

        private static ZRoutedRpc _registeredOn;
        private static float _lastAskedAt = -999f;
        private static int _unanswered;

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
                // Kept up to date on a headless server too, with nobody to show it to: it is what
                // makes dsl_why and dsl_server work on the dedicated server's own console, which is
                // the only screen that machine has. Without it the server would run the diagnosis
                // against itself and conclude the server could not be measured.
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
            // An empty payload today; having one at all means the request can carry options later
            // without needing a second RPC name and a second round of version skew.
            rpc.InvokeRoutedRPC(RpcRequest, new ZPackage());
        }

        /// <summary>A client wants our numbers. Only a server answers.</summary>
        private static void RPC_Request(long sender, ZPackage pkg)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                if (!DslConfig.AnswerClients.Value) return;

                var report = ServerReport.FromLocal(includePeerDetail: MaySeePeerDetail(znet, sender));
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
            Latest = report;
            Module = ServerModule.Present;
            _unanswered = 0;
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

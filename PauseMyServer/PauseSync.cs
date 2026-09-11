using System.Collections.Generic;

namespace PauseMyServer
{
    /// <summary>
    /// The pause state machine. The server is the single authority: a client only ever says
    /// "I want the world paused" (its ESC menu is open) and the server decides, then tells
    /// everybody what the state is. A client freezes only after the server has confirmed, so a
    /// server without the mod simply never pauses instead of leaving the client frozen while the
    /// world runs on without it.
    ///
    /// v1.0 rule: the world is paused while exactly one player is connected to a dedicated server
    /// and that player wants it paused. A second player becoming ready, or the pausing player
    /// leaving, resumes the world on the next frame.
    /// </summary>
    internal static class PauseSync
    {
        private const string RpcWant  = "PMS_WantPause";   // client -> server: bool
        private const string RpcState = "PMS_PauseState";  // server -> everybody: bool

        // ── server ──────────────────────────────────────────────────────────────────

        /// <summary>True while the server holds the world paused. Only ever set on the server.</summary>
        internal static bool ServerPaused { get; private set; }

        private static readonly Dictionary<long, bool> _wants = new Dictionary<long, bool>();
        private static readonly List<long> _gone = new List<long>();
        private static string _pausedBy = "";

        // ── client ──────────────────────────────────────────────────────────────────

        /// <summary>What the server last told us.</summary>
        internal static bool ClientPaused { get; private set; }

        /// <summary>Mirrors Game.m_pause: true between Game.Pause() and Game.Unpause(), i.e. while the ESC menu is open.</summary>
        internal static bool WantPause;

        private static bool _sentWant;

        // ── lifecycle ───────────────────────────────────────────────────────────────

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            rpc.Register<bool>(RpcWant, RPC_WantPause);
            rpc.Register<bool>(RpcState, RPC_PauseState);
        }

        internal static void Reset()
        {
            ServerPaused = false;
            _wants.Clear();
            _pausedBy = "";
            ClientPaused = false;
            WantPause = false;
            _sentWant = false;
        }

        internal static void Update()
        {
            var znet = ZNet.instance;
            if (znet == null || ZRoutedRpc.instance == null) return;
            if (znet.IsServer()) UpdateServer(znet);
            else UpdateClient();
        }

        // ── client side ─────────────────────────────────────────────────────────────

        private static void UpdateClient()
        {
            if (WantPause == _sentWant) return;
            _sentWant = WantPause;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcWant, WantPause);
        }

        private static void RPC_PauseState(long sender, bool paused)
        {
            var znet = ZNet.instance;
            if (znet == null || znet.IsServer()) return;          // the host's own broadcast
            var server = znet.GetServerPeer();
            if (server != null && sender != server.m_uid) return; // only the server decides
            if (paused == ClientPaused) return;

            ClientPaused = paused;
            PauseMyServerMod.Log.LogInfo(paused ? "[PauseMyServer] World paused." : "[PauseMyServer] World resumed.");
            if (paused)
            {
                PauseMyServerMod.Message("World paused");
            }
            else if (WantPause)
            {
                // Resumed while our menu is still open: somebody else came online.
                PauseMyServerMod.Message("World resumed: another player is online");
            }
        }

        // ── server side ─────────────────────────────────────────────────────────────

        private static void RPC_WantPause(long sender, bool want)
        {
            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return;
            if (znet.GetPeer(sender) == null) return;             // unknown sender
            _wants[sender] = want;
            // The state is re-evaluated in UpdateServer on the next frame.
        }

        private static void UpdateServer(ZNet znet)
        {
            int ready = 0;
            long sole = 0;
            string soleName = "";
            var peers = znet.GetPeers();
            for (int i = 0; i < peers.Count; i++)
            {
                var p = peers[i];
                if (!p.IsReady()) continue;
                ready++;
                sole = p.m_uid;
                soleName = p.m_playerName;
            }

            // Forget the wishes of players who left.
            if (_wants.Count > 0)
            {
                _gone.Clear();
                foreach (var uid in _wants.Keys)
                {
                    if (znet.GetPeer(uid) == null) _gone.Add(uid);
                }
                for (int i = 0; i < _gone.Count; i++) _wants.Remove(_gone[i]);
            }

            // v1.0: pause only for a lone player on a dedicated server. A hosting player counts as
            // a player too, and vanilla already pauses a host who is alone.
            bool hostIsPlaying = Player.m_localPlayer != null;
            bool should = !hostIsPlaying && ready == 1 && _wants.TryGetValue(sole, out bool want) && want;
            if (should == ServerPaused) return;

            ServerPaused = should;
            if (should)
            {
                _pausedBy = soleName;
                PauseMyServerMod.Log.LogInfo($"[PauseMyServer] World paused by {soleName}.");
            }
            else
            {
                PauseMyServerMod.Log.LogInfo($"[PauseMyServer] World resumed (was paused by {_pausedBy}; {ready} player(s) connected).");
                _pausedBy = "";
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcState, should);
        }
    }
}

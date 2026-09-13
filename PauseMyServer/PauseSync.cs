using System.Collections.Generic;

namespace PauseMyServer
{
    /// <summary>
    /// The pause state machine. The server is the single authority: clients only ever send
    /// wishes ("my menu is open", "toggle the admin pause") and the server decides, then tells
    /// everybody what the state is. A client freezes only after the server has confirmed, so a
    /// server without the mod simply never pauses instead of leaving the client frozen while the
    /// world runs on without it.
    ///
    /// Two independent reasons keep the world paused:
    ///  * everyone wants it: every player online has the ESC menu open. Alone, that is just you,
    ///    exactly like solo. Anyone closing their menu, or a new player becoming ready, lifts it.
    ///  * admin pause: an admin toggled it (pause key or console). It freezes every client
    ///    regardless of menus, players joining meanwhile are frozen too, and it stays until any
    ///    admin lifts it. As a safety net it is dropped when the last player leaves, so the next
    ///    player to log in is never frozen with nobody able to resume.
    /// </summary>
    internal static class PauseSync
    {
        private const string RpcWant         = "PMS_WantPause";     // client -> server: bool, menu open?
        private const string RpcAdminToggle  = "PMS_AdminToggle";   // client -> server: toggle the admin pause
        private const string RpcRequestState = "PMS_RequestState";  // client -> server: send me the state
        private const string RpcState        = "PMS_PauseState";    // server -> client(s): bool paused, bool forced, string by
        private const string RpcCounts       = "PMS_Counts";        // server -> client(s): int wanting, int online
        private const string RpcNotice       = "PMS_Notice";        // server -> client: string message

        // ── server ──────────────────────────────────────────────────────────────────

        /// <summary>True while the server holds the world paused for any reason. Only ever set on the server.</summary>
        internal static bool ServerPaused { get; private set; }

        /// <summary>The admin pause, independent of the everyone-wants rule.</summary>
        internal static bool AdminPaused { get; private set; }
        internal static string AdminPausedBy { get; private set; } = "";

        private static readonly Dictionary<long, bool> _wants = new Dictionary<long, bool>();
        private static readonly List<long> _gone = new List<long>();
        private static string _resumedBy = "";
        private static bool _sentForced;
        private static string _sentBy = "";
        private static int _sentWants = -1;
        private static int _sentTotal = -1;
        private static int _lastReady;

        // ── client ──────────────────────────────────────────────────────────────────

        /// <summary>What the server last told us.</summary>
        internal static bool ClientPaused { get; private set; }

        /// <summary>True when the pause is an admin pause: it applies whether or not our menu is open.</summary>
        internal static bool ClientForced { get; private set; }

        /// <summary>Who holds the pause (the admin, or ourselves for a lone pause).</summary>
        internal static string PausedBy { get; private set; } = "";

        /// <summary>How many players want the pause, and how many are online, as last reported by the server (0 until it reports).</summary>
        internal static int WantCount { get; private set; }
        internal static int PlayerCount { get; private set; }

        /// <summary>Mirrors Game.m_pause: true between Game.Pause() and Game.Unpause(), i.e. while the ESC menu is open.</summary>
        internal static bool WantPause;

        private static bool _sentWant;

        // ── lifecycle ───────────────────────────────────────────────────────────────

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            rpc.Register<bool>(RpcWant, RPC_WantPause);
            rpc.Register(RpcAdminToggle, RPC_AdminToggle);
            rpc.Register(RpcRequestState, RPC_RequestState);
            rpc.Register<bool, bool, string>(RpcState, RPC_PauseState);
            rpc.Register<int, int>(RpcCounts, RPC_Counts);
            rpc.Register<string>(RpcNotice, RPC_Notice);
        }

        internal static void Reset()
        {
            ServerPaused = false;
            AdminPaused = false;
            AdminPausedBy = "";
            _wants.Clear();
            _resumedBy = "";
            _sentForced = false;
            _sentBy = "";
            _sentWants = -1;
            _sentTotal = -1;
            _lastReady = 0;
            ClientPaused = false;
            ClientForced = false;
            PausedBy = "";
            WantCount = 0;
            PlayerCount = 0;
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

        internal static string Status()
        {
            var znet = ZNet.instance;
            if (znet == null) return "Not in a game.";
            string client = Player.m_localPlayer == null ? "" :
                $" | client: paused={ClientPaused} admin={ClientForced} by='{PausedBy}' menu={WantPause} wanting={WantCount}/{PlayerCount}";
            string server = !znet.IsServer() ? "" :
                $"server: paused={ServerPaused} admin={AdminPaused} by='{AdminPausedBy}' wanting={_sentWants}/{_sentTotal} players={_lastReady}";
            return (server + client).TrimStart(' ', '|');
        }

        // ── client side ─────────────────────────────────────────────────────────────

        private static void UpdateClient()
        {
            if (WantPause == _sentWant) return;
            _sentWant = WantPause;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcWant, WantPause);
        }

        /// <summary>The admin pause key. Runs on clients and on a hosting player alike.</summary>
        internal static void UpdateInput()
        {
            if (Player.m_localPlayer == null || ZRoutedRpc.instance == null) return;
            if (!PauseMyServerMod.PauseKey.Value.IsDown()) return;
            if (global::Console.IsVisible() || TextInput.IsVisible()) return;
            if (Chat.instance != null && Chat.instance.HasFocus()) return;
            SendAdminToggle();
        }

        internal static void SendAdminToggle()
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcAdminToggle);
        }

        /// <summary>Asked on spawn, so a player who joins during an admin pause is frozen too.</summary>
        internal static void RequestState()
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcRequestState);
        }

        /// <summary>True on a client, or on a hosting player; false on a dedicated server or for a spoofed sender.</summary>
        private static bool FromServer(long sender)
        {
            var znet = ZNet.instance;
            if (znet == null) return false;
            if (znet.IsServer() && Player.m_localPlayer == null) return false;   // dedicated server: its own broadcast
            var server = znet.GetServerPeer();
            return server == null || sender == server.m_uid;                    // only the server decides
        }

        private static void RPC_PauseState(long sender, bool paused, bool forced, string by)
        {
            if (!FromServer(sender)) return;

            bool wasPaused = ClientPaused;
            bool wasForced = ClientForced;
            ClientPaused = paused;
            ClientForced = paused && forced;
            PausedBy = by ?? "";
            if (paused == wasPaused && ClientForced == wasForced) return;

            PauseMyServerMod.Log.LogInfo(paused
                ? $"[PauseMyServer] Game paused{(forced ? " by admin " + PausedBy : "")}."
                : "[PauseMyServer] Game resumed.");
            if (paused) return;

            // The pause itself is announced by the persistent PauseOverlay label; only resumes
            // that the player did not cause themselves get a message.
            if (wasForced) PauseMyServerMod.Message(string.IsNullOrEmpty(PausedBy) ? "Game resumed" : $"Game resumed by {PausedBy}");
            else if (WantPause) PauseMyServerMod.Message("Game resumed: not everyone is paused");
        }

        private static void RPC_Counts(long sender, int wanting, int online)
        {
            if (!FromServer(sender)) return;
            WantCount = wanting;
            PlayerCount = online;
        }

        private static void RPC_Notice(long sender, string text)
        {
            if (Player.m_localPlayer == null) return;
            PauseMyServerMod.Message(text, always: true);
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

        private static void RPC_AdminToggle(long sender)
        {
            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return;
            string name = PeerName(znet, sender);
            if (!IsAdmin(znet, sender))
            {
                PauseMyServerMod.Log.LogInfo($"[PauseMyServer] {name} pressed the pause key but is not an admin.");
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcNotice, "Only an admin can pause the server");
                return;
            }
            SetAdminPause(!AdminPaused, name);
        }

        private static void RPC_RequestState(long sender)
        {
            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcCounts, _sentWants < 0 ? 0 : _sentWants, _sentTotal < 0 ? 0 : _sentTotal);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcState, ServerPaused, _sentForced, _sentBy);
        }

        /// <summary>Server only. Also used by the console command; the broadcast follows on the next frame.</summary>
        internal static void SetAdminPause(bool paused, string by)
        {
            if (paused == AdminPaused) return;
            AdminPaused = paused;
            if (paused) { AdminPausedBy = by; _resumedBy = ""; }
            else        { _resumedBy = by; AdminPausedBy = ""; }
            PauseMyServerMod.Log.LogInfo($"[PauseMyServer] Admin pause {(paused ? "set" : "lifted")} by {by}.");
        }

        private static bool IsAdmin(ZNet znet, long sender)
        {
            if (sender == ZDOMan.GetSessionID()) return true;     // the hosting player
            var peer = znet.GetPeer(sender);
            if (peer == null || peer.m_socket == null) return false;
            string host = peer.m_socket.GetHostName();
            return !string.IsNullOrEmpty(host) && znet.IsAdmin(host);
        }

        private static string PeerName(ZNet znet, long sender)
        {
            if (sender == ZDOMan.GetSessionID())
                return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "the server";
            var peer = znet.GetPeer(sender);
            return peer != null && !string.IsNullOrEmpty(peer.m_playerName) ? peer.m_playerName : "an admin";
        }

        private static void UpdateServer(ZNet znet)
        {
            int ready = 0;
            int wanting = 0;
            string soleName = "";
            var peers = znet.GetPeers();
            for (int i = 0; i < peers.Count; i++)
            {
                var p = peers[i];
                if (!p.IsReady()) continue;
                ready++;
                soleName = p.m_playerName;
                if (_wants.TryGetValue(p.m_uid, out bool w) && w) wanting++;
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

            // Safety net: the last player leaving drops an admin pause (a pause set from the
            // console on an already empty server is kept on purpose).
            if (AdminPaused && ready == 0 && _lastReady > 0 && Player.m_localPlayer == null)
            {
                PauseMyServerMod.Log.LogInfo("[PauseMyServer] Server is empty; admin pause dropped.");
                AdminPaused = false;
                AdminPausedBy = "";
                _resumedBy = "";
            }
            _lastReady = ready;

            // Everyone-wants rule. A hosting player is a player too, with a menu of their own; a
            // host who is alone is vanilla's job (it pauses by itself) and is left out here.
            bool hostIsPlaying = Player.m_localPlayer != null;
            int total = ready + (hostIsPlaying ? 1 : 0);
            int wants = wanting + (hostIsPlaying && WantPause ? 1 : 0);
            bool everyone = total > 0 && wants == total && !(hostIsPlaying && ready == 0);

            if (wants != _sentWants || total != _sentTotal)
            {
                _sentWants = wants;
                _sentTotal = total;
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcCounts, wants, total);
            }

            bool should = everyone || AdminPaused;
            bool forced = AdminPaused;
            string by = AdminPaused ? AdminPausedBy : (everyone ? (total == 1 ? soleName : "everyone") : _resumedBy);
            if (should == ServerPaused && forced == _sentForced && by == _sentBy) return;

            ServerPaused = should;
            _sentForced = forced;
            _sentBy = by;
            if (should) _resumedBy = "";   // a new pause; whoever lifted the last one is history
            PauseMyServerMod.Log.LogInfo(should
                ? $"[PauseMyServer] World paused ({(forced ? "admin: " : "wanted by ")}{by}; {total} player(s) online)."
                : $"[PauseMyServer] World resumed ({wants} of {total} player(s) want a pause{(string.IsNullOrEmpty(by) ? "" : "; lifted by " + by)}).");
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcState, should, forced, by);
        }
    }
}

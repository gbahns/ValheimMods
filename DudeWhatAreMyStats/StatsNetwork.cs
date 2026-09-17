using System;
using System.Collections.Generic;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// Gets everyone else's stats, from two places.
    ///
    /// Players who are online answer for themselves: a request goes to everyone and each client
    /// that runs this mod reads its own profile and replies straight to whoever asked. Routed RPCs
    /// are forwarded by the server whether or not it knows the method, so that half works on any
    /// server, modded or not, and players without the mod simply never answer.
    ///
    /// Players who are not online are remembered by the server, if the server runs this mod too.
    /// Clients push their own snapshot now and then, the server keeps the newest per character in
    /// <see cref="StatsStore"/>, and hands the lot back on request. A server without the mod never
    /// answers that, so the panel falls back to showing only whoever is online.
    ///
    /// Live answers always win over the stored copy: the store is for people who are not here.
    /// </summary>
    internal static class StatsNetwork
    {
        private const string RpcRequest  = "DWAMS_Request";    // asker -> everybody
        private const string RpcResponse = "DWAMS_Response";   // answerer -> asker, one snapshot
        private const string RpcPush     = "DWAMS_Push";       // client -> server, one snapshot
        private const string RpcAskAll   = "DWAMS_AskAll";     // client -> server, no payload yet
        private const string RpcAll      = "DWAMS_All";        // server -> client, many snapshots
        private const string RpcForget   = "DWAMS_Forget";     // client -> server, "take me off the board"

        /// <summary>Bumped when the many-snapshots message changes shape.</summary>
        private const int AllSchema = 1;

        /// <summary>After a request goes out, judge who is online by the previous round for this long.</summary>
        private const float SettleSeconds = 3f;

        /// <summary>Least time between asking the server for its whole stored roster.</summary>
        private const float ServerAskSeconds = 60f;

        /// <summary>Least time the server will serve the same player the whole roster again.</summary>
        private const float ServeCooldown = 10f;

        private static readonly Dictionary<long, Snapshot> _remote = new Dictionary<long, Snapshot>();
        private static readonly Dictionary<long, Snapshot> _stored = new Dictionary<long, Snapshot>();
        private static ZRoutedRpc _registeredOn;
        private static float _lastRequestAt = -999f;
        private static float _prevRequestAt = -999f;
        private static float _nextAutoRequestAt;
        private static float _nextPushAt;
        private static float _nextServerAskAt;
        private static bool _serverAnswered;
        private static bool _forgetSent;
        private static readonly Dictionary<long, float> _servedAt = new Dictionary<long, float>();

        internal static int KnownCount => _remote.Count;

        internal static int StoredCount => _stored.Count;

        /// <summary>True once the server has answered the stored-stats request at least once.</summary>
        internal static bool ServerHasStore => _serverAnswered;

        /// <summary>
        /// Registers the RPCs, once per ZRoutedRpc instance. Each new session builds a new one,
        /// which is what makes the reference check the right test for "needs registering again".
        ///
        /// Two details of the game's own code matter here. ZRoutedRpc.Register adds to a dictionary
        /// with Add, so registering the same name twice on one instance throws; and ZRoutedRpc's
        /// singleton is never cleared on shutdown, so the old instance is still handed out after
        /// leaving a world. Remembering the instance even when registration fails keeps a bad case
        /// from turning into a thrown exception on every frame.
        /// </summary>
        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, _registeredOn)) return;
            _registeredOn = rpc;
            try
            {
                rpc.Register<ZPackage>(RpcRequest, RPC_Request);
                rpc.Register<ZPackage>(RpcResponse, RPC_Response);
                rpc.Register<ZPackage>(RpcPush, RPC_Push);
                rpc.Register<ZPackage>(RpcAskAll, RPC_AskAll);
                rpc.Register<ZPackage>(RpcAll, RPC_All);
                rpc.Register<ZPackage>(RpcForget, RPC_Forget);
                DudeWhatAreMyStatsMod.Log.LogInfo("[DudeWhatAreMyStats] Stats RPCs registered.");
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogError($"[DudeWhatAreMyStats] Could not register the stats RPCs: {e}");
            }
        }

        /// <summary>
        /// Forgets everything learned in the world we just left. The registration is deliberately
        /// left alone: the RPCs stay registered on that instance, and re-registering on it would
        /// throw, so only a genuinely new instance triggers registration again.
        /// </summary>
        internal static void Reset()
        {
            _remote.Clear();
            _stored.Clear();
            _lastRequestAt = -999f;
            _prevRequestAt = -999f;
            _nextAutoRequestAt = 0f;
            _nextPushAt = 0f;
            _nextServerAskAt = 0f;
            _serverAnswered = false;
            _forgetSent = false;
            _servedAt.Clear();
        }

        internal static void Update()
        {
            Register();
            PushTick();
            if (!StatsPanel.IsOpen) return;
            float every = DwamsConfig.RefreshSeconds != null ? DwamsConfig.RefreshSeconds.Value : 10f;
            if (every <= 0f) return;
            // Unscaled: the panel can be up while the game is paused, and it should still refresh.
            if (Time.unscaledTime < _nextAutoRequestAt) return;
            _nextAutoRequestAt = Time.unscaledTime + every;
            Request();
        }

        // ── asking the people who are here ──────────────────────────────────────────

        /// <summary>
        /// Asks everyone online for their stats and, unless told not to, the server for everyone it
        /// remembers. A caller that only shows online players has no use for the stored roster, which
        /// is the largest message this mod sends, so it can leave that part out.
        /// </summary>
        internal static void Request(bool includeServer = true)
        {
            if (DwamsConfig.AskOtherPlayers != null && !DwamsConfig.AskOtherPlayers.Value) return;
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || ZNet.instance == null) return;
            _prevRequestAt = _lastRequestAt;
            _lastRequestAt = Time.unscaledTime;
            _nextAutoRequestAt = Time.unscaledTime + Mathf.Max(1f, DwamsConfig.RefreshSeconds != null ? DwamsConfig.RefreshSeconds.Value : 10f);
            // An empty package: the request carries nothing today, but having a payload means the
            // message shape can grow later without a second RPC name.
            rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcRequest, new ZPackage());
            if (includeServer) AskServer();
        }

        /// <summary>Everything again, right now: the Refresh button and the console command.</summary>
        internal static void RequestNow()
        {
            _nextServerAskAt = 0f;
            Request();
        }

        /// <summary>Someone asked for our stats. Dedicated servers have no character and stay quiet.</summary>
        private static void RPC_Request(long sender, ZPackage pkg)
        {
            try
            {
                // Opting out is symmetric: a player who does not ask is not asked either, so the
                // setting really does keep the panel to yourself instead of quietly still
                // publishing your stats to everyone else's scoreboard.
                if (DwamsConfig.AskOtherPlayers != null && !DwamsConfig.AskOtherPlayers.Value) return;
                if (ZNet.instance == null || ZNet.instance.IsDedicated()) return;
                if (Player.m_localPlayer == null || Game.instance == null) return;
                if (sender == ZNet.GetUID()) return;   // our own broadcast came back to us
                var snap = LocalStats.Read();
                if (snap == null) return;
                ZRoutedRpc.instance?.InvokeRoutedRPC(sender, RpcResponse, snap.Pack());
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogWarning($"[DudeWhatAreMyStats] Could not answer a stats request: {e.Message}");
            }
        }

        /// <summary>A player answered. Their newest snapshot replaces any older one.</summary>
        private static void RPC_Response(long sender, ZPackage pkg)
        {
            var snap = Snapshot.Unpack(pkg);
            if (snap == null) return;
            snap.PeerId = sender;
            snap.IsLocal = false;
            snap.FromStore = false;
            snap.ReceivedAt = Time.unscaledTime;
            // Key on the character, not the connection, so a reconnect updates one row instead of
            // adding a second. A player who switches characters gets a row each, which is honest.
            _remote[snap.Identity] = snap;
            StatsPanel.OnRosterChanged(snap.Identity);
        }

        // ── telling the server, so it can speak for us later ────────────────────────

        /// <summary>
        /// Keeps the server's copy of our own stats current, whether or not the panel is open, and
        /// asks it to forget us if we have opted out. A server without this mod drops both.
        /// </summary>
        private static void PushTick()
        {
            if (Player.m_localPlayer == null) return;
            bool participating = DwamsConfig.AskOtherPlayers == null || DwamsConfig.AskOtherPlayers.Value;
            if (!participating)
            {
                // Opting out has to reach the server too, or the record pushed before the switch was
                // flipped would keep us on everyone's board for the whole keep window.
                if (!_forgetSent) SendForget();
                return;
            }
            _forgetSent = false;
            PushLocal();
        }

        /// <summary>
        /// Hands our own stats to the server: on a timer, on opening the panel, and on the way out
        /// of the world.
        ///
        /// The timer is checked here rather than by the callers, so no caller can bypass it by
        /// accident. Opening the panel repeatedly used to push every time, and since each push
        /// restarted the store's write delay, a player leaning on the key could stop the server
        /// writing its file at all. Only leaving the world forces a push.
        /// </summary>
        internal static void PushLocal(bool force = false)
        {
            try
            {
                if (DwamsConfig.AskOtherPlayers != null && !DwamsConfig.AskOtherPlayers.Value) return;
                float minutes = DwamsConfig.PushMinutes != null ? DwamsConfig.PushMinutes.Value : 5f;
                if (minutes <= 0f) return;   // opted out of being remembered at all
                if (!force && Time.unscaledTime < _nextPushAt) return;
                var rpc = ZRoutedRpc.instance;
                if (rpc == null || ZNet.instance == null) return;
                if (Player.m_localPlayer == null || Game.instance == null) return;
                var snap = LocalStats.Read();
                if (snap == null) return;
                _nextPushAt = Time.unscaledTime + Mathf.Max(30f, minutes * 60f);
                rpc.InvokeRoutedRPC(RpcPush, snap.Pack());
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogWarning($"[DudeWhatAreMyStats] Could not send our stats to the server: {e.Message}");
            }
        }

        /// <summary>Asks the server to drop our stored record, for a player who has opted out.</summary>
        private static void SendForget()
        {
            try
            {
                var rpc = ZRoutedRpc.instance;
                if (rpc == null || ZNet.instance == null || Game.instance == null) return;
                var profile = Game.instance.GetPlayerProfile();
                long id = profile != null ? profile.GetPlayerID() : 0L;
                if (id == 0L) return;
                _forgetSent = true;
                var pkg = new ZPackage();
                pkg.Write(id);
                rpc.InvokeRoutedRPC(RpcForget, pkg);
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogWarning($"[DudeWhatAreMyStats] Could not ask the server to forget us: {e.Message}");
            }
        }

        /// <summary>A player asked to be taken off the board. Server only.</summary>
        private static void RPC_Forget(long sender, ZPackage pkg)
        {
            try
            {
                if (!StatsStore.IsServer || !StatsStore.Loaded || pkg == null) return;
                pkg.SetPos(0);
                long id = pkg.ReadLong();
                if (id != 0L && StatsStore.Forget(id))
                    DudeWhatAreMyStatsMod.Log.LogInfo("[DudeWhatAreMyStats] Dropped a character's stored stats at their own request.");
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogWarning($"[DudeWhatAreMyStats] Could not handle a forget request: {e.Message}");
            }
        }

        /// <summary>A client told us how it is doing. Server only.</summary>
        private static void RPC_Push(long sender, ZPackage pkg)
        {
            try
            {
                if (!StatsStore.IsServer || !StatsStore.Loaded) return;
                var snap = Snapshot.Unpack(pkg);
                if (snap == null) return;
                snap.PeerId = sender;
                snap.IsLocal = false;
                StatsStore.Put(snap);
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogWarning($"[DudeWhatAreMyStats] Could not record a pushed snapshot: {e.Message}");
            }
        }

        // ── asking the server about the people who are not here ─────────────────────

        /// <summary>
        /// Asks the server for every character it remembers. Silently unanswered by a server
        /// without the mod.
        ///
        /// Throttled well below the live refresh: the stored copy is by definition of players who
        /// are not here, so it changes at the speed people log in and out, while the whole roster
        /// travels in one message. Refreshing by hand skips the throttle.
        /// </summary>
        internal static void AskServer()
        {
            if (DwamsConfig.RememberOfflinePlayers != null && !DwamsConfig.RememberOfflinePlayers.Value) return;
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || ZNet.instance == null) return;
            if (Time.unscaledTime < _nextServerAskAt) return;
            _nextServerAskAt = Time.unscaledTime + ServerAskSeconds;
            rpc.InvokeRoutedRPC(RpcAskAll, new ZPackage());
        }

        /// <summary>A client wants the stored roster. Server only.</summary>
        private static void RPC_AskAll(long sender, ZPackage pkg)
        {
            try
            {
                if (!StatsStore.IsServer || !StatsStore.Loaded) return;
                // The client throttles itself, but its Refresh button deliberately skips that, so
                // the floor lives here too: serving the whole roster costs a serialize and a large
                // message, and a held-down button should not be able to make the server do it.
                float now = Time.unscaledTime;
                if (_servedAt.TryGetValue(sender, out float last) && now - last < ServeCooldown) return;
                _servedAt[sender] = now;
                var all = StatsStore.All();
                var outPkg = new ZPackage();
                outPkg.Write(AllSchema);
                outPkg.Write(all.Count);
                foreach (var snap in all)
                    outPkg.Write(snap.Pack().GetArray());
                ZRoutedRpc.instance?.InvokeRoutedRPC(sender, RpcAll, outPkg);
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogWarning($"[DudeWhatAreMyStats] Could not send the stored roster: {e.Message}");
            }
        }

        /// <summary>The server sent everyone it remembers. This replaces whatever we held before.</summary>
        private static void RPC_All(long sender, ZPackage pkg)
        {
            try
            {
                if (pkg == null) return;
                pkg.SetPos(0);
                int schema = pkg.ReadInt();
                if (schema != AllSchema) return;
                int count = pkg.ReadInt();

                // Built to one side and swapped in only once the whole message has been read:
                // a truncated one would otherwise leave a half roster on screen until the next ask.
                var fresh = new Dictionary<long, Snapshot>();
                for (int i = 0; i < count; i++)
                {
                    var snap = Snapshot.Unpack(new ZPackage(pkg.ReadByteArray()));
                    if (snap == null) continue;
                    snap.IsLocal = false;
                    snap.FromStore = true;
                    snap.Online = false;
                    fresh[snap.Identity] = snap;
                }
                _stored.Clear();
                foreach (var kv in fresh) _stored[kv.Key] = kv.Value;
                _serverAnswered = true;
                StatsPanel.OnRosterRebuilt();
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogWarning($"[DudeWhatAreMyStats] Could not read the stored roster: {e.Message}");
            }
        }

        // ── what the panel shows ────────────────────────────────────────────────────

        /// <summary>
        /// Everyone we can show: the local character, then whoever answered just now, then whoever
        /// the server remembers and did not answer. A live answer always beats the stored copy.
        ///
        /// Who counts as online comes from the game's own list of connected players when it has one,
        /// not from how recently someone answered. Answer timing is a poor proxy: a player who has
        /// just died has no character for ten seconds and then spends several more respawning, and
        /// answers nothing the whole time, so a request landing in that gap would drop them off the
        /// list at the very moment their death count went up. The connection list is built from
        /// connected peers, not living characters, so a dead player stays on it. Answer timing is
        /// only the fallback, for the first moments after joining before that list arrives.
        ///
        /// The list names players rather than identifying them, so two characters sharing a name
        /// are online together whenever either is. That is the one thing this trades away.
        /// </summary>
        internal static List<Snapshot> Roster()
        {
            var list = new List<Snapshot>();
            var seen = new HashSet<long>();

            // A dedicated server has a player profile of its own that nobody plays, so the local
            // row exists only where there is a character standing in the world.
            var local = Player.m_localPlayer != null ? LocalStats.Read() : null;
            if (local != null)
            {
                list.Add(local);
                seen.Add(local.Identity);
            }

            bool byPresence = ReadConnectedNames();
            bool settling = Time.unscaledTime - _lastRequestAt < SettleSeconds;
            float cutoff = settling ? _prevRequestAt : _lastRequestAt;
            bool keepOffline = DwamsConfig.RememberOfflinePlayers == null || DwamsConfig.RememberOfflinePlayers.Value;

            foreach (var snap in _remote.Values)
            {
                if (!seen.Add(snap.Identity)) continue;   // our own echo
                snap.Online = byPresence ? IsConnected(snap) : snap.ReceivedAt >= cutoff - 0.5f;
                if (!snap.Online && !keepOffline) continue;
                list.Add(snap);
            }

            foreach (var snap in _stored.Values)
            {
                if (!seen.Add(snap.Identity)) continue;   // they answered for themselves already
                // A stored record for someone who is connected but has not answered yet (just
                // joined, or mid-respawn) is still someone playing now, so it is shown as online
                // even when offline players are not wanted. Anyone else only if they are.
                snap.Online = byPresence && IsConnected(snap);
                if (!snap.Online && !keepOffline) continue;
                list.Add(snap);
            }
            return list;
        }

        private static readonly HashSet<string> _connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Fills in the names of everyone connected. False when the game has no list to give yet.</summary>
        private static bool ReadConnectedNames()
        {
            _connected.Clear();
            var znet = ZNet.instance;
            var players = znet != null ? znet.GetPlayerList() : null;
            if (players == null) return false;
            foreach (var p in players)
                if (!string.IsNullOrEmpty(p.m_name)) _connected.Add(p.m_name);
            return _connected.Count > 0;
        }

        private static bool IsConnected(Snapshot snap) => !string.IsNullOrEmpty(snap.Name) && _connected.Contains(snap.Name);
    }
}

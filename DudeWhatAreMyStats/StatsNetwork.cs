using System;
using System.Collections.Generic;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// Asks the other players for their stats and keeps what comes back.
    ///
    /// Nobody's stats live on the server: each player's own game reads its own profile and answers
    /// for itself. A request goes to everyone, and every client that runs this mod replies straight
    /// to whoever asked. Routed RPCs are forwarded by the server whether or not it knows the method,
    /// so the server needs no mod of its own; players without the mod simply never answer.
    /// </summary>
    internal static class StatsNetwork
    {
        private const string RpcRequest  = "DWAMS_Request";    // asker -> everybody
        private const string RpcResponse = "DWAMS_Response";   // answerer -> asker, ZPackage

        /// <summary>After a request goes out, judge who is online by the previous round for this long.</summary>
        private const float SettleSeconds = 3f;

        private static readonly Dictionary<long, Snapshot> _remote = new Dictionary<long, Snapshot>();
        private static ZRoutedRpc _registeredOn;
        private static float _lastRequestAt = -999f;
        private static float _prevRequestAt = -999f;
        private static float _nextAutoRequestAt;

        internal static int KnownCount => _remote.Count;

        /// <summary>
        /// Registers the two RPCs, once per ZRoutedRpc instance. Each new session builds a new one,
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
            _lastRequestAt = -999f;
            _prevRequestAt = -999f;
            _nextAutoRequestAt = 0f;
        }

        internal static void Update()
        {
            Register();
            if (!StatsPanel.IsOpen) return;
            float every = DwamsConfig.RefreshSeconds != null ? DwamsConfig.RefreshSeconds.Value : 10f;
            if (every <= 0f) return;
            // Unscaled: the panel can be up while the game is paused, and it should still refresh.
            if (Time.unscaledTime < _nextAutoRequestAt) return;
            _nextAutoRequestAt = Time.unscaledTime + every;
            Request();
        }

        /// <summary>Asks everyone else online for their stats.</summary>
        internal static void Request()
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
            snap.ReceivedAt = Time.unscaledTime;
            // Key on the character, not the connection, so a reconnect updates one row instead of
            // adding a second. A player who switches characters gets a row each, which is honest.
            _remote[snap.Identity] = snap;
            StatsPanel.OnRosterChanged(snap.Identity);
        }

        /// <summary>Everyone we can show: the local character first, then whoever has answered.</summary>
        internal static List<Snapshot> Roster()
        {
            var list = new List<Snapshot>();
            var local = LocalStats.Read();
            if (local != null) list.Add(local);

            bool settling = Time.unscaledTime - _lastRequestAt < SettleSeconds;
            float cutoff = settling ? _prevRequestAt : _lastRequestAt;
            bool keepOffline = DwamsConfig.RememberOfflinePlayers == null || DwamsConfig.RememberOfflinePlayers.Value;

            foreach (var snap in _remote.Values)
            {
                if (local != null && snap.Identity == local.Identity) continue;   // our own echo
                snap.Online = snap.ReceivedAt >= cutoff - 0.5f;
                if (!snap.Online && !keepOffline) continue;
                list.Add(snap);
            }
            return list;
        }
    }
}

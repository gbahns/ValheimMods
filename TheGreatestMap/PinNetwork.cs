using HarmonyLib;

namespace TheGreatestMap
{
    /// <summary>
    /// Routed RPCs. A client sends its personal map to the server and gets the merged shared map
    /// back; in Instant mode (or after an admin wipe) the server also pushes the changes to
    /// everybody. Two clients exchange personal maps directly, routed through the server.
    /// Every map payload is a serialized MapStore, which starts with its own format version.
    /// </summary>
    internal static class PinNetwork
    {
        private const string Sync        = "TGM_Sync";        // client -> server: my map
        private const string SyncRes     = "TGM_SyncRes";     // server -> client: the shared map
        private const string Delta       = "TGM_Delta";       // server -> everybody: changes
        private const string Exchange    = "TGM_Exchange";    // client -> client: my map
        private const string Wipe        = "TGM_Wipe";        // client (admin) -> server
        private const string Unsuppress  = "TGM_Unsuppress";  // client (admin) -> server
        private const string BUnsuppress = "TGM_BUnsuppress"; // server -> everybody

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            rpc.Register<ZPackage>(Sync, RPC_Sync);
            rpc.Register<ZPackage>(SyncRes, RPC_SyncRes);
            rpc.Register<ZPackage>(Delta, RPC_Delta);
            rpc.Register<ZPackage>(Exchange, RPC_Exchange);
            rpc.Register<int>(Wipe, RPC_Wipe);
            rpc.Register(Unsuppress, RPC_Unsuppress);
            rpc.Register(BUnsuppress, RPC_BUnsuppress);
        }

        private static bool Ready => ZRoutedRpc.instance != null && ZNet.instance != null;

        // ── senders ─────────────────────────────────────────────────────────────────

        internal static void SendSync(MapStore store)
        {
            if (!Ready) return;
            var pkg = new ZPackage();
            store.Write(pkg);
            ZRoutedRpc.instance.InvokeRoutedRPC(Sync, pkg);
        }

        /// <summary>Offer my map to another player: markers, and my explored area if given (compressed, table format).</summary>
        internal static void SendExchange(long peer, MapStore store, bool reply, byte[] exploration)
        {
            if (!Ready) return;
            var pkg = new ZPackage();
            pkg.Write(Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "");
            pkg.Write(reply);
            store.Write(pkg);
            pkg.Write(exploration != null && exploration.Length > 0);
            if (exploration != null && exploration.Length > 0) pkg.Write(exploration);
            ZRoutedRpc.instance.InvokeRoutedRPC(peer, Exchange, pkg);
        }

        internal static void SendWipe(bool autoOnly)
        {
            if (Ready) ZRoutedRpc.instance.InvokeRoutedRPC(Wipe, autoOnly ? 1 : 0);
        }

        internal static void SendUnsuppress()
        {
            if (Ready) ZRoutedRpc.instance.InvokeRoutedRPC(Unsuppress);
        }

        // ── server handlers ─────────────────────────────────────────────────────────

        private static void Broadcast(string method, params object[] args)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, method, args);
        }

        private static bool IsAdmin(long sender)
        {
            var znet = ZNet.instance;
            if (znet == null) return false;
            if (sender == Access.RoutedId(ZRoutedRpc.instance)) return true; // the hosting player
            var peer = znet.GetPeer(sender);
            if (peer == null || peer.m_socket == null) return false;
            string host = peer.m_socket.GetHostName();
            return !string.IsNullOrEmpty(host) && znet.IsAdmin(host);
        }

        private static void RPC_Sync(long sender, ZPackage pkg)
        {
            if (!PinStore.IsServer || !PinStore.Loaded) return;
            MapStore incoming;
            try { incoming = MapStore.Read(pkg); }
            catch (System.Exception e) { TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Bad map from peer {sender}: {e.Message}"); return; }
            var result = PinStore.Merge(incoming);
            if (result.Any && TgmConfig.SharingMode.Value == SharingMode.Instant)
            {
                var delta = new ZPackage();
                result.ToDelta().Write(delta);
                Broadcast(Delta, delta);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, SyncRes, new ZPackage(PinStore.Snapshot()));
        }

        private static void RPC_Wipe(long sender, int autoOnly)
        {
            if (!PinStore.IsServer || !PinStore.Loaded) return;
            if (!IsAdmin(sender))
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Ignored wipe request from non-admin peer {sender}.");
                return;
            }
            var delta = PinStore.Wipe(autoOnly != 0);
            TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Wiped {delta.Tombstones.Count} shared markers (autoOnly={autoOnly != 0}) on request of peer {sender}.");
            var pkg = new ZPackage();
            delta.Write(pkg);
            Broadcast(Delta, pkg);
        }

        private static void RPC_Unsuppress(long sender)
        {
            if (!PinStore.IsServer || !PinStore.Loaded) return;
            if (!IsAdmin(sender)) return;
            PinStore.ClearSuppressions();
            Broadcast(BUnsuppress);
        }

        // ── client handlers (also run on the server process, where Minimap is null) ─

        private static void RPC_SyncRes(long sender, ZPackage pkg)
        {
            if (Minimap.instance == null) return;
            try { SyncEngine.OnSharedMap(MapStore.Read(pkg)); }
            catch (System.Exception e) { TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Bad shared map from the server: {e.Message}"); }
        }

        private static void RPC_Delta(long sender, ZPackage pkg)
        {
            if (Minimap.instance == null) return;
            try { SyncEngine.OnDelta(MapStore.Read(pkg)); }
            catch (System.Exception e) { TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Bad update from the server: {e.Message}"); }
        }

        private static void RPC_Exchange(long sender, ZPackage pkg)
        {
            if (Minimap.instance == null) return;
            string name;
            bool reply;
            MapStore theirs;
            try
            {
                name = pkg.ReadString();
                reply = pkg.ReadBool();
                theirs = MapStore.Read(pkg);
            }
            catch (System.Exception e) { TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Bad map from another player: {e.Message}"); return; }
            // Exploration follows the markers; a sender on an older build stops after the markers.
            byte[] exploration = null;
            try { if (pkg.ReadBool()) exploration = pkg.ReadByteArray(); } catch { exploration = null; }
            SyncEngine.OnExchange(sender, name, reply, theirs, exploration);
        }

        private static void RPC_BUnsuppress(long sender)
        {
            if (Minimap.instance == null) return;
            ClientPins.ClearSuppressions();
        }
    }

    // ── lifecycle ──────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class Game_Start_Patch
    {
        private static void Postfix()
        {
            PinNetwork.Register();
            ClientPins.Reset();
            SyncEngine.Reset();
            DiscoveryLedger.Clear();
            Recorder.Reset();
            LocationIndex.Clear();
            Buildings.Clear();
            Searched.Clear();
            KindInference.Invalidate();
            PocketMap.ResetState();
            TableSync.Reset();
            if (PinStore.IsServer) PinStore.Load();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class Player_OnSpawned_Patch
    {
        private static void Postfix(Player __instance)
        {
            // In Instant mode catch up with the shared map right away. In Table mode the map
            // travels like exploration and waits for a cartography table, except the very first
            // time a character enters a world, when it receives the shared map once so it does
            // not start blank.
            if (__instance != Player.m_localPlayer) return;
            if (SyncEngine.Instant || PersonalMap.IsNew) SyncEngine.SyncWithServer(announceNothing: false);
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
    internal static class ZNet_Shutdown_Patch
    {
        private static void Prefix()
        {
            PinStore.Unload();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.SaveWorldAndPlayerProfiles))]
    internal static class ZNet_SaveWorld_Patch
    {
        private static void Postfix()
        {
            PinStore.SaveIfDirty();
        }
    }
}

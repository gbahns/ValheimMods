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
        private const string WhoAmI      = "TGM_WhoAmI";      // client -> server: am I an admin?
        private const string YouAre      = "TGM_YouAre";      // server -> that client: the answer

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            Rpc.Register<ZPackage>(rpc, Sync, RPC_Sync);
            Rpc.Register<ZPackage>(rpc, SyncRes, RPC_SyncRes);
            Rpc.Register<ZPackage>(rpc, Delta, RPC_Delta);
            Rpc.Register<ZPackage>(rpc, Exchange, RPC_Exchange);
            Rpc.Register<int>(rpc, Wipe, RPC_Wipe);
            Rpc.Register(rpc, Unsuppress, RPC_Unsuppress);
            Rpc.Register(rpc, BUnsuppress, RPC_BUnsuppress);
            Rpc.Register(rpc, WhoAmI, RPC_WhoAmI);
            Rpc.Register<bool, string>(rpc, YouAre, RPC_YouAre);
        }

        private static bool Ready => ZRoutedRpc.instance != null && ZNet.instance != null;

        // ── senders ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Steam carries at most 524,288 bytes in one reliable message, and over that the send
        /// loop breaks rather than skipping the message -- the queue behind it stalls and the
        /// connection dies thirty seconds later. Worth saying when a payload is getting close,
        /// which is never in normal play; it used to be said on every sync, where it was a
        /// sentence nobody read attached to a number that was fine.
        /// </summary>
        private const int WarnBytes = 256 * 1024;

        /// <summary>
        /// Who a peer number belongs to, for a log a person has to read.
        ///
        /// The number itself is a session id the game makes up per connection -- not a Steam id,
        /// not stable across a rejoin, and not the id ServerSync and the Azu mods print beside
        /// their own lines. So "peer 3087014532 sent a map" cannot be matched to a player, to
        /// another mod's line, or to the same player an hour later. The name can.
        /// </summary>
        private static string Who(long peer)
        {
            try
            {
                var net = ZNet.instance;
                var found = net != null ? net.GetPeer(peer) : null;
                string name = found != null ? found.m_playerName : null;
                return string.IsNullOrEmpty(name) ? $"peer {peer}" : $"{name} (peer {peer})";
            }
            catch (System.Exception) { return $"peer {peer}"; }
        }

        private static void WarnIfLarge(int bytes, string what)
        {
            if (bytes < WarnBytes) return;
            TheGreatestMapMod.Log.LogWarning(
                $"[TheGreatestMap] {what} is {bytes / 1024f:0.#} KB, and one message carries at most 512 KB. " +
                "Past that the connection drops rather than the message.");
        }

        /// <summary>When the map last went up, so the answer can be timed against it.</summary>
        private static float _sentAt = -1f;

        internal static void SendSync(MapStore store)
        {
            if (!Ready) return;
            _sentAt = UnityEngine.Time.realtimeSinceStartup;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var pkg = new ZPackage();
            store.WriteFor(pkg);
            // Written after the map, so a server that knows nothing of watermarks reads the map
            // and stops, exactly as it always did. It then answers with the whole map, which is
            // the right answer to a question it did not hear.
            pkg.Write(SyncEngine.HeardSession);
            pkg.Write(SyncEngine.HeardSeq);
            long wrote = clock.ElapsedMilliseconds;
            ZRoutedRpc.instance.InvokeRoutedRPC(Sync, pkg);
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] {System.DateTime.Now:HH:mm:ss} sent our map up: {store.Pins.Count} markers, " +
                $"{pkg.Size() / 1024f:0.#} KB.");
            WarnIfLarge(pkg.Size(), "the map we sent up");
        }

        /// <summary>Offer my map to another player: markers, and my explored area if given (compressed, table format).</summary>
        internal static void SendExchange(long peer, MapStore store, bool reply, byte[] exploration)
        {
            if (!Ready) return;
            var pkg = new ZPackage();
            pkg.Write(Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "");
            pkg.Write(reply);
            store.WriteFor(pkg);
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

        /// <summary>
        /// Ask the server whether it treats this player as an admin. Only the server can answer:
        /// admin status is a line in its adminlist.txt matched against the id on the connection, so
        /// nothing a client believes about itself counts. The answer carries that id back too,
        /// since "which id should I add to the file" is the next question.
        /// </summary>
        internal static void AskAmIAdmin()
        {
            if (!Ready) { TheGreatestMapMod.Message("Not connected to a world."); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(WhoAmI);
        }

        private static void RPC_WhoAmI(long sender)
        {
            if (!PinStore.IsServer) return;
            string id = "";
            var znet = ZNet.instance;
            if (znet != null)
            {
                var peer = znet.GetPeer(sender);
                if (peer != null && peer.m_socket != null) id = peer.m_socket.GetHostName() ?? "";
            }
            if (string.IsNullOrEmpty(id)) id = "this player is hosting, so the id is not on a connection";
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, YouAre, IsAdmin(sender), id);
        }

        private static void RPC_YouAre(long sender, bool admin, string id)
        {
            string text = admin
                ? "The server treats you as an admin."
                : "The server does not treat you as an admin.";
            TheGreatestMapMod.Message(text);
            TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] {text} The id it sees for you: {id}. Admins are the ids listed in the server's adminlist.txt, one per line.");
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
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int came = pkg.Size();
            MapStore incoming;
            try { incoming = MapStore.ReadFrom(pkg); }
            catch (System.Exception e) { TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Bad map from {Who(sender)}: {e.Message}"); return; }
            int tookIn = incoming.Pins.Count;
            // Read after the map and only if the sender wrote them; a client that predates
            // watermarks simply asks for everything.
            long askedSession = 0L, askedSeq = -1L;
            try
            {
                if (pkg.GetPos() < pkg.Size()) askedSession = pkg.ReadLong();
                if (pkg.GetPos() < pkg.Size()) askedSeq = pkg.ReadLong();
            }
            catch (System.Exception) { askedSession = 0L; askedSeq = -1L; }

            var result = PinStore.Merge(incoming);
            long merged = clock.ElapsedMilliseconds;
            if (result.Refused)
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Erasures turned away {result.RefusedByErasure} marker(s) from {Who(sender)}" +
                    (result.RefusedByOldErasure > 0
                        ? $", and pre-rekey erasures another {result.RefusedByOldErasure}" : "") + ".");
            if (result.Any && TgmConfig.SharingMode.Value == SharingMode.Instant)
            {
                var delta = new ZPackage();
                result.ToDelta().WriteFor(delta);
                Broadcast(Delta, delta);
            }
            // Compressed like everything else that crosses the wire. This one was not: it went
            // as a raw snapshot, which is the largest message this mod sends and the one that
            // outgrew what Steam will carry.
            var marks = PinStore.Marks;
            bool known = marks.Knows(askedSession, askedSeq);
            var sending = known ? marks.Since(askedSeq, PinStore.Store) : PinStore.Store;

            var reply = new ZPackage();
            sending.WriteFor(reply);
            // After the map, for the same reason the client writes its watermark after its own:
            // an older client reads the map and stops, and keeps asking for everything.
            reply.Write(marks.Session);
            reply.Write(marks.Seq);
            // One line for the whole exchange rather than one for each half: they are written
            // a millisecond apart by the same handler, and two lines to say "a sync happened"
            // is one more than anybody reading a server log wants.
            string answer = known
                ? $"{sending.Pins.Count} of {PinStore.Store.Pins.Count} markers, {sending.Tombstones.Count} erasure(s)"
                : $"all {PinStore.Store.Pins.Count} markers";
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] {Who(sender)} sent {tookIn} markers ({came / 1024f:0.#} KB); " +
                $"answered with {answer} ({reply.Size() / 1024f:0.#} KB) in {clock.ElapsedMilliseconds} ms, " +
                $"{merged} of it merging.");
            WarnIfLarge(reply.Size(), $"the map sent to {Who(sender)}");
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, SyncRes, reply);
        }

        private static void RPC_Wipe(long sender, int autoOnly)
        {
            if (!PinStore.IsServer || !PinStore.Loaded) return;
            if (!IsAdmin(sender))
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Ignored wipe request from non-admin {Who(sender)}.");
                return;
            }
            var delta = PinStore.Wipe(autoOnly != 0);
            TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Wiped {delta.Tombstones.Count} shared markers (autoOnly={autoOnly != 0}) on request of {Who(sender)}.");
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
            try
            {
                float waited = _sentAt > 0f ? UnityEngine.Time.realtimeSinceStartup - _sentAt : -1f;
                _sentAt = -1f;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                int size = pkg.Size();
                var store = MapStore.ReadFrom(pkg);
                long read = clock.ElapsedMilliseconds;
                // Read here, committed after the merge. Claiming to hold changes we then failed
                // to apply is the one way a watermark can lose a marker for good, so it is only
                // written down once the markers are in. A server too old to send one offers
                // nothing, we keep asking for the whole map, and that is correct.
                long session = 0L, seq = -1L;
                try
                {
                    if (pkg.GetPos() < pkg.Size()) session = pkg.ReadLong();
                    if (pkg.GetPos() < pkg.Size()) seq = pkg.ReadLong();
                }
                catch (System.Exception) { session = 0L; seq = -1L; }
                SyncEngine.OnSharedMap(store);
                if (seq >= 0L) SyncEngine.Heard(session, seq);
                // The round trip and what came of it, in one line. "Answered in 0.0 s" on its
                // own said nothing the next line did not, and the detail only appeared when the
                // merge was slow -- so a sync that went well was two lines, neither of which
                // said what had arrived.
                string line = $"[TheGreatestMap] {System.DateTime.Now:HH:mm:ss} the server answered" +
                              (waited >= 0f ? $" in {waited:0.0} s" : "") +
                              $": {store.Pins.Count} markers, {size / 1024f:0.#} KB, " +
                              $"unpacked in {read} ms and taken in {clock.ElapsedMilliseconds - read} ms.";
                if (clock.ElapsedMilliseconds >= 50L) TheGreatestMapMod.Log.LogWarning(line);
                else TheGreatestMapMod.Log.LogInfo(line);
            }
            catch (System.Exception e) { TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Bad shared map from the server: {e.Message}"); }
        }

        private static void RPC_Delta(long sender, ZPackage pkg)
        {
            if (Minimap.instance == null) return;
            try { SyncEngine.OnDelta(MapStore.ReadFrom(pkg)); }
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
                theirs = MapStore.ReadFrom(pkg);
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

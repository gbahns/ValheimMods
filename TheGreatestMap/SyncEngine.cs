using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace TheGreatestMap
{
    internal enum SharingMode
    {
        Table,   // markers travel like exploration: merge at a cartography table or map to map
        Instant, // every change goes to the shared map at once and out to everyone
    }

    /// <summary>
    /// When and how the personal map meets other maps: the shared map on the server (at a
    /// cartography table, on demand, or continuously in Instant mode) and other players'
    /// personal maps (both maps out, standing together).
    /// </summary>
    internal static class SyncEngine
    {
        private static float _instantDue = -1f;
        private static bool _announcePending;
        private static readonly Dictionary<long, float> _lastExchange = new Dictionary<long, float>();
        private static readonly HashSet<long> _quietOnce = new HashSet<long>();

        internal static bool Instant => TgmConfig.SharingMode.Value == SharingMode.Instant;

        // ── what we have already been told ──────────────────────────────────────────
        //
        // The server's numbering, as far as this client has heard it. Sent with every sync so
        // the answer can be only what has changed since. Forgotten on leaving a world, and
        // never written to disk: one whole map per player per server restart is a cheap price
        // for a claim that cannot outlive the thing it describes.

        /// <summary>The server run our sequence number belongs to; 0 when we have heard none.</summary>
        internal static long HeardSession { get; private set; }

        /// <summary>The last change number the server told us about; -1 when we have heard none.</summary>
        internal static long HeardSeq { get; private set; } = -1L;

        /// <summary>Note where the server's numbering had reached, once its changes are applied.</summary>
        internal static void Heard(long session, long seq)
        {
            if (session == 0L || seq < 0L) return;
            if (session == HeardSession && seq < HeardSeq) return;   // never walk the mark backwards
            HeardSession = session;
            HeardSeq = seq;
        }

        private static void ForgetWatermark()
        {
            HeardSession = 0L;
            HeardSeq = -1L;
            _serverHasOurs = false;
        }

        /// <summary>
        /// Whether the server has everything this map holds -- true only once a reply has come
        /// back for an upload, which is the only proof the server actually took it.
        ///
        /// PersonalMap.Dirty cannot answer this on its own: it is cleared when a map is handed
        /// to the network, not when the far end accepts it, so a send that never arrives leaves
        /// a clean flag over an unsent change. An answered sync is the proof.
        /// </summary>
        private static bool _serverHasOurs;

        internal static void Reset()
        {
            _instantDue = -1f;
            _announcePending = false;
            _lastFullSync = -999f;
            _lastExchange.Clear();
            _quietOnce.Clear();
            ForgetWatermark();
        }

        /// <summary>Called after every local edit. In Instant mode, syncs with the shared map shortly after.</summary>
        internal static void OnLocalChange()
        {
            if (Instant) _instantDue = Time.time + 2f;
        }

        internal static void Update()
        {
            if (_instantDue > 0f && Time.time >= _instantDue)
            {
                _instantDue = -1f;
                SyncWithServer(announceNothing: false);
            }
        }

        // ── shared map on the server ────────────────────────────────────────────────

        /// <summary>How long a map that has not changed is assumed to still match the server's.</summary>
        private const float UnchangedSeconds = 120f;

        private static float _lastFullSync = -999f;

        /// <summary>
        /// Send the personal map to the server; the merged shared map comes back and is merged here.
        ///
        /// The whole map goes up every time, and a map carrying four and a half thousand markers
        /// is half a megabyte. Steam sends that as one ordered message, so until it has gone,
        /// everything behind it waits -- including the answer the server is waiting to hear. Do
        /// that repeatedly, as standing at a cartography table did, and the server stops hearing
        /// from the client at all and closes the connection.
        ///
        /// So a map that has not changed since it last went up is not sent again. Nothing is lost
        /// by staying quiet: the server already has exactly that map, and anything arriving from
        /// anyone else still comes down as usual.
        /// </summary>
        internal static void SyncWithServer(bool announceNothing, bool asked = false)
        {
            if (!PersonalMap.Loaded) return;
            // Skipped only when nobody asked. The wait is measured in game time on purpose, and
            // that is the whole reason it is right: the world is only ever stopped when every
            // player online has asked for it -- Pause My Server grants a pause on no other terms,
            // a lone player being the common case rather than the rule -- or when an admin stops
            // it for everybody. Either way nobody is moving, so time that does not pass is time
            // in which nothing could have changed, and a cooldown that kept running through it
            // would be counting nothing.
            //
            // It exists to avoid a sync, which used to mean the whole map in both directions.
            // A sync now carries what changed, so the thing being avoided costs a tenth of a
            // kilobyte -- not worth refusing somebody who typed the command or pressed the key.
            if (!asked && !PersonalMap.Dirty && Time.time - _lastFullSync < UnchangedSeconds)
            {
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] {System.DateTime.Now:HH:mm:ss} not asking the server: our map has not changed since it last went up.");
                if (announceNothing) TheGreatestMapMod.Message("Shared map: nothing of ours has changed.");
                return;
            }
            _lastFullSync = Time.time;
            // Nothing of ours to offer: the server answered our last upload and nothing has
            // changed here since, so it already holds every marker this map does. Send an empty
            // one and let the watermark fetch whatever the others have added. This is the
            // ordinary case at a cartography table and it used to cost 70 KB in each direction.
            bool nothingToOffer = _serverHasOurs && !PersonalMap.Dirty;
            if (PersonalMap.Dirty) _serverHasOurs = false;
            PersonalMap.Sent();
            _announcePending = announceNothing;
            PinNetwork.SendSync(nothingToOffer ? new MapStore() : PersonalMap.Store);
        }

        internal static void OnSharedMap(MapStore serverStore)
        {
            // The server answered, so it has taken whatever we sent with that question.
            _serverHasOurs = true;
            var result = ClientPins.ApplyMerge(serverStore);
            AfterMerge(result);
            if (result.Any) TheGreatestMapMod.Message("Shared map: " + result);
            else if (_announcePending) TheGreatestMapMod.Message("Shared map: nothing new.");
            _announcePending = false;
        }

        /// <summary>A change pushed by the server (Instant mode, or an admin wipe).</summary>
        internal static void OnDelta(MapStore delta)
        {
            var result = ClientPins.ApplyMerge(delta);
            AfterMerge(result);
            if (result.Deleted > 0 && result.Added == 0 && result.Updated == 0 && delta.Tombstones.Count > 5)
                TheGreatestMapMod.Message($"Shared map: {result.Deleted} markers erased by an admin.");
        }

        /// <summary>Markers that arrived from elsewhere follow this player's label rules too.</summary>
        internal static void AfterMerge(MergeResult result)
        {
            if (result.Added + result.Updated == 0) return;
            // Markers that arrived standing on ones already here. Recording checks for a marker
            // already in the way, but only against this map: another player who recorded the same
            // cave before our maps met wrote theirs just as honestly, and both survive a merge,
            // because by id they are two different markers. Folding here is the only place that
            // sees both. Every map folds a pair the same way round, so this converges rather than
            // each side erasing the other's.
            // Timed, each pass separately. These run over every marker on the map, and a map
            // shared for a month carries thousands; when two players comparing maps in the field
            // watched the game stop for fifteen seconds, nothing in the log said which pass did it.
            var clock = Stopwatch.StartNew();
            if (TgmConfig.FoldDuplicates == null || TgmConfig.FoldDuplicates.Value)
            {
                int folded = Duplicates.Fold();
                if (folded > 0) TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Folded {folded} marker(s) that arrived standing on ones already here.");
            }
            long foldMs = clock.ElapsedMilliseconds;
            // Markers can still arrive with the old wrong icon from players on an older version.
            if (TgmConfig.RepairDungeonIcons.Value)
            {
                int fixedUp = ClientPins.RepairDungeonIcons();
                if (fixedUp > 0) TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Corrected the icon on {fixedUp} dungeon marker(s) that had the crypt key.");
            }
            long repairMs = clock.ElapsedMilliseconds - foldMs;
            if (TgmConfig.ApplyLabelRulesOnSync.Value)
            {
                int stripped = ClientPins.ApplyLabelRules(null);
                if (stripped > 0) TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Removed labels from {stripped} markers to match the label rules.");
            }
            long total = clock.ElapsedMilliseconds;
            if (total >= 50L)
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] Tidying up after a merge took {total} ms over {ClientPins.Count} markers " +
                    $"(folding {foldMs} ms, icons {repairMs} ms, labels {total - foldMs - repairMs} ms). " +
                    "That is a stop the player can feel.");
        }

        // ── map to map ──────────────────────────────────────────────────────────────

        /// <summary>Both maps are out and the players stand together: offer ours, theirs comes back.</summary>
        internal static void TryExchange(Player other)
        {
            if (!PersonalMap.Loaded || other == null) return;
            var nview = Access.NView(other);
            if (nview == null || !nview.IsValid()) return;
            long peer = nview.GetZDO().GetOwner();
            if (peer == 0L) return;
            if (_lastExchange.TryGetValue(peer, out float last) && Time.time - last < TgmConfig.ExchangeCooldown.Value) return;
            _lastExchange[peer] = Time.time;
            var clock = Stopwatch.StartNew();
            var mine = MyExploration();
            PinNetwork.SendExchange(peer, PersonalMap.Store, reply: true, mine);
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Offered our map to {Name(other)}: {PersonalMap.Store.Pins.Count} markers, " +
                $"{Bytes(mine)} of explored area, packed in {clock.ElapsedMilliseconds} ms.");
        }

        internal static void OnExchange(long sender, string theirName, bool reply, MapStore theirs, byte[] exploration)
        {
            var clock = Stopwatch.StartNew();
            var result = ClientPins.ApplyMerge(theirs);
            AfterMerge(result);
            long merged = clock.ElapsedMilliseconds;
            bool newAreas = ApplyExploration(exploration);
            long applied = clock.ElapsedMilliseconds;
            long packed = 0L;
            if (reply)
            {
                _lastExchange[sender] = Time.time;
                var mine = MyExploration();
                PinNetwork.SendExchange(sender, PersonalMap.Store, reply: false, mine);
                packed = clock.ElapsedMilliseconds;
                TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Sent our map back to {theirName}: {Bytes(mine)} of explored area.");
            }
            // Timed because this is the one routine heavy enough to be felt: applying another
            // player's explored area is the same work as reading a cartography table, and it runs
            // on the main thread. A long one here is a frame the game did not draw, which is worth
            // knowing about if anything odd happened at that moment.
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Compared maps with {theirName}: {(result.Any ? result.ToString() : "no marker changes")}, " +
                $"{Bytes(exploration)} of their explored area{(newAreas ? " (new ground)" : "")}. " +
                $"Markers {merged} ms, exploration {applied - merged} ms" +
                (reply ? $", packing ours {packed - applied} ms" : "") + $", {clock.ElapsedMilliseconds} ms in all.");
            string what = result.Any ? result.ToString() : "";
            if (newAreas) what = what.Length > 0 ? what + ", new map areas" : "new map areas";
            if (what.Length > 0) TheGreatestMapMod.Message($"Compared maps with {theirName}: {what}.");
            else if (_quietOnce.Add(sender)) TheGreatestMapMod.Message($"Compared maps with {theirName}: nothing new.");
        }

        private static string Bytes(byte[] data) => data == null ? "none" : $"{data.Length / 1024f:0.#} KB";

        private static string Name(Player other)
        {
            try { return other != null ? other.GetPlayerName() : "someone"; }
            catch (System.Exception) { return "someone"; }
        }

        /// <summary>My explored area in the cartography-table format (markers stripped by the table patches), compressed.</summary>
        private static byte[] MyExploration()
        {
            if (!TgmConfig.ExchangeExploration.Value || Minimap.instance == null) return null;
            try { return Utils.Compress(Minimap.instance.GetSharedMapData(null)); }
            catch (System.Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not pack the explored area for exchange: {e.Message}");
                return null;
            }
        }

        /// <summary>Another player's explored area, applied the way a cartography table read is. True if it revealed anything.</summary>
        private static bool ApplyExploration(byte[] exploration)
        {
            if (exploration == null || exploration.Length == 0 || Minimap.instance == null) return false;
            try { return Minimap.instance.AddSharedMapData(Utils.Decompress(exploration)); }
            catch (System.Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not read another player's explored area: {e.Message}");
                return false;
            }
        }
    }
}

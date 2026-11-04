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

        /// <summary>
        /// What a map that arrives in several messages has changed so far.
        ///
        /// <para>Each parcel is merged the moment it lands -- that is the whole point of a parcel
        /// being a map in its own right -- but what follows a merge must not run per parcel. The
        /// tidying passes walk every marker on the map, and the announcement and the watermark
        /// are about the map as a whole. So the counts are kept here and the once-per-map work
        /// happens on the last one.</para>
        ///
        /// <para>Nothing times out and nothing needs to. This holds a tally, not markers: if the
        /// tail never arrives, what landed is already on the map and the only thing lost is a
        /// line of summary.</para>
        /// </summary>
        private sealed class Arriving
        {
            private MergeResult _held;

            internal void Add(MergeResult result)
            {
                if (_held == null) _held = new MergeResult();
                _held.Absorb(result);
            }

            internal MergeResult Take()
            {
                var all = _held ?? new MergeResult();
                _held = null;
                return all;
            }

            internal void Forget() => _held = null;
        }

        private static readonly Arriving _fromServer = new Arriving();
        private static readonly Arriving _pushed = new Arriving();
        private static readonly Dictionary<long, Arriving> _fromPeers = new Dictionary<long, Arriving>();

        private static Arriving From(long peer)
        {
            Arriving held;
            if (!_fromPeers.TryGetValue(peer, out held)) _fromPeers[peer] = held = new Arriving();
            return held;
        }

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
            // A different run of the server. It may be holding a shared map restored from
            // before our markers reached it, and a question that carries only what changed here
            // since would never put them back -- so the whole map goes up once more.
            if (HeardSession != 0L && session != HeardSession)
            {
                TheGreatestMapMod.Log.LogInfo(
                    "[TheGreatestMap] The server has been restarted since we last synced; sending our whole map up once.");
                PersonalMap.SendEverything();
            }
            HeardSession = session;
            HeardSeq = seq;
        }

        private static void ForgetWatermark()
        {
            HeardSession = 0L;
            HeardSeq = -1L;
        }

        internal static void Reset()
        {
            _instantDue = -1f;
            _announcePending = false;
            _lastFullSync = -999f;
            _lastExchange.Clear();
            Underway.Reset();
            _quietOnce.Clear();
            ForgetWatermark();
            PersonalMap.Accepted();
            _fromServer.Forget();
            _pushed.Forget();
            _fromPeers.Clear();
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
            Underway.Pump();
        }

        // ── shared map on the server ────────────────────────────────────────────────

        /// <summary>How long a map that has not changed is assumed to still match the server's.</summary>
        private const float UnchangedSeconds = 120f;

        private static float _lastFullSync = -999f;

        /// <summary>
        /// Offer the personal map to the server; what the shared map has that we do not comes
        /// back and is merged here.
        ///
        /// <para>Both directions carry only what changed. The reply has for a while: the client
        /// sends the change number it last heard and the server answers from there. The question
        /// does now too -- the map names the markers it has that the server may not, and sends
        /// those. Recording one berry bush used to send 7,269 markers up, 96 KB of which 7,268
        /// markers were ones the server had handed us itself.</para>
        ///
        /// <para>It matters because Steam sends a message as one ordered thing: until it has
        /// gone, everything behind it waits, including the answer the server is waiting to hear.
        /// Do that repeatedly, as standing at a cartography table did, and the server stops
        /// hearing from the client at all and closes the connection. One message carries at most
        /// 512 KB, and at about thirteen bytes a marker compressed the whole map reaches that at
        /// roughly forty thousand of them -- which is the ceiling this lifts off, since what goes
        /// up no longer grows with the map.</para>
        ///
        /// <para>A map with nothing to offer still says nothing at all, rather than saying it in
        /// a few hundred bytes: the server already has it, and anything arriving from anyone else
        /// comes down as usual.</para>
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
            // A sync now carries what changed in both, so the thing being avoided costs a
            // tenth of a kilobyte -- not worth refusing somebody who typed the command or
            // pressed the key.
            if (!asked && !PersonalMap.Dirty && Time.time - _lastFullSync < UnchangedSeconds)
            {
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] {System.DateTime.Now:HH:mm:ss} not asking the server: our map has not changed since it last went up.");
                if (announceNothing) TheGreatestMapMod.Message("Shared map: nothing of ours has changed.");
                return;
            }
            _lastFullSync = Time.time;
            _announcePending = announceNothing;
            // Empty when the server has everything this map holds, which is the ordinary case
            // at a cartography table: the watermark written after it still fetches whatever the
            // others have added.
            PinNetwork.SendSync(PersonalMap.Outgoing());
        }

        /// <summary>
        /// The shared map, or one parcel of it. <paramref name="more"/> says another message of
        /// the same answer is still coming, in which case the markers go on the map and nothing
        /// else happens yet.
        /// </summary>
        internal static void OnSharedMap(MapStore serverStore, bool more)
        {
            // The merge itself writes nothing down as ours to send: these markers are the
            // server's own answer. The tidying after it does, and should -- folding two markers
            // into one erases the loser, and that erasure has to travel.
            _fromServer.Add(ClientPins.ApplyMerge(serverStore, fromServer: true));
            if (more) return;
            // The server has answered in full, which is the only proof it took what went up with
            // the question -- a send is not an arrival. Held until the last parcel so that an
            // answer cut short by a dropped connection leaves our markers waiting rather than
            // believing they were taken. Anything marked here since that send is untouched and
            // goes up next time either way.
            PersonalMap.Accepted();
            var result = _fromServer.Take();
            AfterMerge(result);
            if (result.Any) TheGreatestMapMod.Message("Shared map: " + result);
            else if (_announcePending) TheGreatestMapMod.Message("Shared map: nothing new.");
            _announcePending = false;
        }

        /// <summary>A change pushed by the server (Instant mode, or an admin wipe).</summary>
        internal static void OnDelta(MapStore delta, bool more)
        {
            // Pushed by the server, so the same reasoning as a reply: it already has these.
            // Not an answer to anything we sent, though, so nothing in flight is cleared.
            _pushed.Add(ClientPins.ApplyMerge(delta, fromServer: true));
            if (more) return;
            var result = _pushed.Take();
            AfterMerge(result);
            // Counted over the whole push rather than the one parcel it arrived in: a wipe of a
            // big map is an erasure per marker, which is the one delta large enough to be cut up.
            if (result.Deleted > 5 && result.Added == 0 && result.Updated == 0)
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
            // Packing our explored ground is the expensive half and it is not worth a swim.
            // Offer the markers now and owe them the ground; Underway hands it over the moment
            // the deck settles.
            bool underway = Underway.Moving();
            var mine = underway ? null : MyExploration();
            if (underway) Underway.Hold(peer, Name(other), null, owesOurs: true);
            PinNetwork.SendExchange(peer, PersonalMap.Store, reply: true, mine);
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Offered our map to {Name(other)}: {PersonalMap.Store.Pins.Count} markers, " +
                (underway
                    ? "explored ground held back while we are under way"
                    : $"{Bytes(mine)} of explored area") +
                $", packed in {clock.ElapsedMilliseconds} ms.");
        }

        /// <summary>
        /// Another player's map, or one parcel of it. Their explored ground comes in a message of
        /// its own, so a map of four parcels and some ground is five of these, and only the last
        /// carries <paramref name="reply"/>.
        /// </summary>
        internal static void OnExchange(long sender, string theirName, bool reply, MapStore theirs,
                                        byte[] exploration, bool more)
        {
            var clock = Stopwatch.StartNew();
            // Another player's markers. The server has not seen these, so they are ours to pass
            // on and the merge writes each one down as such.
            var tally = From(sender);
            tally.Add(ClientPins.ApplyMerge(theirs, fromServer: false));
            if (more)
            {
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Took in part of {theirName}'s map: {theirs.Pins.Count} markers " +
                    $"in {clock.ElapsedMilliseconds} ms. More of it still coming.");
                return;
            }
            _fromPeers.Remove(sender);
            var result = tally.Take();
            AfterMerge(result);
            long merged = clock.ElapsedMilliseconds;

            // Markers are always worth their 25 ms. Explored ground is not, not here: both
            // halves of it run in this one frame, and this frame is the one that put a player
            // in the sea. Under way, keep theirs and owe them ours.
            bool underway = Underway.Moving();
            if (underway)
            {
                Underway.Hold(sender, theirName, exploration, owesOurs: reply);
                if (reply)
                {
                    _lastExchange[sender] = Time.time;
                    PinNetwork.SendExchange(sender, PersonalMap.Store, reply: false, null);
                }
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Compared maps with {theirName} under way: " +
                    $"{(result.Any ? result.ToString() : "no marker changes")}, markers {merged} ms. " +
                    $"{Bytes(exploration)} of explored ground held back until the deck settles, " +
                    "because applying it takes long enough to walk a moving hull out from under you.");
                string held = result.Any ? result.ToString() : "";
                TheGreatestMapMod.Message(held.Length > 0
                    ? $"Compared maps with {theirName}: {held}. Map areas once we stop."
                    : $"Compared maps with {theirName}: map areas once we stop.");
                return;
            }

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

        /// <summary>Apply explored ground that was held back. Used by <see cref="Underway"/>.</summary>
        internal static bool TakeExploration(byte[] exploration) => ApplyExploration(exploration);

        /// <summary>Hand over the explored ground we owed somebody. Used by <see cref="Underway"/>.</summary>
        internal static void SendExplorationTo(long peer, string name)
        {
            var mine = MyExploration();
            if (mine == null || mine.Length == 0) return;
            PinNetwork.SendExchange(peer, new MapStore(), reply: false, mine);
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Sent {name} the explored ground we owed them from under way: {Bytes(mine)}.");
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

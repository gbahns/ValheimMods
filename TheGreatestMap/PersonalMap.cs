using System;
using System.Collections.Generic;
using HarmonyLib;

namespace TheGreatestMap
{
    /// <summary>
    /// The character's own map: every marker they recorded, placed or received, plus tombstones
    /// for what they erased. Saved with the character, one map per world, so it travels like the
    /// vanilla explored area does. Merged into the shared map at cartography tables or with
    /// another player's map when both are out.
    /// </summary>
    internal static class PersonalMap
    {
        private const string KeyPrefix = "TheGreatestMap.Map:";

        internal static MapStore Store { get; private set; } = Adopt(new MapStore());

        /// <summary>Listen to a store, so it says which markers change. Every map put here goes through this.</summary>
        private static MapStore Adopt(MapStore store)
        {
            store.OnChanged = Changed;
            store.OnSuppressed = Suppressed;
            return store;
        }

        /// <summary>
        /// Put a different map in place of this one, for tgm_restore. Marked dirty so it is
        /// written to the character, and so the next merge sends it like any other change.
        /// </summary>
        internal static void Replace(MapStore store)
        {
            if (store == null) return;
            Store = Adopt(store);
            Loaded = true;
            IsNew = false;
            SendEverything();
        }
        internal static bool Loaded { get; private set; }

        // ── what the server has not been told ────────────────────────────────────────
        //
        // This was one bool, and a bool can only say "something changed". The only honest
        // answer to that was the whole map, so recording a single berry bush sent 7,269
        // markers up -- 96 KB to tell the server about one of them, the other 7,268 of which
        // it had handed us itself. The reply had been differential for a while; this half
        // was not.
        //
        // So: which markers. The store reports each one as it is changed, and a sync carries
        // those and nothing else. Erased spots are a handful and travel together.

        private static readonly HashSet<PinKey> _pending = new HashSet<PinKey>();
        private static readonly HashSet<PinKey> _inFlight = new HashSet<PinKey>();
        private static bool _pendingAll, _inFlightAll;
        private static bool _pendingSpots, _inFlightSpots;
        private static int _quiet;

        /// <summary>
        /// Is there anything here the server may not have? What is in flight counts: a send is
        /// not an arrival, and a question that went unanswered has to be asked again rather than
        /// be held off by the cooldown for a map that "has not changed".
        /// </summary>
        internal static bool Dirty =>
            _pendingAll || _inFlightAll || _pendingSpots || _inFlightSpots
            || _pending.Count > 0 || _inFlight.Count > 0;

        private static void Changed(PinKey key)
        {
            if (_quiet > 0 || key.IsNone) return;
            _pending.Add(key);
        }

        private static void Suppressed()
        {
            if (_quiet > 0) return;
            _pendingSpots = true;
        }

        /// <summary>A marker that arrived from another player: ours to pass on now.</summary>
        internal static void Took(PinKey key) => Changed(key);

        /// <summary>
        /// Applying what the server just sent. Those markers came from it, so they are not news
        /// to it, and writing them down as ours to send would hand every one straight back at
        /// the next sync.
        /// </summary>
        internal static void BeginQuiet() => _quiet++;
        internal static void EndQuiet() { if (_quiet > 0) _quiet--; }

        /// <summary>
        /// The whole map goes up next time. For the cases where "what changed" cannot be
        /// answered: the first sync after loading, a map put back by tgm_restore, a server that
        /// has been restarted under us, and erasures from before the rekey, which are keyed by
        /// an id this map no longer uses and so have no key to name.
        /// </summary>
        internal static void SendEverything() => _pendingAll = true;

        /// <summary>
        /// The markers to offer the server: everything when it cannot be told what changed, and
        /// otherwise only what has changed here since it last answered.
        ///
        /// <para>What goes out is held aside rather than forgotten. A send is not an arrival:
        /// nothing proves the server took it until the answer comes back, and a change dropped
        /// at the moment of sending is a marker that reaches nobody and is never missed. If the
        /// next sync comes round with that one still unanswered, it goes back in the queue.</para>
        /// </summary>
        internal static MapStore Outgoing()
        {
            if (_inFlightAll) _pendingAll = true;
            if (_inFlightSpots) _pendingSpots = true;
            foreach (var key in _inFlight) _pending.Add(key);
            _inFlight.Clear();

            _inFlightAll = _pendingAll;
            _inFlightSpots = _pendingSpots || _pendingAll;
            if (!_pendingAll) foreach (var key in _pending) _inFlight.Add(key);
            _pendingAll = false;
            _pendingSpots = false;
            _pending.Clear();

            if (_inFlightAll) return Store;

            var slice = new MapStore();
            foreach (var key in _inFlight)
            {
                // One or the other, never both: writing a marker drops its erasure and erasing
                // one drops the marker. A key in neither was lifted -- taken off this map
                // without an erasure to carry it -- and has nothing to send.
                if (Store.Pins.TryGetValue(key, out var pin)) slice.Pins[key] = pin;
                else if (Store.Tombstones.TryGetValue(key, out long at)) slice.Tombstones[key] = at;
            }
            if (_inFlightSpots) slice.Suppressions.AddRange(Store.Suppressions);
            return slice;
        }

        /// <summary>The server answered, so it holds what went up with the question.</summary>
        internal static void Accepted()
        {
            _inFlight.Clear();
            _inFlightAll = false;
            _inFlightSpots = false;
        }
        /// <summary>True when this character has never had a map for this world: it receives the shared map once on spawn.</summary>
        internal static bool IsNew { get; private set; }

        internal static void Reset()
        {
            Store = Adopt(new MapStore());
            Loaded = false;
            IsNew = false;
            _pending.Clear();
            _inFlight.Clear();
            _pendingAll = _inFlightAll = false;
            _pendingSpots = _inFlightSpots = false;
            _quiet = 0;
        }

        private static string Key
        {
            get
            {
                var world = ZNet.World;
                string worldKey = world != null ? world.m_name + "_" + world.m_seed : "world";
                return KeyPrefix + worldKey;
            }
        }

        /// <summary>
        /// A local edit: in Instant mode, sync soon.
        ///
        /// <para>It used to set the dirty flag as well. It no longer needs to: the edit went
        /// through the store, and the store said which marker it was.</para>
        /// </summary>
        internal static void Touch() => SyncEngine.OnLocalChange();

        /// <summary>Which character on which world, for naming a backup of this map.</summary>
        /// <summary>What this map is called in a file name: the character and the world.</summary>
        internal static string Label => Name(Player.m_localPlayer);

        private static string Name(Player player)
        {
            string who = player != null ? player.GetPlayerName() : "character";
            var world = ZNet.World;
            return (string.IsNullOrEmpty(who) ? "character" : who) + "_" + (world != null ? world.m_name : "world");
        }

        internal static void LoadFrom(Player player)
        {
            Store = Adopt(new MapStore());
            Loaded = true;
            IsNew = false;
            _pending.Clear();
            _inFlight.Clear();
            _pendingSpots = _inFlightSpots = _inFlightAll = false;
            // The first sync of a session carries the whole map. Nothing here knows what the
            // server holds yet -- it may be a server this character has never met, or one put
            // back from a save written before these markers reached it -- and "what changed
            // since you last answered" has no meaning until it has answered once.
            SendEverything();
            if (player == null || player.m_customData == null) return;
            if (!player.m_customData.TryGetValue(Key, out var data) || string.IsNullOrEmpty(data)) { IsNew = true; return; }
            try
            {
                var stored = Utils.Decompress(Convert.FromBase64String(data));
                Store = Adopt(MapStore.FromBytes(stored));
                int crops = Store.ForgetCropErasures();
                if (crops > 0)
                {
                    TheGreatestMapMod.Log.LogInfo(
                        $"[TheGreatestMap] Forgot {crops} crop erasure(s) from your own map.");
                }
                // On every load; see PinStore for why it is no longer tied to the conversion.
                int ghosts = Store.ForgetGhosts();
                if (ghosts > 0)
                {
                    TheGreatestMapMod.Log.LogInfo(
                        $"[TheGreatestMap] Dropped {ghosts} erasure(s) from your own map that were only the former " +
                        "name of a marker still standing in the same place.");
                }
                if (Store.ReadVersion < MapStore.FormatVersion)
                {
                    // Before anything saves over it. A personal map cannot be copied as a file,
                    // because it lives inside the character; this writes out the bytes that were
                    // in there, which tgm_restore reads back.
                    MapBackup.Keep(Name(player), Store.ReadVersion, stored);
                    TheGreatestMapMod.Log.LogInfo(
                        $"[TheGreatestMap] Your own map was in format {Store.ReadVersion} and has been brought up to " +
                        $"{MapStore.FormatVersion}: {Store.Pins.Count} markers, {Store.LegacyTombstones.Count} erasure(s) carried over." +
                        (Store.Collapsed > 0
                            ? $" {Store.Collapsed} marker(s) were a second record of a spot already marked and are now one marker each."
                            : ""));
                }
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Personal map loaded: {Store.Pins.Count} markers, {Store.Tombstones.Count} erased" +
                    (Store.LegacyTombstones.Count > 0 ? $" (and {Store.LegacyTombstones.Count} erased before the rekey)" : "") + ".");
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not read the personal map from the character; starting empty: {e.Message}");
                Store = Adopt(new MapStore());
            }
        }

        internal static void SaveTo(Player player)
        {
            if (!Loaded || player == null || player.m_customData == null) return;
            try
            {
                // Nothing is cleared here. Saving writes the map into the character; it says
                // nothing about what the server has, and this used to clear the flag that did --
                // so an autosave landing between recording a marker and syncing left the marker
                // on this map and on no other, until some later edit happened to dirty the map
                // again.
                player.m_customData[Key] = Convert.ToBase64String(Utils.Compress(Store.ToBytes()));
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not save the personal map: {e.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Save))]
    internal static class Player_Save_PersonalMap_Patch
    {
        private static void Prefix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) PersonalMap.SaveTo(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Load))]
    internal static class Player_Load_PersonalMap_Patch
    {
        private static void Postfix(Player __instance)
        {
            PersonalMap.LoadFrom(__instance);
            ClientPins.RebuildPins();
            if (TgmConfig.RepairDungeonIcons.Value && PersonalMap.Store.Pins.Count > 0)
            {
                int fixedUp = ClientPins.RepairDungeonIcons();
                if (fixedUp > 0) TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Corrected the icon on {fixedUp} dungeon marker(s) on the personal map that had the crypt key.");
            }
            if (TgmConfig.ApplyLabelRulesOnSync.Value && PersonalMap.Store.Pins.Count > 0)
            {
                int stripped = ClientPins.ApplyLabelRules(null);
                if (stripped > 0) TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Removed labels from {stripped} markers on the personal map to match the label rules.");
            }
        }
    }
}

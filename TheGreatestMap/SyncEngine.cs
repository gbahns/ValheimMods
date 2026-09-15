using System.Collections.Generic;
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

        internal static void Reset()
        {
            _instantDue = -1f;
            _announcePending = false;
            _lastExchange.Clear();
            _quietOnce.Clear();
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

        /// <summary>Send the personal map to the server; the merged shared map comes back and is merged here.</summary>
        internal static void SyncWithServer(bool announceNothing)
        {
            if (!PersonalMap.Loaded) return;
            _announcePending = announceNothing;
            PinNetwork.SendSync(PersonalMap.Store);
        }

        internal static void OnSharedMap(MapStore serverStore)
        {
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
            // Markers can still arrive with the old wrong icon from players on an older version.
            if (TgmConfig.RepairDungeonIcons.Value)
            {
                int fixedUp = ClientPins.RepairDungeonIcons();
                if (fixedUp > 0) TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Corrected the icon on {fixedUp} dungeon marker(s) that had the crypt key.");
            }
            if (!TgmConfig.ApplyLabelRulesOnSync.Value) return;
            int stripped = ClientPins.ApplyLabelRules(null);
            if (stripped > 0) TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Removed labels from {stripped} markers to match the label rules.");
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
            PinNetwork.SendExchange(peer, PersonalMap.Store, reply: true, MyExploration());
        }

        internal static void OnExchange(long sender, string theirName, bool reply, MapStore theirs, byte[] exploration)
        {
            var result = ClientPins.ApplyMerge(theirs);
            AfterMerge(result);
            bool newAreas = ApplyExploration(exploration);
            if (reply)
            {
                _lastExchange[sender] = Time.time;
                PinNetwork.SendExchange(sender, PersonalMap.Store, reply: false, MyExploration());
            }
            string what = result.Any ? result.ToString() : "";
            if (newAreas) what = what.Length > 0 ? what + ", new map areas" : "new map areas";
            if (what.Length > 0) TheGreatestMapMod.Message($"Compared maps with {theirName}: {what}.");
            else if (_quietOnce.Add(sender)) TheGreatestMapMod.Message($"Compared maps with {theirName}: nothing new.");
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

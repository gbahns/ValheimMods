using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// "Have I checked this one?" answered automatically: opening a chest or harvesting a
    /// beehive inside a building crosses that building's marker off for everyone. If the
    /// building has no marker yet, it is remembered as searched and its marker is created
    /// crossed off when it is recorded.
    ///
    /// <para>Buildings and nothing else, which is a decision rather than an omission. A dungeon
    /// is the case to argue about -- knowing you have already been down a burial chamber is the
    /// same useful fact -- and it is left to the hand for two reasons. A crypt holds several
    /// chests in several rooms, so one chest opened is not the statement one hut searched is;
    /// and the marker stands on the hill above while the chest is thirty meters under it, so
    /// matching by distance the way this does would cross off whatever happened to be overhead.
    /// Camps are out with them. A click on the map crosses any marker off, which is enough for
    /// the places where "cleared" is a judgment only the player can make (asked, and settled,
    /// 2026-11-02).</para>
    /// </summary>
    internal static class Searched
    {
        private const float MatchRadius = 3f;

        private static readonly HashSet<string> _keys = new HashSet<string>();

        internal static bool WasSearched(string key) => key != null && _keys.Contains(key);

        internal static void Clear() => _keys.Clear();

        /// <summary>Something inside a building was searched (chest opened, beehive harvested).</summary>
        internal static void OnSearched(GameObject go)
        {
            if (go == null || !TgmConfig.CrossOffStructuresOnChest.Value) return;
            var player = Player.m_localPlayer;
            if (player == null || player.InInterior()) return;
            // Enemy bases as well as structures. The dvergr excavations and the charred
            // fortresses were structures until the kinds were sorted out, so they had this
            // already; losing it in the move would have been a change nobody asked for, and a
            // fuling village is the clearest case there is of a place worth knowing you cleared.
            if (!Catalog.TryClassify(go, out var found)
                || (found.Cat != Category.Structure && found.Cat != Category.Camp)) return;
            _keys.Add(found.Key);
            // The marker may sit anywhere in the building, so search its whole radius.
            if (ClientPins.TryCheckOff(found.Icon, found.DedupeCenter, Mathf.Max(MatchRadius, found.Radius)))
                TheGreatestMapMod.Message("Searched: " + found.Name);
        }
    }
}

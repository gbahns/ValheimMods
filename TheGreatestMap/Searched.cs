using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// "Have I checked this one?" answered automatically: opening a chest or harvesting a
    /// beehive inside a building crosses that building's marker off for everyone. If the
    /// building has no marker yet, it is remembered as searched and its marker is created
    /// crossed off when it is recorded.
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
            if (!Catalog.TryClassify(go, out var found) || found.Cat != Category.Structure) return;
            _keys.Add(found.Key);
            // The marker may sit anywhere in the building, so search its whole radius.
            if (ClientPins.TryCheckOff(found.Icon, found.DedupeCenter, Mathf.Max(MatchRadius, found.Radius)))
                TheGreatestMapMod.Message("Searched: " + found.Name);
        }
    }
}

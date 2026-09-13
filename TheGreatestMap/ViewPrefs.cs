using System;
using System.Collections.Generic;
using HarmonyLib;

namespace TheGreatestMap
{
    /// <summary>
    /// Markers this player chose to hide one at a time ("Hide this marker"). A view preference
    /// only: saved with the character, never synced, and the markers stay on the map and keep
    /// merging; they are just not drawn. Whole icons and kinds are hidden through config instead
    /// (Display > Hidden Icons, Show &lt;Kind&gt;).
    /// </summary>
    internal static class ViewPrefs
    {
        private const string Key = "TheGreatestMap.HiddenMarkers";
        private static readonly HashSet<string> _hidden = new HashSet<string>(StringComparer.Ordinal);

        internal static int Count => _hidden.Count;

        internal static bool IsHidden(string id) => id != null && _hidden.Contains(id);

        internal static void Hide(string id)
        {
            if (string.IsNullOrEmpty(id) || !_hidden.Add(id)) return;
            Refresh();
        }

        internal static void Unhide(string id)
        {
            if (id != null && _hidden.Remove(id)) Refresh();
        }

        internal static void Clear()
        {
            if (_hidden.Count == 0) return;
            _hidden.Clear();
            Refresh();
        }

        private static void Refresh()
        {
            var map = Minimap.instance;
            if (map != null) Access.PinUpdateRequired(map) = true;
        }

        internal static void LoadFrom(Player player)
        {
            _hidden.Clear();
            if (player == null || player.m_customData == null) return;
            if (!player.m_customData.TryGetValue(Key, out var data) || string.IsNullOrEmpty(data)) return;
            foreach (var id in data.Split(','))
                if (id.Length > 0) _hidden.Add(id);
        }

        internal static void SaveTo(Player player)
        {
            if (player == null || player.m_customData == null) return;
            // Forget markers that no longer exist, so the list cannot grow without bound.
            if (PersonalMap.Loaded)
            {
                var pins = PersonalMap.Store.Pins;
                _hidden.RemoveWhere(id => !pins.ContainsKey(id));
            }
            if (_hidden.Count == 0) player.m_customData.Remove(Key);
            else player.m_customData[Key] = string.Join(",", _hidden);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Save))]
    internal static class Player_Save_ViewPrefs_Patch
    {
        private static void Prefix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) ViewPrefs.SaveTo(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Load))]
    internal static class Player_Load_ViewPrefs_Patch
    {
        private static void Postfix(Player __instance) => ViewPrefs.LoadFrom(__instance);
    }
}

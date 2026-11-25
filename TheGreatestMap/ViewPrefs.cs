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
        private static readonly HashSet<PinKey> _hidden = new HashSet<PinKey>();

        internal static int Count => _hidden.Count;

        internal static bool IsHidden(PinKey id) => _hidden.Contains(id);

        /// <summary>The markers put away one at a time, for <see cref="PutBack"/> to remember.</summary>
        internal static void CopyInto(List<PinKey> into)
        {
            if (into == null) return;
            foreach (var id in _hidden) into.Add(id);
        }

        internal static void Hide(PinKey id)
        {
            if (id.IsNone || !_hidden.Add(id)) return;
            Refresh();
        }

        internal static void Unhide(PinKey id)
        {
            if (_hidden.Remove(id)) Refresh();
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
            // Anything unreadable is dropped rather than argued with. The list written before
            // markers were rekeyed names GUIDs, which no longer mean anything, so a player who
            // had hidden markers by hand sees them once more and can hide them again.
            foreach (var text in data.Split(','))
                if (PinKey.TryDecode(text, out var id)) _hidden.Add(id);
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
            if (_hidden.Count == 0) { player.m_customData.Remove(Key); return; }
            var parts = new List<string>(_hidden.Count);
            foreach (var id in _hidden) parts.Add(id.Encode());
            player.m_customData[Key] = string.Join(",", parts.ToArray());
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

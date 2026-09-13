using BepInEx.Configuration;
using UnityEngine;

namespace TheGreatestPortal
{
    /// <summary>All configuration entries. Bind() once from TheGreatestPortalMod.Awake.</summary>
    internal static class TgpConfig
    {
        // ── server-synced rules ─────────────────────────────────────────────────────
        internal static ConfigEntry<int> MaxNameLength;
        internal static ConfigEntry<bool> UntargetedOpensMap;
        internal static ConfigEntry<bool> AdoptExistingConnections;

        // ── keys ────────────────────────────────────────────────────────────────────
        internal static ConfigEntry<KeyboardShortcut> TogglePinsKey;

        // ── client display ──────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> ShowMessages;
        internal static ConfigEntry<bool> ShowPinsOnMap;
        internal static ConfigEntry<bool> HideOtherPinsWhileChoosing;
        internal static ConfigEntry<bool> ShowDistances;
        internal static ConfigEntry<float> AutoCloseGraceSeconds;
        internal static ConfigEntry<bool> ShowPortalListOnMap;

        internal static void Bind(TheGreatestPortalMod mod)
        {
            MaxNameLength = mod.BindSyncedRange("Rules", "Max Name Length", 32, 1, 64,
                "Longest portal name allowed. Vanilla allows 10 characters.");
            UntargetedOpensMap = mod.BindSynced("Rules", "Open Portals Show The Map", true,
                "A portal with no destination of its own is an open portal: stepping in shows the map and you " +
                "pick where to go. When false an open portal simply goes nowhere, like an unpaired vanilla portal.");
            AdoptExistingConnections = mod.BindSynced("Rules", "Adopt Existing Connections", true,
                "The first time the server sees a portal this mod has not configured, an existing vanilla tag pair " +
                "is kept as its destination, so a world keeps its portal network when the mod is added. Turn off " +
                "to start every unconfigured portal as an open portal.");

            TogglePinsKey = mod.BindLocal("Keys", "Toggle Portal Pins", new KeyboardShortcut(KeyCode.P),
                "While the large map is open: show or hide every portal on the map, with the portal list on the left. " +
                "Click a portal there to centre the map on it; right-click to mark it as a favourite.");

            ShowMessages = mod.BindLocal("Display", "Show Messages", true,
                "Show small top-left messages when a portal is configured, a favourite is added, and so on.");
            ShowPinsOnMap = mod.BindLocal("Display", "Always Show Portal Pins", false,
                "Draw every portal on the large map all the time, not only after pressing the toggle key or while " +
                "choosing a destination.");
            HideOtherPinsWhileChoosing = mod.BindLocal("Display", "Hide Other Pins While Choosing", true,
                "While you are choosing a destination, hide the map's other saved pins so the portals stand out. " +
                "Death markers, players and pings stay visible.");
            ShowDistances = mod.BindLocal("Display", "Show Distances", true,
                "Show how far away each portal is in the destination lists.");
            ShowPortalListOnMap = mod.BindLocal("Display", "Show Portal List On Map", true,
                "Show the list of portals on the left of the map while choosing a destination. With it off, only " +
                "the pins are clickable.");
            AutoCloseGraceSeconds = mod.BindLocalRange("Display", "Auto Close Grace Seconds", 0.5f, 0f, 3f,
                "When you leave an open portal's doorway while its destination map is up, the map closes after this " +
                "many seconds. 0 keeps it open until you close it.");
        }
    }
}

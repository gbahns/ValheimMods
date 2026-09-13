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
        internal static ConfigEntry<bool> AnyoneCanRedirectAll;
        internal static ConfigEntry<bool> AnyoneCanRenameRemote;

        // ── keys ────────────────────────────────────────────────────────────────────
        internal static ConfigEntry<KeyboardShortcut> TogglePinsKey;

        // ── client display ──────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> ShowMessages;
        internal static ConfigEntry<bool> ShowPinsOnMap;
        internal static ConfigEntry<bool> HideOtherPinsWhileChoosing;
        internal static ConfigEntry<bool> ShowDistances;
        internal static ConfigEntry<bool> GroupByBiome;
        internal static ConfigEntry<string> CollapsedGroups;
        internal static ConfigEntry<string> PanelSize;
        internal static ConfigEntry<string> PanelPosition;
        internal static ConfigEntry<int> ListScrollRows;
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
            AnyoneCanRedirectAll = mod.BindSynced("Rules", "Anyone Can Redirect All Portals", false,
                "Let any player use the panel's 'Point all portals to here' button, which points every portal in the " +
                "world at one portal. When false only admins can (the host of a local game counts as an admin).");
            AnyoneCanRenameRemote = mod.BindSynced("Rules", "Anyone Can Rename Remote Portals", true,
                "Let any player rename a portal from the panel's list without standing at it (Rename in a " +
                "row's right-click menu). When false only admins can; renaming the portal you are standing at is always allowed.");

            TogglePinsKey = mod.BindLocal("Keys", "Toggle Portal Pins", new KeyboardShortcut(KeyCode.P),
                "While the large map is open: show or hide every portal on the map, with the portal list on the left. " +
                "Click a portal there to center the map on it; right-click to mark it as a favorite.");

            ShowMessages = mod.BindLocal("Display", "Show Messages", true,
                "Show small top-left messages when a portal is configured, a favorite is added, and so on.");
            ShowPinsOnMap = mod.BindLocal("Display", "Always Show Portal Pins", false,
                "Draw every portal on the large map all the time, not only after pressing the toggle key or while " +
                "choosing a destination.");
            HideOtherPinsWhileChoosing = mod.BindLocal("Display", "Hide Other Pins While Choosing", true,
                "While you are choosing a destination, hide the map's other saved pins so the portals stand out. " +
                "Death markers, players and pings stay visible.");
            ShowDistances = mod.BindLocal("Display", "Show Distances", true,
                "Show how far away each portal is in the destination lists.");
            GroupByBiome = mod.BindLocal("Display", "Group By Biome", false,
                "Group the destination lists by biome, with a Favorites section first. The 'Group by biome' " +
                "switch on the panel and on the map changes this setting too.");
            CollapsedGroups = mod.BindLocal("Display", "Collapsed Groups", "",
                "Which groups are folded away in the destination lists, kept between sessions. Click a group's " +
                "header, or Expand all / Collapse all, in the lists themselves rather than editing this.");
            PanelSize = mod.BindLocal("Display", "Panel Size", "680,600",
                "Width and height of the portal panel, remembered when you drag its bottom-right corner.");
            PanelPosition = mod.BindLocal("Display", "Panel Position", "0,0",
                "Where the portal panel sits, as an offset from the screen center, remembered when you drag it by " +
                "its title. Set to 0,0 to put it back in the middle.");
            ListScrollRows = mod.BindLocalRangeInt("Display", "List Scroll Rows", 4, 1, 20,
                "How many rows a destination list moves per notch of the mouse wheel.");
            ShowPortalListOnMap = mod.BindLocal("Display", "Show Portal List On Map", true,
                "Show the list of portals on the left of the map while choosing a destination. With it off, only " +
                "the pins are clickable.");
            AutoCloseGraceSeconds = mod.BindLocalRange("Display", "Auto Close Grace Seconds", 0.5f, 0f, 3f,
                "When you leave an open portal's doorway while its destination map is up, the map closes after this " +
                "many seconds. 0 keeps it open until you close it.");
        }
    }
}

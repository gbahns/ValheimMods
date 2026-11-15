using System;
using BepInEx.Configuration;
using UnityEngine;

namespace TheGreatestPortal
{
    /// <summary>All configuration entries. Bind() once from TheGreatestPortalMod.Awake.</summary>
    internal static class TgpConfig
    {
        // ── server-synced rules ─────────────────────────────────────────────────────
        internal static ConfigEntry<int> MaxNameLength;
        internal static ConfigEntry<bool> LockConfiguration;
        internal static ConfigEntry<bool> UntargetedOpensMap;
        internal static ConfigEntry<bool> AdoptExistingConnections;
        internal static ConfigEntry<bool> AnyoneCanRedirectAll;
        internal static ConfigEntry<bool> AnyoneCanRenameRemote;
        internal static ConfigEntry<bool> AltarSpellingIsFatal;
        internal static ConfigEntry<bool> AllowPrivatePortals;
        internal static ConfigEntry<int> MaximumOpenMinutes;

        // ── this player's own choices ───────────────────────────────────────────────
        internal static ConfigEntry<int> OpenForMinutes;

        // ── keys ────────────────────────────────────────────────────────────────────
        internal static ConfigEntry<KeyboardShortcut> TogglePinsKey;
        internal static ConfigEntry<KeyboardShortcut> DetourKey;

        // ── client display ──────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> ShowMessages;
        internal static ConfigEntry<bool> ShowPinsOnMap;
        internal static ConfigEntry<bool> ShowDistances;
        internal static ConfigEntry<bool> GroupByBiome;
        internal static ConfigEntry<string> CollapsedGroups;
        internal static ConfigEntry<bool> CombinedRecents;
        internal static ConfigEntry<int> CombinedRecentsCount;
        internal static ConfigEntry<int> RecentPortals;
        internal static ConfigEntry<int> OthersRecentPortals;
        internal static ConfigEntry<int> RecentlyCreatedPortals;
        internal static ConfigEntry<string> PanelSize;
        internal static ConfigEntry<string> PanelPosition;
        internal static ConfigEntry<int> ListScrollRows;
        internal static ConfigEntry<float> AutoCloseGraceSeconds;
        internal static ConfigEntry<bool> ShowPrivatePortals;
        internal static ConfigEntry<string> MapListSize;
        internal static ConfigEntry<string> MapListPosition;

        // ── how it looks ────────────────────────────────────────────────────────────
        internal static ConfigEntry<Color> RowColorMine;
        internal static ConfigEntry<Color> RowColorOthers;
        internal static ConfigEntry<float> RowColorStrength;
        internal static ConfigEntry<bool> RowColorPatterns;
        internal static ConfigEntry<float> PanelOpacity;

        /// <summary>
        /// Raised when a setting that changes nothing but how things look is changed.
        ///
        /// What is already on screen follows it. These are the settings a player sets by looking
        /// at the result, and a color you have to close and reopen a panel to see is a color
        /// nobody lands on.
        /// </summary>
        internal static event Action AppearanceChanged;

        internal static void Bind(TheGreatestPortalMod mod)
        {
            LockConfiguration = mod.BindLocking("General", "Lock Configuration", true,
                "While on, the settings marked as synced can be changed only by the server's admins " +
                "(adminlist.txt), from their game; everyone else sees them read-only. Off, any player can " +
                "change them for the whole server.");

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

            AllowPrivatePortals = mod.BindSynced("Rules", "Allow Private Portals", false,
                "Let a portal's owner make it theirs alone: out of everyone else's list and map, and nobody " +
                "else able to walk through it or arrive at it. Off, the Private toggle is not there, nobody " +
                "can make a portal private, and a portal somebody made private while it was on is an " +
                "ordinary portal again -- its mark is kept, so turning this back on restores it. " +
                "Turn it on only once everybody is on 1.5.0 or newer: what keeps a player out of a private " +
                "portal is the mod in their own game, and an older one knows nothing about it.");
            MaximumOpenMinutes = mod.BindSyncedRange("Rules", "Maximum Open Minutes", 60, 0, 1440,
                "The longest a private portal may be opened to everyone for, in minutes. Its owner opens it " +
                "for a while so somebody can come over, and it shuts by itself with nobody to remember to " +
                "take off a list. 0 turns that off, leaving a private portal private until its owner makes " +
                "it public again. Does nothing while Allow Private Portals is off.");
            AltarSpellingIsFatal = mod.BindSynced("Rules", "Altar Spelling Is Fatal", false,
                "A portal within 50 m of the sacrificial stones in the middle of the world, named \"alter\" " +
                "(only the letters count, so spaces, digits and punctuation do not help), " +
                "kills whoever named it. There is a word for that place and the word is altar. Off by default, " +
                "because a server should choose to be like this on purpose. Renaming that portal from anywhere " +
                "counts; you do not have to be standing at it.");

            OpenForMinutes = mod.BindLocalRangeInt("General", "Open For Minutes", 15, 1, 1440,
                "How long \"Open to everyone\" opens one of your private portals for. Cut down to the " +
                "server's Maximum Open Minutes when that is shorter, so what you see offered is what you get.");

            TogglePinsKey = mod.BindLocal("Keys", "Toggle Portal Pins", new KeyboardShortcut(KeyCode.P),
                "While the large map is open and you are not choosing a destination: show or hide the portal " +
                "pins, whichever way \"Show Pins In Normal Map View\" leaves them. Does nothing while another " +
                "mod is already marking the portals.");

            DetourKey = mod.BindLocal("Keys", "Detour Key", new KeyboardShortcut(KeyCode.LeftAlt),
                "Hold this as you step into a portal to go somewhere else just this once: the destination map " +
                "opens instead of the usual trip, and the portal keeps the destination it is set to. Also gets " +
                "you out of a portal whose destination has gone missing.");

            ShowMessages = mod.BindLocal("Display", "Show Messages", true,
                "Show small top-left messages when a portal is configured, a favorite is added, and so on.");
            ShowPinsOnMap = mod.BindLocal("Display", "Show Pins In Normal Map View", false,
                "Draw every portal on the large map when you are not at a portal: markers only, with no list and " +
                "nothing to click. This does nothing while another mod is already marking the portals -- " +
                "TheGreatestMap draws them all by itself, and others do too -- because two pins on one portal " +
                "is worse than neither mod drawing it.");
            ShowDistances = mod.BindLocal("Display", "Show Distances", true,
                "Show how far away each portal is in the destination lists.");
            GroupByBiome = mod.BindLocal("Display", "Group By Biome", false,
                "Group the destination lists by biome, with a Favorites section first. The 'Group by biome' " +
                "switch on the panel and on the map changes this setting too.");
            CollapsedGroups = mod.BindLocal("Display", "Collapsed Groups", "",
                "Which groups are folded away in the destination lists, kept between sessions. Click a group's " +
                "header, or Expand all / Collapse all, in the lists themselves rather than editing this.");
            CombinedRecents = mod.BindLocal("Display", "One Combined Recents List", true,
                "Show a single Recents section instead of three: the portals most recently used or built by " +
                "anybody, newest first. The row's background says who -- blue for a portal only you have " +
                "been through, green once anybody else has, each banded as well. Off gives the three " +
                "separate sections instead. " +
                "Right-click any Recents heading in a list to switch.");
            CombinedRecentsCount = mod.BindLocalRangeInt("Display", "Combined Recents Count", 15, 0, 50,
                "How many portals the combined Recents section shows. 0 leaves it out. More history than this " +
                "is kept, so raising it fills in at once rather than from then on.");
            RecentPortals = mod.BindLocalRangeInt("Display", "Recent Portals", 5, 0, 20,
                "How many portals the Recents section at the top of the lists shows: the ones you most recently " +
                "traveled to or from, newest first. They keep their usual place in the list as well. 0 hides the section.");
            PanelSize = mod.BindLocal("Display", "Panel Size", "680,600",
                "Width and height of the portal panel, remembered when you drag its bottom-right corner.");
            PanelPosition = mod.BindLocal("Display", "Panel Position", "0,0",
                "Where the portal panel sits, as an offset from the screen center, remembered when you drag it by " +
                "its title. Set to 0,0 to put it back in the middle.");
            OthersRecentPortals = mod.BindLocalRangeInt("Display", "Other Players Recent Portals", 5, 0, 20,
                "How many portals other players have used lately to show in an Others' Recents section. 0 leaves it out.");
            RecentlyCreatedPortals = mod.BindLocalRangeInt("Display", "Recently Created Portals", 5, 0, 20,
                "How many newly built portals to show in a Recently Created section. 0 leaves it out.");
            ShowPrivatePortals = mod.BindLocal("Display", "Show Private Portals", false,
                "Admins only: ask the server for the private portals of other players as well, for when one " +
                "has to be found or cleaned up. The server ignores this from anyone who is not in its " +
                "adminlist.txt, so leaving it on does nothing for an ordinary player. Off is how a portal " +
                "somebody marked private stays theirs, admin or not.");
            MapListSize = mod.BindLocal("Display", "Map List Size", "380,660",
                "Width and height of the portal list on the map, remembered when you drag its bottom-right corner.");
            MapListPosition = mod.BindLocal("Display", "Map List Position", "20,-12",
                "Where the portal list sits on the map, from the top-left corner, remembered when you drag it by " +
                "its heading. Set to 20,-12 to put it back.");
            ListScrollRows = mod.BindLocalRangeInt("Display", "List Scroll Rows", 4, 1, 20,
                "How many rows a destination list moves per notch of the mouse wheel.");
            AutoCloseGraceSeconds = mod.BindLocalRange("Display", "Auto Close Grace Seconds", 0.5f, 0f, 3f,
                "When you leave an open portal's doorway while its destination map is up, the map closes after this " +
                "many seconds. 0 keeps it open until you close it.");

            RowColorMine = mod.BindLocal("Appearance", "Row Color Yours Alone", UiKit.DefaultRowMine,
                "The background color of a portal you are the only one to have been through. Only the hue is " +
                "taken from this; Row Color Strength decides how strongly it is painted.");
            RowColorOthers = mod.BindLocal("Appearance", "Row Color Used By Others", UiKit.DefaultRowTheirs,
                "The background color of a portal another player has been through, whether or not you have. Only " +
                "the hue is taken from this; Row Color Strength decides how strongly it is painted.");
            RowColorStrength = mod.BindLocalRange("Appearance", "Row Color Strength", UiKit.DefaultRowStrength, 0f, 0.6f,
                "How strongly those two colors are painted over a row. 0 leaves every row plain. One setting for " +
                "both colors, so neither of them ends up looking like it means more than the other.");
            RowColorPatterns = mod.BindLocal("Appearance", "Row Color Patterns", true,
                "Band a tinted row diagonally in its own color as well: leaning right for a portal only you " +
                "have been through, leaning left once another player has. Two colors alone leave out the " +
                "players who cannot tell them apart, and a direction reads the same in grey.");
            PanelOpacity = mod.BindLocalRange("Appearance", "Panel Opacity", 1f, 0.15f, 1f,
                "How solid the portal panel and the portal list on the map are, as a fraction of their usual look. " +
                "1 is as they have always been; lower lets more of the map and the world through. The text, the " +
                "buttons and the rows are not touched.");

            // The server keeps this wish against the connection, so it has to hear about a change.
            ShowPrivatePortals.SettingChanged += (_, __) => PortalNetwork.SendReveal();

            // The panels watch these rather than read them once, so a color or a slider lands while
            // you are looking at the thing it changes. Watched by section rather than one entry at a
            // time, so a setting added to Appearance later is carried without anyone remembering to.
            mod.Config.SettingChanged += (_, args) =>
            {
                if (args == null || args.ChangedSetting == null) return;
                if (args.ChangedSetting.Definition.Section != "Appearance") return;
                var handler = AppearanceChanged;
                if (handler != null) handler();
            };
        }
    }
}

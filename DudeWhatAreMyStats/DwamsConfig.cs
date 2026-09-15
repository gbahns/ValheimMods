using BepInEx.Configuration;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>All configuration entries. Bind() once from DudeWhatAreMyStatsMod.Awake.</summary>
    internal static class DwamsConfig
    {
        // ── keys ────────────────────────────────────────────────────────────────────
        internal static ConfigEntry<KeyboardShortcut> OpenKey;

        // ── behaviour ───────────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> PauseWhileOpen;
        internal static ConfigEntry<bool> AskOtherPlayers;
        internal static ConfigEntry<float> RefreshSeconds;
        internal static ConfigEntry<bool> RememberOfflinePlayers;
        internal static ConfigEntry<float> PushMinutes;

        // ── the server's store ──────────────────────────────────────────────────────
        internal static ConfigEntry<bool> ServerStoreEnabled;
        internal static ConfigEntry<float> ServerKeepDays;
        internal static ConfigEntry<int> ServerMaxCharacters;

        // ── display ─────────────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> ShowZeroStats;
        internal static ConfigEntry<string> SortColumn;
        internal static ConfigEntry<bool> SortDescending;
        internal static ConfigEntry<string> CollapsedGroups;
        internal static ConfigEntry<string> PanelSize;
        internal static ConfigEntry<string> PanelPosition;
        internal static ConfigEntry<int> ListScrollRows;
        internal static ConfigEntry<int> TopCreatureCount;

        // ── fixes ───────────────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> FixTreasureDiscoveryCount;

        internal static void Bind(DudeWhatAreMyStatsMod mod)
        {
            // I is free in vanilla Valheim (which leaves B H I J K L N O P U Y Z unbound) and is
            // the mnemonic for "info".  Check your other mods before changing it.
            OpenKey = mod.BindLocal("Keys", "Open Stats", new KeyboardShortcut(KeyCode.I),
                "Opens and closes the stats panel. Escape closes it too.");

            PauseWhileOpen = mod.BindLocal("Behaviour", "Pause While Open", true,
                "Pause the game while the stats panel is open, the way the ESC menu does. This goes through " +
                "the game's own pause, so it only takes effect when the game would let the ESC menu pause it: " +
                "alone in a solo or hosted game. On a server with other players online nothing freezes, unless " +
                "Pause My Server is installed and grants the pause.");
            AskOtherPlayers = mod.BindLocal("Behaviour", "Ask Other Players", true,
                "Ask everyone else online for their stats so the scoreboard can compare you. Each player's own " +
                "game answers for them, so only players who also run this mod appear. Turning this off works both " +
                "ways: you stop asking, you stop answering, and the server is told to forget the record it " +
                "already holds, so you leave everyone else's scoreboard as well as emptying your own.");
            RefreshSeconds = mod.BindLocalRange("Behaviour", "Refresh Seconds", 10f, 0f, 120f,
                "How often to ask the other players again while the panel is open. 0 asks only when the panel opens " +
                "and when you press Refresh.");
            RememberOfflinePlayers = mod.BindLocal("Behaviour", "Show Offline Players", true,
                "List players who are not online, as well as those who are. That covers anyone who logs out while " +
                "you are still playing, and, if the server also runs this mod, everyone it remembers from before you " +
                "logged in, each marked with how long ago they were last seen. Without the mod on the server there is " +
                "nowhere to remember anyone, so only the first case applies. Turn off to list only players online now.");
            PushMinutes = mod.BindLocalRange("Behaviour", "Push Minutes", 5f, 0f, 60f,
                "How often to hand your own stats to the server, so it can show them to others once you have logged " +
                "off. It also happens when you open the panel and when you leave the world. 0 stops sending them at " +
                "all, which keeps you off the board for anyone who was not online at the same time as you. A server " +
                "without this mod ignores them either way.");

            ServerStoreEnabled = mod.BindLocal("Server", "Server Store Enabled", true,
                "Server-side setting, ignored on a client. Keep a record of each character's last known stats so the " +
                "scoreboard can show players who are not online. The file lives in BepInEx/config/DudeWhatAreMyStats/, " +
                "one per world. Turn off to keep nothing, which leaves every client showing only the players online.");
            ServerKeepDays = mod.BindLocalRange("Server", "Server Keep Days", 30f, 0f, 365f,
                "Server-side setting, ignored on a client. Forget a character the server has not heard from in this " +
                "many days, so a world does not accumulate one-time visitors forever. 0 keeps every character for good.");
            ServerMaxCharacters = mod.BindLocalRangeInt("Server", "Server Max Characters", 100, 10, 200,
                "Server-side setting, ignored on a client. The most characters to remember; past this the least " +
                "recently seen are dropped. The whole set travels to a client in one message, and an oversized one " +
                "would jam that player's connection rather than merely fail, so this is a real ceiling and not just " +
                "housekeeping. 100 is far more than a group of friends will ever need.");

            ShowZeroStats = mod.BindLocal("Display", "Show Zero Stats", false,
                "Show stats that are still zero in the Details tab. Off hides them, which is most of the 200-odd " +
                "stats the game tracks.");
            SortColumn = mod.BindLocal("Display", "Sort Column", "Kills",
                "Which scoreboard column is sorted, remembered between sessions. Click a column header to change it " +
                "rather than editing this.");
            SortDescending = mod.BindLocal("Display", "Sort Descending", true,
                "Whether the sorted scoreboard column runs highest first. Click a column header twice to flip it.");
            CollapsedGroups = mod.BindLocal("Display", "Collapsed Groups", "",
                "Which sections are folded away in the Details tab, kept between sessions. Click a section header, " +
                "or Expand all / Collapse all, in the panel itself rather than editing this.");
            PanelSize = mod.BindLocal("Display", "Panel Size", "820,620",
                "Width and height of the stats panel, remembered when you drag its bottom-right corner.");
            PanelPosition = mod.BindLocal("Display", "Panel Position", "0,0",
                "Where the stats panel sits, as an offset from the screen center, remembered when you drag it by its " +
                "title. Set to 0,0 to put it back in the middle.");
            ListScrollRows = mod.BindLocalRangeInt("Display", "List Scroll Rows", 4, 1, 20,
                "How many rows a list moves per notch of the mouse wheel.");
            TopCreatureCount = mod.BindLocalRangeInt("Display", "Top Creature Count", 15, 0, 100,
                "How many creatures to list in the Details tab's Creatures killed section, most killed first. 0 hides it.");

            FixTreasureDiscoveryCount = mod.BindLocal("Fixes", "Fix Treasure Discovery Count", true,
                "Correct a bug in Valheim itself that counts a treasure chest as newly found every time it is " +
                "opened. The game marks a chest discovered over a message it never registered a handler for, so " +
                "the mark is never written and the treasure numbers on the stats panel read high. This registers " +
                "the missing handler on chests that keep a discovery stat, which also stops the repeated \"Failed " +
                "to find rpc method 327122920\" warnings in the log. It works while your own game owns the chest, " +
                "which in a dungeon it usually does; a chest held by a player without the mod still miscounts for " +
                "them. Turn off to leave the game exactly as it ships.");
        }
    }
}

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

        // ── display ─────────────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> ShowZeroStats;
        internal static ConfigEntry<string> SortColumn;
        internal static ConfigEntry<bool> SortDescending;
        internal static ConfigEntry<string> CollapsedGroups;
        internal static ConfigEntry<string> PanelSize;
        internal static ConfigEntry<string> PanelPosition;
        internal static ConfigEntry<int> ListScrollRows;
        internal static ConfigEntry<int> TopCreatureCount;

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
                "ways: you stop asking, and you also stop answering, so your stats leave everyone else's " +
                "scoreboard too and the panel shows only yourself.");
            RefreshSeconds = mod.BindLocalRange("Behaviour", "Refresh Seconds", 10f, 0f, 120f,
                "How often to ask the other players again while the panel is open. 0 asks only when the panel opens " +
                "and when you press Refresh.");
            RememberOfflinePlayers = mod.BindLocal("Behaviour", "Remember Offline Players", true,
                "Keep the last stats received from a player on the scoreboard after they log out, marked as offline. " +
                "Only for the rest of the session: nothing is stored, so leaving the world forgets everyone and the " +
                "board is rebuilt from whoever answers next time. Turn off to list only players online right now.");

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
        }
    }
}

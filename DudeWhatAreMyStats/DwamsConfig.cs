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

        // ── the always-on player list ───────────────────────────────────────────────
        internal static ConfigEntry<bool> ShowPlayerList;
        internal static ConfigEntry<KeyboardShortcut> PlayerListKey;
        internal static ConfigEntry<bool> PlayerListIncludeOffline;
        internal static ConfigEntry<float> PlayerListRefreshSeconds;
        internal static ConfigEntry<int> PlayerListMaxRows;
        internal static ConfigEntry<HudCorner> PlayerListCorner;
        internal static ConfigEntry<string> PlayerListOffset;
        internal static ConfigEntry<float> PlayerListFontSize;

        // ── what killed you ────────────────────────────────────────────────────────
        internal static ConfigEntry<bool> RecordDeaths;
        internal static ConfigEntry<bool> RecordBystanders;
        internal static ConfigEntry<float> BystanderSeconds;
        internal static ConfigEntry<float> FightGapSeconds;
        internal static ConfigEntry<int> KeepReports;
        internal static ConfigEntry<int> TopDeathCauses;
        internal static ConfigEntry<bool> LogDeaths;

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

            ShowPlayerList = mod.BindLocal("Player List", "Show Player List", false,
                "Keep a small list of players and how many times each has died on screen all the time, most deaths " +
                "first. It stays up over the map and the inventory, and steps aside only when the game hides its own " +
                "HUD (Ctrl+F3), during cutscenes, behind the pause menu and the death or teleport fade, and while the " +
                "stats panel is open. Off by default. The console command dwams_hud turns it on and off too.");
            PlayerListKey = mod.BindLocal("Player List", "Toggle Key", new KeyboardShortcut(KeyCode.None),
                "A key that turns the player list on and off. Unbound by default, since almost every letter is taken " +
                "by the game or another mod; pick one that is free in your own setup. It stands down while you are " +
                "typing in any text box.");
            PlayerListIncludeOffline = mod.BindLocal("Player List", "Include Offline Players", false,
                "Also list players who are not online, from the server's record of them. Off keeps the list to the " +
                "people actually playing, which is what an always-visible list is usually for. Needs the mod on the " +
                "server to have anyone to show.");
            PlayerListRefreshSeconds = mod.BindLocalRange("Player List", "Refresh Seconds", 30f, 5f, 300f,
                "How often the list asks the other players for their numbers while the stats panel is closed. " +
                "Deaths change rarely, so this can be slow. Your own count is always current.");
            PlayerListMaxRows = mod.BindLocalRangeInt("Player List", "Max Rows", 10, 1, 30,
                "The most players to list. Any beyond that are summed up as \"+N more\".");
            PlayerListCorner = mod.BindLocal("Player List", "Corner", HudCorner.TopLeft,
                "Which corner of the screen the list sits in.");
            PlayerListOffset = mod.BindLocal("Player List", "Offset", "16,300",
                "How far in from that corner, as x,y in pixels at 1080p. Both numbers are measured inward from the " +
                "corner, whichever corner it is. The default sits on the left, below the hotbar; move it if it " +
                "overlaps something else on your screen.");
            PlayerListFontSize = mod.BindLocalRange("Player List", "Font Size", 16f, 10f, 30f,
                "Text size of the player list.");

            RecordDeaths = mod.BindLocal("Deaths", "Record Deaths", true,
                "Keep track of what kills you: which creature, what it was holding, its star level, and everyone " +
                "else who was in the fight. Valheim counts your deaths and sorts them by category but never records " +
                "which creature, so this is the mod watching your fights as they happen. It runs entirely on your own " +
                "game, costs nothing until you are actually in a fight, and needs nothing on the server. Off keeps no " +
                "record and leaves the tables empty; what is already recorded is kept.");
            RecordBystanders = mod.BindLocal("Deaths", "Record Bystanders", true,
                "Also record creatures that were in the fight without landing a blow. Ten greydwarves that surround " +
                "you and drain your stamina are part of why the troll got you, so they belong in the record. This is " +
                "the one part that looks around rather than waiting to be hit: twice a second it checks which of the " +
                "loaded creatures have you as their target. Off records only what actually reached you.");
            BystanderSeconds = mod.BindLocalRange("Deaths", "Bystander Seconds", 3f, 0f, 30f,
                "How long something must have you in its sights before it counts as having been in the fight. This " +
                "keeps a boar that glanced at you from across a field out of the record. Anything that swung at you, " +
                "hurt you or killed you is recorded however brief it was.");
            FightGapSeconds = mod.BindLocalRange("Deaths", "Fight Gap Seconds", 10f, 2f, 60f,
                "How long the fight has to go quiet before it counts as over. Nothing hitting you and nothing " +
                "targeting you for this long starts the next fight from scratch, so a death is described by the fight " +
                "it happened in and not by the one before it.");
            KeepReports = mod.BindLocalRangeInt("Deaths", "Keep Reports", 20, 0, 200,
                "How many deaths to keep the full story of, newest first, readable with the dwams_deaths console " +
                "command. These stay on your own machine: they are far too big to hand around the way the scoreboard " +
                "numbers are. 0 keeps none, and the running totals still count up.");
            TopDeathCauses = mod.BindLocalRangeInt("Deaths", "Top Death Causes", 10, 0, 50,
                "How many rows the Killed by and In the fight tables show in the Details tab, most often first. This " +
                "is also how many travel to the other players, so the scoreboard message stays small. 0 hides them.");
            LogDeaths = mod.BindLocal("Deaths", "Log Deaths", false,
                "Write every death report to the BepInEx log as well, for the record. Off by default; the reports are " +
                "kept either way and dwams_deaths prints them.");

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

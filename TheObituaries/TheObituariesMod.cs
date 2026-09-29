using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TheObituaries
{
    /// <summary>
    /// The Obituaries: every player death is announced to the whole server the way Quake,
    /// Quake II and Quake III announced a frag. "Greg was railed by a Deathsquito." "Marco sank
    /// like a rock." "Anna does a back flip into the lava."
    ///
    /// The dying player's own game composes the line (it is the only one that knows what hit
    /// it last) and sends it to everybody over a routed RPC; every client with the mod shows
    /// it in the chat window and on screen, and a player who killed another player gets Quake
    /// III's "You fragged X". The server needs nothing: an unknown routed RPC is dropped there.
    ///
    /// Client-side only.
    /// </summary>
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInProcess("valheim.exe")]
    public class TheObituariesMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.TheObituaries";
        public const string ModName    = "The Obituaries";
        public const string ModVersion = "0.3.1";

        internal static ManualLogSource Log { get; private set; }
        internal static TheObituariesMod Instance { get; private set; }

        internal enum ScreenSpot  { Off, TopLeft, Center }
        internal enum PronounMode { Auto, He, She, They }

        // [General]
        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<bool> LogObituaries;
        internal static ConfigEntry<bool> LogKillingHit;

        // [Display]
        internal static ConfigEntry<bool>       ShowInChat;
        internal static ConfigEntry<float>      ChatSeconds;
        internal static ConfigEntry<int>        TextSize;
        internal static ConfigEntry<ScreenSpot> OnScreen;
        internal static ConfigEntry<float>      CenterSeconds;
        internal static ConfigEntry<bool>       YouFragged;

        // [Wording]
        internal static ConfigEntry<PronounMode> PronounChoice;
        internal static ConfigEntry<bool>        LevelStars;
        internal static ConfigEntry<bool>        TameNames;

        // [Colors]
        internal static ConfigEntry<string> LineColor;
        internal static ConfigEntry<string> VictimColor;
        internal static ConfigEntry<string> KillerColor;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master switch. Off means your deaths are not announced and other players' " +
                "obituaries are not shown. Takes effect immediately.");
            LogObituaries = Config.Bind("General", "Log Obituaries", true,
                "Also write every obituary you see to the BepInEx log, plain text.");
            LogKillingHit = Config.Bind("General", "Log Killing Hit", true,
                "When you die, write what the game said about the killing hit to the BepInEx log: " +
                "the creature, the weapon it held, the damage types. This is how a creature or weapon " +
                "that reads wrong gets its own line in a later version; send the line along.");

            ShowInChat = Config.Bind("Display", "Show In Chat", true,
                "Add the obituary to the chat window, the way Quake printed it in the console " +
                "notify area. The window pops up for it like it does for a chat message.");
            ChatSeconds = Config.Bind("Display", "Chat Seconds", 30f,
                new ConfigDescription(
                    "How long the chat window stays up after an obituary. The game's own chat " +
                    "messages keep it up for 10 seconds; anything at or below that gives the game's stay.",
                    new AcceptableValueRange<float>(0f, 120f)));
            TextSize = Config.Bind("Display", "Text Size", 115,
                new ConfigDescription(
                    "Size of the obituary in the chat window, as a percentage of the chat's normal text.",
                    new AcceptableValueRange<int>(50, 200)));
            OnScreen = Config.Bind("Display", "On Screen", ScreenSpot.Center,
                "Also show the obituary as an on-screen message: Center (big, in the middle of the " +
                "screen), TopLeft (the small messages next to the minimap), or Off.");
            CenterSeconds = Config.Bind("Display", "Center Seconds", 15f,
                new ConfigDescription(
                    "How long a center message stays on screen. The game's own center messages fade " +
                    "out over 4 seconds; this holds it up and fades it out at the end. You lie there 10 " +
                    "seconds before the respawn starts, and a loading screen covers the rest. " +
                    "Also the stay of 'You fragged'.",
                    new AcceptableValueRange<float>(4f, 60f)));
            YouFragged = Config.Bind("Display", "You Fragged", true,
                "When you kill another player, show Quake III's 'You fragged <name>' in the " +
                "center of your screen.");

            PronounChoice = Config.Bind("Wording", "Pronouns", PronounMode.Auto,
                "The pronouns used for you in your own obituaries ('got his head bashed in', 'fell " +
                "to her death'). Auto picks from your character's body type; He, She or They override it.");
            LevelStars = Config.Bind("Wording", "Level Stars", true,
                "Name a starred creature by its stars: 'a 2-star Troll' instead of 'a Troll'.");
            TameNames = Config.Bind("Wording", "Tame Names", true,
                "Name a tamed creature by its pet name: 'Fluffy the Wolf' instead of 'a Wolf'.");

            LineColor = Config.Bind("Colors", "Line Color", "#ff8c1a",
                "Color of the whole obituary line, as an HTML color (#rrggbb or a name such as " +
                "orange or yellow). Empty leaves it the chat's normal white.");
            VictimColor = Config.Bind("Colors", "Victim Color", "#ffe66d",
                "Color of the dead player's name, as an HTML color. Empty for the line color.");
            KillerColor = Config.Bind("Colors", "Killer Color", "#ff5e5e",
                "Color of the killer's name, as an HTML color. Empty for the line color.");

            MigrateOldDefaults();

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll();

            Commands.Register();

            Log.LogInfo($"{ModName} {ModVersion} loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        /// <summary>
        /// A changed default never touches a value already in the config file, so a file
        /// written by 0.1.0 or 0.2.0 kept the small top-left message and the old victim color,
        /// and 0.3.0's louder display never showed. Once per file: a value still at an old
        /// default moves to the new one; a value the player changed is left alone.
        /// </summary>
        private void MigrateOldDefaults()
        {
            var configVersion = Config.Bind("General", "Config Version", 0,
                "Which version's defaults this file was last brought up to. Internal; leave it alone.");
            if (configVersion.Value >= 2) return;
            if (configVersion.Value < 1)
            {
                // 0.1.0 and 0.2.0 files
                if (OnScreen.Value == ScreenSpot.TopLeft) OnScreen.Value = ScreenSpot.Center;
                if (VictimColor.Value == "#ffa640") VictimColor.Value = "#ffe66d";
            }
            // early 0.3.0 builds defaulted the center stay to 8 and then 10
            if (CenterSeconds.Value == 8f || CenterSeconds.Value == 10f) CenterSeconds.Value = 15f;
            configVersion.Value = 2;
        }

        /// <summary>Wraps text in a color tag if the configured color parses; the bare text otherwise.</summary>
        internal static string Colored(string text, ConfigEntry<string> color)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string c = color?.Value?.Trim() ?? "";
            if (c.Length == 0 || !ColorUtility.TryParseHtmlString(c, out _)) return text;
            return "<color=" + c + ">" + text + "</color>";
        }

        internal static string Bold(string text) => string.IsNullOrEmpty(text) ? (text ?? "") : "<b>" + text + "</b>";
    }
}

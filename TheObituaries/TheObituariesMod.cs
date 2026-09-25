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
    /// it in the chat window and top-left, and a player who killed another player gets Quake
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
        public const string ModVersion = "0.1.0";

        internal static ManualLogSource Log { get; private set; }

        internal enum ScreenSpot { Off, TopLeft, Center }

        // [General]
        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<bool> LogObituaries;

        // [Display]
        internal static ConfigEntry<bool>       ShowInChat;
        internal static ConfigEntry<ScreenSpot> OnScreen;
        internal static ConfigEntry<bool>       YouFragged;

        // [Names]
        internal static ConfigEntry<string> VictimColor;
        internal static ConfigEntry<string> KillerColor;
        internal static ConfigEntry<bool>   LevelStars;
        internal static ConfigEntry<bool>   TameNames;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master switch. Off means your deaths are not announced and other players' " +
                "obituaries are not shown. Takes effect immediately.");
            LogObituaries = Config.Bind("General", "Log Obituaries", true,
                "Also write every obituary you see to the BepInEx log, plain text.");

            ShowInChat = Config.Bind("Display", "Show In Chat", true,
                "Add the obituary to the chat window, the way Quake printed it in the console " +
                "notify area. The window pops up for it like it does for a chat message.");
            OnScreen = Config.Bind("Display", "On Screen", ScreenSpot.TopLeft,
                "Also show the obituary as an on-screen message: TopLeft (the small messages " +
                "next to the minimap), Center (big, in the middle of the screen), or Off.");
            YouFragged = Config.Bind("Display", "You Fragged", true,
                "When you kill another player, show Quake III's 'You fragged <name>' in the " +
                "center of your screen.");

            VictimColor = Config.Bind("Names", "Victim Color", "#ffa640",
                "Color of the dead player's name, as an HTML color (#rrggbb). Empty for none.");
            KillerColor = Config.Bind("Names", "Killer Color", "#ff5e5e",
                "Color of the killer's name, as an HTML color (#rrggbb). Empty for none.");
            LevelStars = Config.Bind("Names", "Level Stars", true,
                "Name a starred creature by its stars: 'a 2-star Troll' instead of 'a Troll'.");
            TameNames = Config.Bind("Names", "Tame Names", true,
                "Name a tamed creature by its pet name: 'Fluffy the Wolf' instead of 'a Wolf'.");

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll();

            Commands.Register();

            Log.LogInfo($"{ModName} {ModVersion} loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        /// <summary>Wraps a name in a color tag if the configured color parses; the bare name otherwise.</summary>
        internal static string Colored(string name, ConfigEntry<string> color)
        {
            string c = color?.Value?.Trim() ?? "";
            if (c.Length == 0 || !ColorUtility.TryParseHtmlString(c, out _)) return name;
            return "<color=" + c + ">" + name + "</color>";
        }
    }
}

using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace OneClickLaunch
{
    /// <summary>
    /// One Click Launch: one click from the main menu back into the game you were playing.
    ///
    /// Vanilla makes you pick a character, then a world, then Start, and remembers the two
    /// separately: change the world and it happily loads the character you used last time into
    /// it. This mod remembers each character together with the world (or server) they were
    /// actually played in, and puts a button for each recent pair at the top of the main menu.
    /// Click one and the game starts. It also warns before the vanilla Start button loads a
    /// character into a world that character has never been in.
    ///
    /// Client-side only; nothing goes to the server.
    /// </summary>
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInProcess("valheim.exe")]
    public class OneClickLaunchMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.OneClickLaunch";
        public const string ModName    = "One Click Launch";
        public const string ModVersion = "1.0.0";

        internal static ManualLogSource Log { get; private set; }
        internal static OneClickLaunchMod Instance { get; private set; }

        internal static ConfigEntry<bool>   ModEnabled;
        internal static ConfigEntry<int>    ButtonCount;
        internal static ConfigEntry<int>    HistorySize;
        internal static ConfigEntry<string> WorldLabel;
        internal static ConfigEntry<string> ServerLabel;
        internal static ConfigEntry<bool>   RememberPasswords;
        internal static ConfigEntry<bool>   WarnOnUnknownWorld;
        internal static ConfigEntry<int>    MenuBottomMargin;
        internal static ConfigEntry<string> ButtonColorSetting;
        internal static ConfigEntry<bool>   Divider;
        internal static ConfigEntry<bool>   MoreButton;

        private readonly Harmony _harmony = new Harmony(ModGuid);

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master toggle. Set to false to disable every patch without removing the DLL. " +
                "Requires a game restart to take effect.");
            ButtonCount = Config.Bind("General", "Buttons", 3,
                new ConfigDescription(
                    "How many recent character + world pairs get a button at the top of the main menu, " +
                    "most recent first. The history keeps more than this (see History Size), so raising " +
                    "it later brings older pairs back.",
                    new AcceptableValueRange<int>(1, 8)));
            HistorySize = Config.Bind("General", "History Size", 100,
                new ConfigDescription(
                    "How many character + world pairs to remember, most recent first. Each pair is one " +
                    "entry however often it is played, so the list only fills up with distinct pairs. " +
                    "Lowering it drops the oldest entries the next time the game starts.",
                    new AcceptableValueRange<int>(1, 1000)));
            WorldLabel = Config.Bind("General", "World Button Label", "{character} in {world}",
                "Text of a button that starts a local world. {character} is the character's name and " +
                "{world} the world's name.");
            ServerLabel = Config.Bind("General", "Server Button Label", "{character} on {server}",
                "Text of a button that joins a server. {character} is the character's name and " +
                "{server} the server's name, or its address if the name is not known.");
            RememberPasswords = Config.Bind("General", "Remember Passwords", true,
                "Remember the password you typed for a server, or set for a world you hosted, so the " +
                "button can enter it for you. Stored in BepInEx/config/DeathMonger.OneClickLaunch.history.json, " +
                "encrypted with Windows DPAPI so only your Windows account on this computer can read it " +
                "(elsewhere, Mono's per-user protection; plain text only if neither is available, and " +
                "the log says so). Turn off to be asked every time; passwords already stored are " +
                "dropped on the next launch.");
            WarnOnUnknownWorld = Config.Bind("General", "Warn On Unknown World", true,
                "When the vanilla Start button is about to load a character into a world that " +
                "character has never been in, ask first. A brand-new character with no worlds yet is " +
                "never asked. The Continue buttons are pairs that were played before, so they never ask.");

            MenuBottomMargin = Config.Bind("General", "Menu Bottom Margin", 40,
                new ConfigDescription(
                    "The main menu's buttons stack downward, and every button a mod adds (this one's, " +
                    "ServerConnect's) pushes Quit further down. When the lowest button would end up closer " +
                    "than this to the bottom of the screen, the whole list is lifted to keep it there. " +
                    "In UI units at the game's reference resolution. Set to a negative value to disable.",
                    new AcceptableValueRange<int>(-1, 400)));

            ButtonColorSetting = Config.Bind("Look", "Button Color", "#F5D76E",
                "Text color of the Continue buttons, so they stand out from the vanilla ones. A hex " +
                "color like #F5D76E, or a name like yellow, white, orange. Blank for the vanilla color.");
            Divider = Config.Bind("Look", "Divider", true,
                "Draw a thin line between the Continue buttons and the vanilla menu.");
            MoreButton = Config.Bind("Look", "More Button", false,
                "Add a More... button under the Continue buttons that opens the full list of remembered " +
                "games. Right-clicking any Continue button opens the same list, so this is off by default.");

            if (!ModEnabled.Value)
            {
                Log.LogInfo($"{ModName} {ModVersion} is disabled in its config; nothing patched.");
                return;
            }

            History.Load();
            _harmony.PatchAll(typeof(OneClickLaunchMod).Assembly);
            Log.LogInfo($"{ModName} {ModVersion} loaded; {History.Entries.Count} remembered pair(s).");
        }

        // A mod manager's config editor writes the file while the game runs, and BepInEx does
        // not watch it. Poll its timestamp once a second and reload on a change, which raises
        // SettingChanged for every value that differs; the menu rebuilds from that.
        private DateTime _configWriteTime;
        private float _nextConfigCheck;

        private void Update()
        {
            if (Time.unscaledTime < _nextConfigCheck) return;
            _nextConfigCheck = Time.unscaledTime + 1f;
            try
            {
                string path = Config.ConfigFilePath;
                if (!File.Exists(path)) return;
                DateTime written = File.GetLastWriteTimeUtc(path);
                if (written == _configWriteTime) return;
                bool first = _configWriteTime == default;
                _configWriteTime = written;
                if (first) return;
                Config.Reload();
                // Reloading can make BepInEx save the file again; that write is not an edit.
                _configWriteTime = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex)
            {
                Log.LogDebug($"Config check skipped: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }
    }
}

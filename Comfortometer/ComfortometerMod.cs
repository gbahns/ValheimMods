using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Comfortometer
{
    /// <summary>
    /// Comfortometer: a small HUD panel that stays up while you walk around, listing every
    /// piece that gives you comfort right now, how far away each one is, and how many of the
    /// same kind are in range. Walk out of range of something and it stays on the list in red,
    /// so you can see what you just lost and go back for it.
    ///
    /// The list is built exactly the way the game builds the comfort level: the same 10 m
    /// radius, the same "one per group, one per name" rule, and the same shelter test. Nothing
    /// is sent anywhere and the server needs nothing. Client-side only.
    /// </summary>
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInProcess("valheim.exe")]
    public class ComfortometerMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.Comfortometer";
        public const string ModName    = "Comfortometer";
        public const string ModVersion = "0.1.0";

        internal static ManualLogSource Log { get; private set; }

        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<bool> OpenAtStart;
        internal static ConfigEntry<float> ForgetBeyond;
        internal static ConfigEntry<bool> ShowSuperseded;
        internal static ConfigEntry<bool> ShowRestedTime;
        internal static ConfigEntry<bool> ShowRange;
        internal static ConfigEntry<bool> BoldNames;
        internal static ConfigEntry<bool> AutoHeight;
        internal static ConfigEntry<float> FontSize;
        internal static ConfigEntry<string> PanelPosition;
        internal static ConfigEntry<string> PanelSize;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master switch. Off hides the panel and ignores the hotkeys. Takes effect immediately.");
            ToggleKey = Config.Bind("General", "Toggle Panel", new KeyboardShortcut(KeyCode.F4),
                "Shows the comfort panel, which stays up while you move around. While it is up, the " +
                "same key frees the mouse so you can drag the panel by its title, resize it by the grip " +
                "in its lower-right corner, or close it with the x; press it again to give the mouse " +
                "back to the camera. You can still walk around meanwhile. The panel can also be used " +
                "whenever the cursor is already free, such as with the inventory open.");
            OpenAtStart = Config.Bind("General", "Open At Start", false,
                "Show the panel as soon as you spawn in, without pressing the toggle key.");
            ForgetBeyond = Config.Bind("General", "Forget Beyond", 30f,
                new ConfigDescription("A piece you have walked out of range of stays on the list in red until " +
                    "you are this many meters from it. Comfort range itself is 10 m and cannot be changed here.",
                    new AcceptableValueRange<float>(10f, 200f)));
            ShowSuperseded = Config.Bind("General", "Show Superseded", false,
                "Also list pieces in range that give nothing because a better piece of the same group " +
                "is also in range, such as a stool next to a chair. They are dimmed, with the group named.");
            ShowRestedTime = Config.Bind("General", "Show Rested Time", true,
                "Show how long the Rested effect lasts at the current comfort level, next to the level.");
            ShowRange = Config.Bind("General", "Show Range", false,
                "Show each piece's distance against the range it counts within, as in 8.2 / 10 m. The " +
                "range is the same for every piece: 10 m, fixed by the game.");
            BoldNames = Config.Bind("Display", "Bold Names", true,
                "Draw the piece names in bold.");
            AutoHeight = Config.Bind("Display", "Auto Height", true,
                "The panel grows and shrinks to fit its list. The grip then only changes the width. Off " +
                "keeps whatever height you drag it to, and a list that does not fit ends in '+N more'.");
            FontSize = Config.Bind("Display", "Font Size", 15f,
                new ConfigDescription("Text size in the panel. Row height follows it.",
                    new AcceptableValueRange<float>(10f, 30f)));
            PanelPosition = Config.Bind("Display", "Panel Position", "",
                "Where the panel sits, as x,y from the center of the screen. Written when you drag it; " +
                "empty means the default spot near the top-right.");
            PanelSize = Config.Bind("Display", "Panel Size", "",
                "The panel's width,height. Written when you resize it; empty means the default size.");

            ModEnabled.SettingChanged += (_, __) => { if (!ModEnabled.Value) ComfortPanel.Close(); };
            FontSize.SettingChanged += (_, __) => ComfortPanel.Rebuild();
            ShowSuperseded.SettingChanged += (_, __) => ComfortPanel.Refresh();
            ShowRestedTime.SettingChanged += (_, __) => ComfortPanel.Refresh();
            ShowRange.SettingChanged += (_, __) => ComfortPanel.Refresh();
            BoldNames.SettingChanged += (_, __) => ComfortPanel.Refresh();
            AutoHeight.SettingChanged += (_, __) => ComfortPanel.Refresh();

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll();
            Log.LogInfo($"{ModName} {ModVersion} loaded.");
        }

        private void Update()
        {
            if (!ModEnabled.Value) return;
            ComfortPanel.Tick();
            if (Player.m_localPlayer == null) return;

            UiBits.ObserveFocus();
            if (!Keys.CanTakeInput()) return;

            // One key: shows the panel, then toggles the mouse focus that lets you arrange or close it.
            if (Keys.IsDown(ToggleKey.Value))
            {
                if (!ComfortPanel.IsOpen) ComfortPanel.Open();
                else ComfortPanel.SetArranging(!ComfortPanel.Arranging);
            }
        }

        private void OnDestroy()
        {
            ComfortPanel.Close();
            _harmony?.UnpatchSelf();
        }
    }

    /// <summary>Shows the panel on spawn when the player asked for that.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class Player_OnSpawned_OpenPanel
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            if (!ComfortometerMod.ModEnabled.Value || !ComfortometerMod.OpenAtStart.Value) return;
            ComfortPanel.Open();
        }
    }
}

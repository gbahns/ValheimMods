using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PauseMyServer
{
    /// <summary>
    /// Pause My Server: pause a server-hosted game.
    ///
    /// Vanilla refuses to pause as soon as anyone is connected to a server. With this mod:
    ///  * the world pauses when every player online has the ESC menu open; alone, that is just
    ///    you, the way solo does (v1.0 for the lone player, v1.2 for everyone);
    ///  * an admin can pause the whole server for everyone with a key (default: Pause), even
    ///    with other players online; players joining meanwhile are frozen too, and any admin
    ///    can resume (v1.1).
    /// The server keeps its clock, raids and sleep time-skips frozen in step, and a persistent
    /// "Game paused" label is shown on screen while paused.
    ///
    /// Install on the server and on every client.
    /// </summary>
    // No [BepInProcess] filter on purpose: the mod must load in the client, the Windows dedicated
    // server (valheim_server.exe) and the Linux one (valheim_server.x86_64), and BepInEx matches
    // that attribute against the bare process name.
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class PauseMyServerMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.PauseMyServer";
        public const string ModName    = "Pause My Server";
        public const string ModVersion = "1.2.0";

        internal static ManualLogSource Log { get; private set; }

        // Master toggle, so a player who runs into a problem can switch the mod off in
        // BepInEx/config/DeathMonger.PauseMyServer.cfg without removing the DLL.
        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<bool> ShowMessages;

        internal static ConfigEntry<KeyboardShortcut> PauseKey;

        internal static ConfigEntry<bool> ShowPauseMessage;
        internal static ConfigEntry<string> PauseMessage;
        internal static ConfigEntry<string> AdminText;
        internal static ConfigEntry<PauseOverlay.Position> PauseMessagePosition;
        internal static ConfigEntry<int> PauseMessageSize;
        internal static ConfigEntry<bool> ShowUnpausedWarning;
        internal static ConfigEntry<string> UnpausedText;
        internal static ConfigEntry<string> UnpausedCountText;

        private readonly Harmony _harmony = new Harmony(ModGuid);

        private void Awake()
        {
            Log = Logger;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master toggle for the entire mod. Set to false to disable every patch without removing " +
                "the DLL. Requires a game restart to take effect.");
            ShowMessages = Config.Bind("General", "Show Messages", true,
                "Show a small top-left HUD message when the game resumes for a reason other than you " +
                "closing your menu: an admin lifted the pause, or another player came online while your " +
                "menu was still open. Client-side only.");

            PauseKey = Config.Bind("Admin", "Pause Key", new KeyboardShortcut(KeyCode.Pause),
                "Admins only: toggle the server-wide pause for everyone. Ignored while typing in chat, the " +
                "console or a text box. Non-admins get a notice. The console command pms_pause does the same.");

            ShowPauseMessage = Config.Bind("Pause Message", "Show Pause Message", true,
                "Show a persistent on-screen label while the game is paused. Also shown when pausing a " +
                "solo game. Client-side only.");
            PauseMessage = Config.Bind("Pause Message", "Text", "Game paused",
                "The label text.");
            AdminText = Config.Bind("Pause Message", "Admin Text", "Game paused by {0}",
                "The label text during an admin pause; {0} is replaced by the admin's name.");
            PauseMessagePosition = Config.Bind("Pause Message", "Position", PauseOverlay.Position.Bottom,
                "Where the label sits, centred at the Top or the Bottom of the screen.");
            PauseMessageSize = Config.Bind("Pause Message", "Font Size", 40,
                new ConfigDescription("Label font size (at a 1920x1080 reference; scales with the screen).",
                    new AcceptableValueRange<int>(12, 120)));
            ShowUnpausedWarning = Config.Bind("Pause Message", "Show Unpaused Warning", true,
                "Show the label in bright red while the ESC menu is up but the game keeps running because " +
                "other players are online.");
            UnpausedText = Config.Bind("Pause Message", "Unpaused Text", "Game Unpaused",
                "The label text for that warning when the server has not reported how many players want to pause.");
            UnpausedCountText = Config.Bind("Pause Message", "Unpaused Count Text", "Game Unpaused ({0} of {1} players paused)",
                "The label text for that warning once the server reports the counts: {0} players have their menu open, {1} are online.");

            if (!ModEnabled.Value)
            {
                Log.LogInfo("[PauseMyServer] Mod Enabled = false in config; skipping patches.");
                return;
            }

            Commands.Register();
            _harmony.PatchAll();
            Log.LogInfo($"[PauseMyServer] {ModVersion} loaded.");
        }

        private void Update()
        {
            if (!ModEnabled.Value) return;
            PauseSync.Update();
            PauseSync.UpdateInput();
            PauseOverlay.Update();
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }

        /// <summary>Small top-left HUD message. Respects the Show Messages toggle unless <paramref name="always"/>.</summary>
        internal static void Message(string text, bool always = false)
        {
            if (!always && (ShowMessages == null || !ShowMessages.Value)) return;
            if (MessageHud.instance == null) return;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }
    }
}

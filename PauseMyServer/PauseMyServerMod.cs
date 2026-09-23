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
    ///  * the world pauses when every player online has asked for a pause (the ESC menu, or any
    ///    mod that calls Game.Pause()); alone, that is just you, the way solo does;
    ///  * an admin can pause the whole server for everyone with a key (default: Pause), even
    ///    with other players online; players joining meanwhile are frozen too, and any admin
    ///    can resume (v1.1).
    /// The server keeps its clock, raids and sleep time-skips frozen in step, and a persistent
    /// "Paused" label is shown on screen while paused.
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
        public const string ModVersion = "1.5.0";

        internal static ManualLogSource Log { get; private set; }

        // Master toggle, so a player who runs into a problem can switch the mod off in
        // BepInEx/config/DeathMonger.PauseMyServer.cfg without removing the DLL.
        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<bool> ShowMessages;

        internal static ConfigEntry<KeyboardShortcut> PauseKey;

        internal static ConfigEntry<bool> PublishStateFile;

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

            PublishStateFile = Config.Bind("Server", "Publish State File", true,
                "Servers only: keep BepInEx/config/PauseMyServer.state.json up to date with the pause " +
                "state, so a monitor or dashboard can read it instead of scraping the log. Rewritten on " +
                "every change and every few seconds as a heartbeat, and deleted when the world shuts down.");
            PauseStateFile.Version = ModVersion;
            PauseStateFile.Enabled = PublishStateFile.Value;

            ShowPauseMessage = Config.Bind("Pause Message", "Show Pause Message", true,
                "Show a persistent on-screen label while the game is paused. Also shown when pausing a " +
                "solo game. Client-side only.");
            PauseMessage = BindText("Text", "Paused",
                "The label text.", "Game paused");
            AdminText = BindText("Admin Text", "Paused by {0}",
                "The label text during an admin pause; {0} is replaced by the admin's name.", "Game paused by {0}");
            PauseMessagePosition = Config.Bind("Pause Message", "Position", PauseOverlay.Position.Bottom,
                "Where the label sits, centered at the Top or the Bottom of the screen.");
            PauseMessageSize = Config.Bind("Pause Message", "Font Size", 40,
                new ConfigDescription("Label font size (at a 1920x1080 reference; scales with the screen).",
                    new AcceptableValueRange<int>(12, 120)));
            ShowUnpausedWarning = Config.Bind("Pause Message", "Show Unpaused Warning", true,
                "Show the label in bright red while something has asked for a pause and the game is still " +
                "running: the ESC menu, or any mod that pauses for an open map or inventory panel.");
            UnpausedText = BindText("Unpaused Text", "Unpaused",
                "The first line of that warning.", "Game Unpaused");
            UnpausedCountText = BindText("Unpaused Count Text", "{0}/{1} want to pause",
                "The second line of that warning, in a smaller font, shown once the server reports the counts: " +
                "{0} players have asked for a pause, {1} are online. Leave it empty for no second line.",
                "Game Unpaused ({0} of {1} players paused)");

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
            // Update, not a coroutine: this keeps running at time scale zero, so the heartbeat
            // survives the very pause it is reporting.
            if (ZNet.instance != null && ZNet.instance.IsServer()) PauseStateFile.Heartbeat();
        }

        private void OnDestroy()
        {
            PauseStateFile.Shutdown();
            _harmony.UnpatchSelf();
        }

        /// <summary>
        /// Binds one of the label texts, and replaces a stored value that is still an old release's
        /// default with the current one. BepInEx writes every default into the .cfg file, so without
        /// this the old wording would stick for everyone who already has a config. A text the player
        /// actually chose is never touched.
        /// </summary>
        private ConfigEntry<string> BindText(string key, string defaultValue, string description, params string[] supersededDefaults)
        {
            var entry = Config.Bind("Pause Message", key, defaultValue, description);
            foreach (var superseded in supersededDefaults)
            {
                if (entry.Value != superseded) continue;
                entry.Value = defaultValue;
                Log.LogInfo($"[PauseMyServer] Config '{key}' still held the old default; updated it to \"{defaultValue}\".");
                break;
            }
            return entry;
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

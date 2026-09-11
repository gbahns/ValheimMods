using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace PauseMyServer
{
    /// <summary>
    /// Pause My Server: the ESC menu pauses a server-hosted game the way it pauses a solo game.
    ///
    /// Vanilla refuses to pause as soon as anyone is connected to a server. With this mod, the
    /// one player who is alone on a dedicated server freezes the world when the menu opens, and
    /// the server keeps its clock, raids and sleep time-skips frozen in step. A second player
    /// joining, or the pausing player leaving, resumes the world at once.
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
        public const string ModVersion = "1.0.0";

        internal static ManualLogSource Log { get; private set; }

        // Master toggle, so a player who runs into a problem can switch the mod off in
        // BepInEx/config/DeathMonger.PauseMyServer.cfg without removing the DLL.
        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<bool> ShowMessages;

        private readonly Harmony _harmony = new Harmony(ModGuid);

        private void Awake()
        {
            Log = Logger;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master toggle for the entire mod. Set to false to disable every patch without removing " +
                "the DLL. Requires a game restart to take effect.");
            ShowMessages = Config.Bind("General", "Show Messages", true,
                "Show a small top-left HUD message when the world is paused, and when it resumes because " +
                "another player came online while your menu was still open. Client-side only.");

            if (!ModEnabled.Value)
            {
                Log.LogInfo("[PauseMyServer] Mod Enabled = false in config; skipping patches.");
                return;
            }

            _harmony.PatchAll();
            Log.LogInfo($"[PauseMyServer] {ModVersion} loaded.");
        }

        private void Update()
        {
            if (!ModEnabled.Value) return;
            PauseSync.Update();
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }

        /// <summary>Small top-left HUD message, respecting the Show Messages toggle.</summary>
        internal static void Message(string text)
        {
            if (ShowMessages == null || !ShowMessages.Value) return;
            if (MessageHud.instance == null) return;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }
    }
}

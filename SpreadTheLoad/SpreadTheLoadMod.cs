using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SpreadTheLoad
{
    /// <summary>
    /// Keeps a struggling machine from being handed the simulation of things everyone else is
    /// using.
    ///
    /// Valheim runs a creature's AI, and answers an interaction with any object, only on the
    /// machine that owns it. Ownership goes to whoever was in range first and vanilla never
    /// rebalances it: whoever loads a zone keeps it until they walk away. When that machine is
    /// the slowest in the group, everybody else's axe swings and arrows route through it, and
    /// they all inherit its frame time.
    ///
    /// Measured on a three-player server: two clients at 18 ms a frame, one at 63 ms, and a
    /// round trip through the slow one of 144 ms against an 18 ms ping to the server.
    ///
    /// This does not make the slow machine faster - it has nothing to do with frame rate on the
    /// machine it steers away from, and measurement showed ownership made no difference to it
    /// either way. It protects everyone else from inheriting that machine's frame time when they
    /// touch something it happens to own.
    ///
    /// Server-side only, and it has to be: ZDOMan.Update runs the whole ownership handout inside
    /// `if (ZNet.instance.IsServer())`. Nobody else installs anything, and it works for players
    /// who run no mods at all.
    /// </summary>
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class SpreadTheLoadMod : BaseUnityPlugin
    {
        public const string ModGuid = "DeathMonger.SpreadTheLoad";
        public const string ModName = "Spread The Load";
        public const string ModVersion = "0.1.3";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> YieldPlayers;
        internal static ConfigEntry<bool> LogActivity;
        internal static ConfigEntry<bool> RememberIds;
        internal static ConfigEntry<bool> AutoDetect;
        internal static ConfigEntry<int> StallsPerMinute;
        internal static ConfigEntry<bool> OwnershipFollowsAttacker;
        internal static ConfigEntry<float> AttackerDwellSeconds;
        internal static ConfigEntry<bool> AssignShipToCaptain;
        internal static ConfigEntry<bool> YieldingRetainsHelm;

        private Harmony _harmony;
        private float _nextJudge;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Steer ownership of shared objects away from the players named below. " +
                "Turn this off to get stock Valheim behaviour back without removing the mod.");

            YieldPlayers = Config.Bind("General", "Yield Players", "",
                "Comma-separated list of the players whose machines should not be given objects " +
                "somebody else is also standing near. They still own anything only they are near, " +
                "so nothing is ever left unsimulated. Empty means the mod does nothing. " +
                "Each entry is either a Steam id (the 17-digit number the server logs as " +
                "\"Got connection SteamID ...\") or a character name. A character name is not an " +
                "identity - the same person on a second character stops matching - so a name is " +
                "used once to find the player and their id is then remembered, after which the " +
                "name no longer matters. Ids are the reliable thing to enter.");

            RememberIds = Config.Bind("General", "Remember Ids", true,
                "When a character name here matches a connected player, write down their network " +
                "id so they keep matching on any character. Ids are kept in " +
                "SpreadTheLoad-known-ids.txt beside this file; delete a line there to forget " +
                "one. Turn this off to match strictly on what is typed above.");

            AutoDetect = Config.Bind("Auto Detect", "Auto Detect Struggling Players", false,
                "Work out for itself which machines are struggling and steer objects away from " +
                "them, without naming anyone. Detection is by stalls: the server times the gaps " +
                "between the updates each client sends, and a gap means that machine stopped. It " +
                "cannot tell a frozen machine from a hiccuping connection, which is fine - routing " +
                "other players' work through either is a bad idea. Note that ping and connection " +
                "quality are NOT used: the client this was written for had a 24 ms ping and ran at " +
                "16 fps. Flags are held in memory only, never written to the known-ids file, and " +
                "are dropped when the player disconnects.");

            StallsPerMinute = Config.Bind("Auto Detect", "Stalls Per Minute", 6,
                "How many stalls a minute, averaged over two minutes, before a player is flagged. " +
                "A stall is a gap over 0.3s in the updates they send; healthy clients manage about " +
                "one update every 0.05s. Clearing the flag needs five clean minutes - deliberately " +
                "harder than setting it, so a borderline machine does not flap ownership back and " +
                "forth. The last healthy player on the server is never flagged.");

            OwnershipFollowsAttacker = Config.Bind("Interacting", "Ownership Follows Attacker", true,
                "Give a tree, rock or ore vein to whoever is hitting it. Vanilla never does: the " +
                "machine that loaded it keeps it, so every swing anyone else makes travels to that " +
                "machine and back, for that tree and the next one. A chopping session is hundreds " +
                "of interactions against a few objects, which makes this the commonest way a group " +
                "feels somebody else's frame time. " +
                "Resources only. A creature carries live AI state that is not all replicated, so " +
                "moving one mid-fight can make it re-acquire its target or re-path; that is left " +
                "alone until it can be measured rather than guessed at.");

            AttackerDwellSeconds = Config.Bind("Interacting", "Attacker Dwell Seconds", 5f,
                "How long an object stays put after being handed to somebody, so two players " +
                "working the same tree cannot bounce it back and forth between them.");

            AssignShipToCaptain = Config.Bind("Ships", "Assign Ship To Captain", true,
                "Give a ship to whoever is steering it. Vanilla only moves a ship when its owner " +
                "is not aboard, and then picks an arbitrary passenger rather than the captain, so " +
                "the person at the helm often does not own the hull their steering has to reach. " +
                "Steering is batched to the owner every 0.2s and the physics runs only there, so a " +
                "captain who does not own the ship waits about a quarter second for every turn.");

            YieldingRetainsHelm = Config.Bind("Ships", "Yielding Player Retains Boat Helm Ownership", true,
                "Whether a player listed in Yield Players still gets the ship when they are the one " +
                "steering it. On (default) they keep the helm: their steering stays responsive, at " +
                "the cost of the hull lurching for everyone aboard whenever their machine stalls. " +
                "Off, the ship goes to someone else and they steer through a delay - smoother for " +
                "passengers, but a captain fighting a mushy helm is the one who hits the rocks. " +
                "Which is better has been reasoned about but not measured; try both.");

            LogActivity = Config.Bind("General", "Log Activity", false,
                "Write a line to the server log now and then saying how many objects were moved " +
                "and who they came from. Off by default; it is a summary, never one line per object.");

            SetupConfigWatcher();

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll();
            Log.LogInfo($"[SpreadTheLoad] {ModVersion} loaded.");
        }

        /// <summary>
        /// Re-reads the config file when it changes on disk.
        ///
        /// BepInEx does not do this by itself: a ConfigEntry holds the value parsed at startup, so
        /// without a watcher an edited file does nothing until the process restarts. On a dedicated
        /// server that is the difference between changing a setting and disconnecting everybody to
        /// change a setting - which matters most for exactly the settings worth changing mid-session,
        /// like who is being steered away from.
        ///
        /// Everything here reads .Value each pass rather than caching it, so a reload takes effect
        /// within a second with nothing else to do.
        /// </summary>
        private FileSystemWatcher _configWatcher;

        private void SetupConfigWatcher()
        {
            try
            {
                _configWatcher = new FileSystemWatcher(Paths.ConfigPath, Path.GetFileName(Config.ConfigFilePath));
                _configWatcher.Changed += OnConfigFileChanged;
                _configWatcher.Created += OnConfigFileChanged;
                _configWatcher.Renamed += OnConfigFileChanged;
                _configWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
                _configWatcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                Log.LogWarning($"[SpreadTheLoad] could not watch the config file for changes: {e.Message}");
            }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(Config.ConfigFilePath)) return;
            try
            {
                Config.Reload();
                Log.LogInfo("[SpreadTheLoad] config reloaded; the new settings are in effect.");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[SpreadTheLoad] could not reload the config: {ex.Message}");
            }
        }

        /// <summary>
        /// Drives the conflict watch. It lives here rather than in a patch on purpose: the failure
        /// being watched for is another mod replacing the method this one hooks, so a heartbeat
        /// that depended on that same method could be silenced by the very thing it is meant to
        /// detect. A plugin's own Update always runs.
        /// </summary>
        private void Update()
        {
            var znet = ZNet.instance;
            if (znet == null) { Conflicts.Tick(Time.unscaledTime, false, 0); return; }
            try
            {
                Conflicts.Tick(Time.unscaledTime, znet.IsServer(), znet.GetConnectedPeers().Count);
                Ships.Tick(Time.unscaledTime);
                Attackers.Tick(Time.unscaledTime);
                if (Time.unscaledTime >= _nextJudge)
                {
                    _nextJudge = Time.unscaledTime + 1f;
                    Detection.Tick(Time.unscaledTime, znet);
                }
            }
            catch { /* the watch is a convenience; it must never take the server down */ }
        }

        private void OnDestroy()
        {
            _configWatcher?.Dispose();
            _harmony?.UnpatchSelf();
        }
    }
}

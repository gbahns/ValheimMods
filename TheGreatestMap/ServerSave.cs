using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// World saving on the server, for hosts whose panel stop is a hard kill (DatHost's is: no
    /// shutdown save ever appears in its console) and whose dedicated server ignores console
    /// input.
    ///  * A file named "save-now" in BepInEx/config/TheGreatestMap/ makes the server save the
    ///    world and every player's profile at once, then the file is deleted. A deploy script
    ///    can create it over the host's file API and watch the console for the save.
    ///  * "Server Autosave Minutes" adds an extra periodic save on top of vanilla's.
    /// Both are server-only: they do nothing on clients.
    /// </summary>
    internal static class ServerSave
    {
        private const string TriggerName = "save-now";
        private static float _nextCheck;
        private static float _nextAutosave = -1f;

        private static string TriggerPath => Path.Combine(Paths.ConfigPath, "TheGreatestMap", TriggerName);

        internal static void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            float now = Time.unscaledTime;
            if (now < _nextCheck) return;
            _nextCheck = now + 2f;

            try
            {
                string path = TriggerPath;
                if (File.Exists(path))
                {
                    File.Delete(path);
                    TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] save-now trigger found: saving the world and player profiles.");
                    ZNet.instance.SaveWorldAndPlayerProfiles();
                }
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] save-now trigger failed: {e.Message}");
            }

            float minutes = TgmConfig.ServerAutosaveMinutes.Value;
            if (minutes <= 0f) { _nextAutosave = -1f; return; }
            if (_nextAutosave < 0f) { _nextAutosave = now + minutes * 60f; return; }
            if (now >= _nextAutosave)
            {
                _nextAutosave = now + minutes * 60f;
                TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Extra autosave ({minutes:0.#} min interval).");
                ZNet.instance.SaveWorldAndPlayerProfiles();
            }
        }
    }
}

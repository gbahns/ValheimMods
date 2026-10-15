using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SmartSilencer
{
    /// <summary>
    /// Stops the game asking for music it does not have, over and over.
    ///
    /// A music volume names the track it wants, and MusicMan looks that name up in the list it
    /// built at startup. Ask for a name that is not in the list and it says so:
    ///
    ///     if (StartMusic(m_locationMusic)) m_lastLocationMusic = m_locationMusic;
    ///     else { ZLog.Log("Location music missing: " + m_locationMusic); m_locationMusic = null; }
    ///
    /// Clearing the name would end it, except the volume re-asserts the name every tick the player
    /// is inside it, so the lookup fails, logs and clears again - for as long as they stand there.
    /// Valheim ships at least one of these: the prefab Music_FrostCavesSanctum asks for
    /// "frostcaves", and no track of that name exists, nor any frost cave music anywhere in the
    /// game's audio. It fills the log at frame rate while a player is in that cave.
    ///
    /// So a name MusicMan cannot play is dropped before it is set. Nothing is silenced that would
    /// otherwise have been heard: the only names dropped are ones whose lookup can only fail.
    /// A null or empty name is always let through, because that is how a volume says "stop".
    ///
    /// If Valheim ever ships the missing track, the name will be found and this stops doing
    /// anything, without a code change.
    /// </summary>
    internal static class LocationMusic
    {
        // The names MusicMan can actually play, and what they were read from. Rebuilt when the
        // music list changes, which in practice means once, when a world loads.
        private static readonly HashSet<string> _playable = new HashSet<string>(StringComparer.Ordinal);
        private static MusicMan _readFrom;
        private static int _readCount = -1;

        // Names already reported, so a dropped name is mentioned once rather than per frame -
        // which is the whole point of this.
        private static readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Whether this location music name should be dropped rather than set.</summary>
        internal static bool ShouldDrop(MusicMan man, string name)
        {
            try
            {
                if (SmartSilencerMod.QuietMissingLocationMusic == null
                    || !SmartSilencerMod.QuietMissingLocationMusic.Value) return false;
                if (man == null || string.IsNullOrEmpty(name)) return false;

                var list = man.m_music;
                if (list == null || list.Count == 0) return false;   // nothing read yet: leave it alone

                if (!ReferenceEquals(man, _readFrom) || list.Count != _readCount) Reread(man, list);
                if (_playable.Contains(name)) return false;

                if (_reported.Add(name) && SmartSilencerMod.LogTransitions != null
                    && SmartSilencerMod.LogTransitions.Value)
                {
                    SmartSilencerMod.Log.LogInfo(
                        $"[SmartSilencer] No music named '{name}' is loaded; not asking for it again " +
                        "(the game would log that once a frame).");
                }
                return true;
            }
            catch (Exception e)
            {
                // Whatever went wrong here, the game's own behaviour is the safe answer.
                SmartSilencerMod.Log.LogWarning($"[SmartSilencer] Location music check failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// The names MusicMan registered, by its own rule: enabled, and with a first clip. An entry
        /// that fails that test is one MusicMan left out of its lookup, so asking for it fails too.
        /// </summary>
        private static void Reread(MusicMan man, List<MusicMan.NamedMusic> list)
        {
            _playable.Clear();
            _reported.Clear();
            _readFrom = man;
            _readCount = list.Count;
            foreach (var music in list)
            {
                if (music == null || string.IsNullOrEmpty(music.m_name)) continue;
                if (!music.m_enabled) continue;
                if (music.m_clips == null || music.m_clips.Length == 0 || music.m_clips[0] == null) continue;
                _playable.Add(music.m_name);
            }
        }
    }

    [HarmonyPatch(typeof(MusicMan), nameof(MusicMan.LocationMusic))]
    internal static class MusicMan_LocationMusic_Patch
    {
        private static bool Prefix(MusicMan __instance, string name)
        {
            return !LocationMusic.ShouldDrop(__instance, name);
        }
    }
}

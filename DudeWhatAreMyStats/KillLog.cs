using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>What this mod has watched this character kill, per species.</summary>
    internal sealed class KillTally
    {
        /// <summary>Kills this mod saw, which is always less than the game's own count to begin with.</summary>
        internal float Seen;
        /// <summary>Of those, the ones that were somebody's pet.</summary>
        internal float Tame;
        /// <summary>Of those, the ones carrying at least one star.</summary>
        internal float Starred;
        /// <summary>The highest star level of this species this character has killed.</summary>
        internal int BestLevel = 1;
    }

    /// <summary>
    /// This character's own kill record, kept because Valheim's does not say enough.
    ///
    /// The game files a kill under the species name and nothing else. Not whether the wolf was wild
    /// or somebody's pet -- Character.OnDeath credits the kill with no IsTamed check anywhere -- and
    /// not how many stars it had. Both are properties of the animal rather than the species, so no
    /// amount of reading the old tallies recovers them.
    ///
    /// What this does is record them from now on, and keep a count of kills it has *seen*, so the
    /// part of the history it was not there for can be named exactly rather than guessed at
    /// wholesale. The game says 300 boar; this says 42 of them, 11 tame; so 258 are unaccounted for
    /// and the config's assumption applies to those 258 and never to the 42. The guesswork shrinks
    /// every time you play. See <see cref="KillSplit"/> for that arithmetic.
    /// </summary>
    internal static class KillLog
    {
        /// <summary>
        /// Bumped when the file's shape changes. A newer file is readable as long as it only ever
        /// appended, the same contract the stats message keeps.
        /// </summary>
        private const int FileSchema = 1;

        private const float SaveDebounce = 5f;
        private const float SaveRetry = 30f;

        private static readonly Dictionary<string, KillTally> _tallies = new Dictionary<string, KillTally>(StringComparer.Ordinal);
        private static string _path;
        private static bool _loaded;
        private static bool _dirty;
        private static float _saveAt;
        private static float _nextLoadTry;
        private static long _profileId;

        internal static bool Loaded => _loaded;
        internal static string Path => _path;
        internal static int Species => _tallies.Count;

        /// <summary>This character's tally for a species, or null if it has never killed one.</summary>
        internal static KillTally For(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _tallies.TryGetValue(key, out var tally) ? tally : null;
        }

        internal static Dictionary<string, KillTally> All() => _tallies;

        // ── lifetime ────────────────────────────────────────────────────────────────

        internal static void Load()
        {
            if (DwamsConfig.RecordKills == null || !DwamsConfig.RecordKills.Value) return;
            var game = Game.instance;
            var profile = game != null ? game.GetPlayerProfile() : null;
            if (profile == null) return;
            if (ZNet.instance != null && ZNet.instance.IsDedicated()) return;

            long id = profile.GetPlayerID();
            string name = profile.GetName();
            if (string.IsNullOrEmpty(name)) return;
            if (_loaded && id == _profileId) return;

            Unload();
            _profileId = id;
            _path = System.IO.Path.Combine(Paths.ConfigPath, "DudeWhatAreMyStats",
                Sanitize(name) + "_" + id + ".kills.bin");
            _loaded = true;
            try
            {
                if (!File.Exists(_path)) return;
                FromBytes(File.ReadAllBytes(_path));
                DudeWhatAreMyStatsMod.Log.LogInfo(
                    $"[DudeWhatAreMyStats] Loaded watched kills for {_tallies.Count} species for {name}.");
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogError($"[DudeWhatAreMyStats] Could not read {_path}: {e}");
                Preserve("corrupt");
                _tallies.Clear();
            }
        }

        internal static void Unload()
        {
            SaveIfDirty();
            _tallies.Clear();
            _path = null;
            _loaded = false;
            _dirty = false;
            _profileId = 0L;
        }

        internal static void Update()
        {
            if (!_loaded)
            {
                float now = Time.unscaledTime;
                if (now < _nextLoadTry) return;
                _nextLoadTry = now + 1f;
                Load();
                return;
            }
            if (_dirty && Time.unscaledTime >= _saveAt) Save();
        }

        internal static void SaveIfDirty()
        {
            if (_loaded && _dirty) Save();
        }

        // ── recording ───────────────────────────────────────────────────────────────

        /// <summary>One kill, with the two things the game throws away.</summary>
        internal static void Record(string key, bool tame, int level)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!_loaded) Load();
            if (!_loaded) return;

            if (!_tallies.TryGetValue(key, out var tally))
            {
                tally = new KillTally();
                _tallies[key] = tally;
            }
            tally.Seen += 1f;
            if (tame) tally.Tame += 1f;
            if (level > 1) tally.Starred += 1f;
            if (level > tally.BestLevel) tally.BestLevel = level;
            MarkDirty();
        }

        // ── the file ────────────────────────────────────────────────────────────────

        private static void MarkDirty()
        {
            _dirty = true;
            _saveAt = Time.unscaledTime + SaveDebounce;
        }

        internal static void Save()
        {
            if (!_loaded || _path == null) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
                string tmp = _path + ".tmp";
                File.WriteAllBytes(tmp, ToBytes());
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
                _dirty = false;
            }
            catch (Exception e)
            {
                // Back off rather than retrying next frame, as the other two stores do.
                _saveAt = Time.unscaledTime + SaveRetry;
                DudeWhatAreMyStatsMod.Log.LogError(
                    $"[DudeWhatAreMyStats] Could not save {_path}; trying again in {SaveRetry:0}s: {e.Message}");
            }
        }

        private static byte[] ToBytes()
        {
            var pkg = new ZPackage();
            pkg.Write(FileSchema);
            pkg.Write(_tallies.Count);
            foreach (var kv in _tallies)
            {
                pkg.Write(kv.Key);
                pkg.Write(kv.Value.Seen);
                pkg.Write(kv.Value.Tame);
                pkg.Write(kv.Value.Starred);
                pkg.Write(kv.Value.BestLevel);
            }
            return pkg.GetArray();
        }

        private static void FromBytes(byte[] bytes)
        {
            _tallies.Clear();
            if (bytes == null || bytes.Length == 0) return;
            var pkg = new ZPackage(bytes);
            int schema = pkg.ReadInt();
            if (schema < 1)
            {
                Preserve("fileschema");
                DudeWhatAreMyStatsMod.Log.LogWarning(
                    $"[DudeWhatAreMyStats] The kill log is file version {schema} and this build reads 1 or later. " +
                    "The old file has been kept alongside; starting empty.");
                return;
            }
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                string key = pkg.ReadString();
                _tallies[key] = new KillTally
                {
                    Seen = pkg.ReadSingle(),
                    Tame = pkg.ReadSingle(),
                    Starred = pkg.ReadSingle(),
                    BestLevel = pkg.ReadInt(),
                };
            }
        }

        private static void Preserve(string why)
        {
            try { File.Copy(_path, _path + "." + why + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); }
            catch { /* best effort */ }
        }

        private static string Sanitize(string s)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "viking" : sb.ToString();
        }
    }
}

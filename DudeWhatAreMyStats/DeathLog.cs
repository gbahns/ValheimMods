using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// This character's own record of how it has died: running totals per cause, and the last
    /// handful of fights in full.
    ///
    /// Valheim has nowhere to keep any of this, so it lives beside the config in a file of its own,
    /// one per character. Two quite different things are kept here, and they are kept apart on
    /// purpose:
    ///
    ///  * The <b>totals</b> are small, bounded and interesting to other people, so they ride the
    ///    scoreboard message and the server store like the rest of the stats. That is what the
    ///    Nemesis column on the scoreboard is made of.
    ///  * The <b>reports</b> are per death and unbounded, and they never go near that message. The
    ///    whole store travels to a client in one routed RPC, and an oversized send does not merely
    ///    fail: Valheim's Steam socket leaves it at the head of that peer's queue and stops
    ///    draining, which stalls everything else going to that player. So reports stay local, and
    ///    you read your own.
    ///
    /// The totals begin at zero the day this mod is installed, which is why Recorded is kept
    /// alongside them: every death before that, and any the ledger could not attribute, shows up as
    /// the difference between the game's own death count and this one.
    /// </summary>
    internal static class DeathLog
    {
        /// <summary>Bumped when the file's shape changes. An older file is kept aside, not guessed at.</summary>
        private const int FileSchema = 1;

        private const float SaveDebounce = 3f;
        private const float SaveRetry = 30f;

        /// <summary>Cause key to how many times it has killed this character.</summary>
        private static readonly Dictionary<string, float> _killedBy = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>Creature key to how many of this character's deaths it was in the fight for.</summary>
        private static readonly Dictionary<string, float> _foughtWith = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>Newest first.</summary>
        private static readonly List<DeathReport> _reports = new List<DeathReport>();

        private static int _recorded;
        private static string _path;
        private static bool _loaded;
        private static bool _dirty;
        private static float _saveAt;
        private static long _profileId;
        private static float _nextLoadTry;

        internal static bool Loaded => _loaded;
        internal static string Path => _path;

        /// <summary>How many deaths this log has actually seen, as against the profile's own count.</summary>
        internal static int Recorded => _recorded;

        internal static int ReportCount => _reports.Count;

        // ── lifetime ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads this character's log. Safe to call repeatedly: it reloads only when the character
        /// has changed, which is what makes it usable as a retry from Update when the profile was
        /// not ready at Game.Start.
        /// </summary>
        internal static void Load()
        {
            if (DwamsConfig.RecordDeaths == null || !DwamsConfig.RecordDeaths.Value) return;
            var game = Game.instance;
            var profile = game != null ? game.GetPlayerProfile() : null;
            if (profile == null) return;
            // A dedicated server has no character of its own to keep a log for.
            if (ZNet.instance != null && ZNet.instance.IsDedicated()) return;

            long id = profile.GetPlayerID();
            string name = profile.GetName();
            if (string.IsNullOrEmpty(name)) return;
            if (_loaded && id == _profileId) return;

            Unload();
            _profileId = id;
            _path = System.IO.Path.Combine(Paths.ConfigPath, "DudeWhatAreMyStats",
                Sanitize(name) + "_" + id + ".deaths.bin");
            _loaded = true;
            try
            {
                if (!File.Exists(_path))
                {
                    DudeWhatAreMyStatsMod.Log.LogInfo(
                        $"[DudeWhatAreMyStats] No death log for {name} yet; recording from now on ({_path}).");
                    return;
                }
                FromBytes(File.ReadAllBytes(_path));
                DudeWhatAreMyStatsMod.Log.LogInfo(
                    $"[DudeWhatAreMyStats] Loaded {_recorded} recorded death(s) and {_reports.Count} report(s) for {name}.");
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogError($"[DudeWhatAreMyStats] Could not read {_path}: {e}");
                Preserve("corrupt");
                Clear();
            }
        }

        internal static void Unload()
        {
            SaveIfDirty();
            Clear();
            _path = null;
            _loaded = false;
            _profileId = 0L;
        }

        private static void Clear()
        {
            _killedBy.Clear();
            _foughtWith.Clear();
            _reports.Clear();
            _recorded = 0;
            _dirty = false;
        }

        /// <summary>Writes once things go quiet, and picks the character up if it arrived late.</summary>
        internal static void Update()
        {
            if (!_loaded)
            {
                // The profile is normally there at Game.Start; this is the retry for when it is not.
                // Once a second, because the alternative is a handful of null checks every frame for
                // as long as the character takes to arrive.
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

        // ── contents ────────────────────────────────────────────────────────────────

        /// <summary>Files a fresh death: one for the cause, one for everything that was in the fight.</summary>
        internal static void Record(DeathReport report)
        {
            if (report == null) return;
            if (!_loaded) Load();
            if (!_loaded) return;

            _recorded++;
            Bump(_killedBy, report.Cause);
            foreach (var p in report.Participants)
            {
                // Once per death, not once per blow: this counts the fights a creature was in, which
                // is what makes it readable against the number of deaths.
                if (p.Key != report.Cause) Bump(_foughtWith, p.Key);
            }

            _reports.Insert(0, report);
            int keep = DwamsConfig.KeepReports != null ? DwamsConfig.KeepReports.Value : 20;
            if (keep >= 0 && _reports.Count > keep) _reports.RemoveRange(keep, _reports.Count - keep);

            if (DwamsConfig.LogDeaths != null && DwamsConfig.LogDeaths.Value)
                foreach (string line in report.Lines(true))
                    DudeWhatAreMyStatsMod.Log.LogInfo("[DudeWhatAreMyStats] " + line);

            MarkDirty();
        }

        private static void Bump(Dictionary<string, float> counts, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            counts[key] = (counts.TryGetValue(key, out float have) ? have : 0f) + 1f;
        }

        /// <summary>What has killed this character, most often first. <paramref name="top"/> 0 means all.</summary>
        internal static List<KeyValuePair<string, float>> KilledBy(int top = 0) => Sorted(_killedBy, top);

        /// <summary>What has been in the fight when this character died, most often first.</summary>
        internal static List<KeyValuePair<string, float>> FoughtWith(int top = 0) => Sorted(_foughtWith, top);

        private static List<KeyValuePair<string, float>> Sorted(Dictionary<string, float> counts, int top)
        {
            var list = new List<KeyValuePair<string, float>>(counts);
            list.Sort((a, b) =>
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });
            if (top > 0 && list.Count > top) list.RemoveRange(top, list.Count - top);
            return list;
        }

        /// <summary>The reports, newest first. The list itself is the live one; read it, do not hold it.</summary>
        internal static List<DeathReport> Reports() => _reports;

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
                // Back off rather than retrying next frame: a read-only directory would otherwise
                // re-serialize everything and log a stack trace every frame for as long as it lasts.
                _saveAt = Time.unscaledTime + SaveRetry;
                DudeWhatAreMyStatsMod.Log.LogError(
                    $"[DudeWhatAreMyStats] Could not save {_path}; trying again in {SaveRetry:0}s: {e.Message}");
            }
        }

        private static byte[] ToBytes()
        {
            var pkg = new ZPackage();
            pkg.Write(FileSchema);
            pkg.Write(_recorded);

            pkg.Write(_killedBy.Count);
            foreach (var kv in _killedBy)
            {
                pkg.Write(kv.Key);
                pkg.Write(kv.Value);
            }
            pkg.Write(_foughtWith.Count);
            foreach (var kv in _foughtWith)
            {
                pkg.Write(kv.Key);
                pkg.Write(kv.Value);
            }

            // Each report goes in as its own blob, so one that a later build writes differently can
            // be skipped without losing the rest of them.
            pkg.Write(_reports.Count);
            foreach (var r in _reports)
            {
                var one = new ZPackage();
                r.Pack(one);
                pkg.Write(one.GetArray());
            }
            return pkg.GetArray();
        }

        private static void FromBytes(byte[] bytes)
        {
            Clear();
            if (bytes == null || bytes.Length == 0) return;
            var pkg = new ZPackage(bytes);
            int schema = pkg.ReadInt();
            if (schema != FileSchema)
            {
                Preserve("fileschema");
                DudeWhatAreMyStatsMod.Log.LogWarning(
                    $"[DudeWhatAreMyStats] The death log is file version {schema} and this build reads {FileSchema}. " +
                    "The old file has been kept alongside; starting empty.");
                return;
            }
            _recorded = pkg.ReadInt();

            int killedBy = pkg.ReadInt();
            for (int i = 0; i < killedBy; i++)
            {
                string key = pkg.ReadString();
                _killedBy[key] = pkg.ReadSingle();
            }
            int foughtWith = pkg.ReadInt();
            for (int i = 0; i < foughtWith; i++)
            {
                string key = pkg.ReadString();
                _foughtWith[key] = pkg.ReadSingle();
            }

            int reports = pkg.ReadInt();
            for (int i = 0; i < reports; i++)
            {
                byte[] blob = pkg.ReadByteArray();
                try { _reports.Add(DeathReport.Unpack(new ZPackage(blob))); }
                catch (Exception e)
                {
                    // The totals are the part that matters; one unreadable report is not worth
                    // throwing the file away for.
                    DudeWhatAreMyStatsMod.Log.LogWarning(
                        $"[DudeWhatAreMyStats] Skipped an unreadable death report: {e.Message}");
                }
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

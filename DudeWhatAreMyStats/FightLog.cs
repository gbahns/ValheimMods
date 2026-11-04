using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// This character's own fight reports: who did what to the things worth remembering.
    ///
    /// Reports arrive from whichever game owned the creature, which is the only one that saw the
    /// whole fight. Boss fights are kept as long as there is room for them, because comparing
    /// Bonemass to Yagluth years later is the whole point; everything else rolls over.
    /// </summary>
    internal static class FightLog
    {
        private const int FileSchema = 1;
        private const float SaveDebounce = 5f;
        private const float SaveRetry = 30f;
        /// <summary>Bosses do not roll over, but they do not grow without bound either.</summary>
        private const int KeepBosses = 60;

        private static readonly List<FightReport> _bosses = new List<FightReport>();
        private static readonly List<FightReport> _recent = new List<FightReport>();
        private static string _path;
        private static bool _loaded;
        private static bool _dirty;
        private static float _saveAt;
        private static float _nextLoadTry;
        private static long _profileId;
        private static string _me = "";

        // One death can reach OnDeath more than once in odd circumstances. Two identical reports
        // seconds apart is that, not two fights.
        private static string _lastKey = "";
        private static float _lastAt = -999f;

        internal static bool Loaded => _loaded;
        internal static string Path => _path;
        internal static int BossCount => _bosses.Count;
        internal static int RecentCount => _recent.Count;
        internal static string Me => _me;

        internal static List<FightReport> Bosses() => _bosses;
        internal static List<FightReport> Recent() => _recent;

        /// <summary>Boss fights this character took part in, what they dealt, and their mean share.</summary>
        internal static void BossSummary(out float fights, out float damage, out float share)
        {
            fights = 0f;
            damage = 0f;
            share = 0f;
            if (string.IsNullOrEmpty(_me)) return;
            float shareSum = 0f;
            foreach (var r in _bosses)
            {
                float mine = r.DamageFor(_me);
                if (mine <= 0f) continue;
                fights += 1f;
                damage += mine;
                shareSum += r.ShareFor(_me);
            }
            if (fights > 0f) share = shareSum / fights;
        }

        // ── lifetime ────────────────────────────────────────────────────────────────

        internal static void Load()
        {
            if (DwamsConfig.RecordFights == null || !DwamsConfig.RecordFights.Value) return;
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
            _me = name;
            _path = System.IO.Path.Combine(Paths.ConfigPath, "DudeWhatAreMyStats",
                Sanitize(name) + "_" + id + ".fights.bin");
            _loaded = true;
            try
            {
                if (!File.Exists(_path)) return;
                FromBytes(File.ReadAllBytes(_path));
                DudeWhatAreMyStatsMod.Log.LogInfo(
                    $"[DudeWhatAreMyStats] Loaded {_bosses.Count} boss fight(s) and {_recent.Count} other fight(s) for {name}.");
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogError($"[DudeWhatAreMyStats] Could not read {_path}: {e}");
                Preserve("corrupt");
                _bosses.Clear();
                _recent.Clear();
            }
        }

        internal static void Unload()
        {
            SaveIfDirty();
            _bosses.Clear();
            _recent.Clear();
            _path = null;
            _loaded = false;
            _dirty = false;
            _profileId = 0L;
            _me = "";
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

        internal static void Record(FightReport report, bool announce)
        {
            if (report == null) return;
            if (!_loaded) Load();
            if (!_loaded) return;

            string key = report.CreatureKey + "|" + report.TotalDamage.ToString("0.0");
            float now = Time.unscaledTime;
            if (key == _lastKey && now - _lastAt < 2f) return;
            _lastKey = key;
            _lastAt = now;

            if (report.Boss)
            {
                _bosses.Insert(0, report);
                if (_bosses.Count > KeepBosses) _bosses.RemoveRange(KeepBosses, _bosses.Count - KeepBosses);
            }
            else
            {
                _recent.Insert(0, report);
                int keep = DwamsConfig.KeepFights != null ? DwamsConfig.KeepFights.Value : 25;
                if (keep >= 0 && _recent.Count > keep) _recent.RemoveRange(keep, _recent.Count - keep);
            }
            MarkDirty();

            if (announce && DwamsConfig.AnnounceFights != null && DwamsConfig.AnnounceFights.Value)
                DudeWhatAreMyStatsMod.Message(report.Headline(_me));
            if (DwamsConfig.LogFights != null && DwamsConfig.LogFights.Value)
                foreach (string line in report.Lines())
                    DudeWhatAreMyStatsMod.Log.LogInfo("[DudeWhatAreMyStats] " + line);
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
                _saveAt = Time.unscaledTime + SaveRetry;
                DudeWhatAreMyStatsMod.Log.LogError(
                    $"[DudeWhatAreMyStats] Could not save {_path}; trying again in {SaveRetry:0}s: {e.Message}");
            }
        }

        private static byte[] ToBytes()
        {
            var pkg = new ZPackage();
            pkg.Write(FileSchema);
            WriteList(pkg, _bosses);
            WriteList(pkg, _recent);
            return pkg.GetArray();
        }

        private static void WriteList(ZPackage pkg, List<FightReport> list)
        {
            pkg.Write(list.Count);
            // Each report is length-framed as its own blob, so a report shape that gains fields
            // later cannot throw the rest of the file out of step.
            foreach (var r in list) pkg.Write(r.Pack().GetArray());
        }

        private static void FromBytes(byte[] bytes)
        {
            _bosses.Clear();
            _recent.Clear();
            if (bytes == null || bytes.Length == 0) return;
            var pkg = new ZPackage(bytes);
            int schema = pkg.ReadInt();
            if (schema < 1)
            {
                Preserve("fileschema");
                DudeWhatAreMyStatsMod.Log.LogWarning(
                    $"[DudeWhatAreMyStats] The fight log is file version {schema} and this build reads 1 or later. " +
                    "The old file has been kept alongside; starting empty.");
                return;
            }
            ReadList(pkg, _bosses);
            if (pkg.GetPos() < pkg.Size()) ReadList(pkg, _recent);
        }

        private static void ReadList(ZPackage pkg, List<FightReport> into)
        {
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                var blob = pkg.ReadByteArray();
                try
                {
                    var report = FightReport.Unpack(new ZPackage(blob));
                    if (report != null) into.Add(report);
                }
                catch { /* one unreadable report is not worth the rest of the file */ }
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

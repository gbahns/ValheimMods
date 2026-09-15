using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// The server's memory of who has what, so the scoreboard can show a player who is not online.
    ///
    /// Clients push their own snapshot now and then; the server keeps the newest one per character
    /// and writes it beside its config, one file per world. Nothing is computed here and nothing is
    /// trusted beyond what a client says about itself, which is the same trust the live answers
    /// already carry. Everything in this class is inert on a client: a listen server is both, and
    /// there the store simply runs on the host's machine.
    ///
    /// The store is an addition, not a requirement. A server without this mod never answers the
    /// request for it, and the panel falls back to showing only the players online to answer.
    /// </summary>
    internal static class StatsStore
    {
        /// <summary>Bumped when the file's shape changes. An older file is discarded, not guessed at.</summary>
        private const int FileSchema = 1;

        private static readonly Dictionary<long, Snapshot> _store = new Dictionary<long, Snapshot>();
        /// <summary>Quiet time after a change before writing, so a burst of pushes costs one write.</summary>
        private const float SaveDebounce = 5f;

        /// <summary>However busy it gets, never go longer than this between writes.</summary>
        private const float SaveDeadline = 30f;

        /// <summary>How long to wait after a failed write before trying again.</summary>
        private const float SaveRetry = 30f;

        private static string _path;
        private static bool _loaded;
        private static bool _dirty;
        private static float _saveAt;
        private static float _saveBy;

        internal static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        internal static bool Loaded => _loaded;

        internal static int Count => _store.Count;

        internal static string Path => _path;

        // ── lifetime ────────────────────────────────────────────────────────────────

        /// <summary>Reads the world's store. Call once the world is up; does nothing on a client.</summary>
        internal static void Load()
        {
            Unload();
            if (!IsServer) return;
            if (!DwamsConfig.ServerStoreEnabled.Value)
            {
                DudeWhatAreMyStatsMod.Log.LogInfo("[DudeWhatAreMyStats] Server store disabled in config; offline players will not be listed.");
                return;
            }

            _path = BuildPath();
            if (_path == null) return;
            _loaded = true;
            try
            {
                if (File.Exists(_path))
                {
                    FromBytes(File.ReadAllBytes(_path));
                    int pruned = Prune();
                    DudeWhatAreMyStatsMod.Log.LogInfo(
                        $"[DudeWhatAreMyStats] Loaded stats for {_store.Count} character(s)" +
                        (pruned > 0 ? $", dropped {pruned} past the keep window" : "") + $", from {_path}");
                    if (pruned > 0) MarkDirty();
                }
                else
                {
                    DudeWhatAreMyStatsMod.Log.LogInfo($"[DudeWhatAreMyStats] No stats store yet ({_path}); starting empty.");
                }
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogError($"[DudeWhatAreMyStats] Could not read {_path}: {e}");
                // Keep the unreadable file rather than overwriting it, so it can be looked at.
                try { File.Copy(_path, _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { /* best effort */ }
                _store.Clear();
            }
        }

        internal static void Unload()
        {
            SaveIfDirty();
            _store.Clear();
            _path = null;
            _loaded = false;
            _dirty = false;
        }

        /// <summary>
        /// Writes the file once things go quiet, or once the deadline passes, whichever comes first.
        ///
        /// The deadline is the point: the quiet period restarts on every change, so a world where
        /// somebody keeps pushing would otherwise never go quiet and never write at all. This host's
        /// stop is a hard kill with no shutdown save, so "never wrote" means "lost everything".
        /// </summary>
        internal static void Update()
        {
            if (!_loaded || !_dirty) return;
            float now = Time.unscaledTime;
            if (now >= _saveAt || now >= _saveBy) Save();
        }

        internal static void SaveIfDirty()
        {
            if (_loaded && _dirty) Save();
        }

        private static void MarkDirty()
        {
            float now = Time.unscaledTime;
            if (!_dirty) _saveBy = now + SaveDeadline;   // the deadline is set by the first change, not the last
            _dirty = true;
            _saveAt = now + SaveDebounce;
        }

        // ── contents ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Records what a player says about themselves. The arrival time is stamped here by the
        /// server rather than taken from the sender, so the ages are measured from one clock.
        /// </summary>
        internal static void Put(Snapshot snap)
        {
            if (!_loaded || snap == null) return;
            // Only a real character id is worth keeping. Snapshot.Identity falls back to the
            // connection id, which is regenerated every session and is not written to the file, so
            // storing one would add a fresh row per login and collapse to a single shared row on
            // reload. Valheim always assigns a profile id, so this should never actually fire.
            if (snap.ProfileId == 0L) return;
            snap.LastSeenUtc = Snapshot.NowUtc();
            snap.FromStore = true;
            snap.Online = false;
            _store[snap.Identity] = snap;
            Prune();
            Cap();
            MarkDirty();
        }

        /// <summary>Forgets one character, for a player who has opted out of being on the board.</summary>
        internal static bool Forget(long identity)
        {
            if (!_loaded || !_store.Remove(identity)) return false;
            MarkDirty();
            return true;
        }

        /// <summary>
        /// Everything to send to a client that asked, most recently seen first.
        ///
        /// The whole store travels in one routed RPC, and an oversized message is worse than a
        /// dropped one: Valheim's Steam socket leaves a send it could not make at the head of that
        /// peer's queue and stops draining it, which stalls everything else going to that player.
        /// The store is capped so the message stays well inside what a send can carry.
        /// </summary>
        internal static List<Snapshot> All()
        {
            var list = new List<Snapshot>(_store.Values);
            list.Sort((a, b) => b.LastSeenUtc.CompareTo(a.LastSeenUtc));
            return list;
        }

        /// <summary>Drops the least recently seen characters once the store is over its limit.</summary>
        private static void Cap()
        {
            int max = DwamsConfig.ServerMaxCharacters != null ? DwamsConfig.ServerMaxCharacters.Value : 100;
            if (max <= 0 || _store.Count <= max) return;
            var list = All();                       // newest first
            for (int i = max; i < list.Count; i++)
                _store.Remove(list[i].Identity);
        }

        /// <summary>Drops characters not heard from inside the keep window. Returns how many went.</summary>
        private static int Prune()
        {
            float days = DwamsConfig.ServerKeepDays != null ? DwamsConfig.ServerKeepDays.Value : 30f;
            if (days <= 0f) return 0;   // keep forever
            long cutoff = Snapshot.NowUtc() - (long)(days * 86400d);
            List<long> drop = null;
            foreach (var kv in _store)
            {
                if (kv.Value == null || kv.Value.LastSeenUtc <= 0L || kv.Value.LastSeenUtc >= cutoff) continue;
                (drop ?? (drop = new List<long>())).Add(kv.Key);
            }
            if (drop == null) return 0;
            foreach (long id in drop) _store.Remove(id);
            return drop.Count;
        }

        // ── the file ────────────────────────────────────────────────────────────────

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
                // Back off rather than retrying next frame: a read-only config directory or a file
                // held open elsewhere would otherwise re-serialize the whole store and log a stack
                // trace on every frame, for as long as the condition lasts.
                float now = Time.unscaledTime;
                _saveAt = now + SaveRetry;
                _saveBy = now + SaveRetry;
                DudeWhatAreMyStatsMod.Log.LogError(
                    $"[DudeWhatAreMyStats] Could not save {_path}; trying again in {SaveRetry:0}s: {e.Message}");
            }
        }

        private static byte[] ToBytes()
        {
            var pkg = new ZPackage();
            pkg.Write(FileSchema);
            // The rows are Snapshot blobs, so the file is only readable by a build that speaks that
            // version too. Recording it here is what lets Load tell "different row format" apart
            // from "corrupt", instead of reading a valid-looking count and then dropping every row.
            pkg.Write(Snapshot.Schema);
            pkg.Write(_store.Count);
            foreach (var snap in _store.Values)
                pkg.Write(snap.Pack().GetArray());
            return pkg.GetArray();
        }

        private static void FromBytes(byte[] bytes)
        {
            _store.Clear();
            if (bytes == null || bytes.Length == 0) return;
            var pkg = new ZPackage(bytes);
            int schema = pkg.ReadInt();
            if (schema != FileSchema)
            {
                Preserve("fileschema");
                DudeWhatAreMyStatsMod.Log.LogWarning(
                    $"[DudeWhatAreMyStats] The stats store is file version {schema} and this build reads {FileSchema}. " +
                    "The old file has been kept alongside; starting empty.");
                return;
            }
            int rowSchema = pkg.ReadInt();
            if (rowSchema != Snapshot.Schema)
            {
                Preserve("rowschema");
                DudeWhatAreMyStatsMod.Log.LogWarning(
                    $"[DudeWhatAreMyStats] The stats store holds version {rowSchema} records and this build reads " +
                    $"{Snapshot.Schema}, so none of them can be read. The old file has been kept alongside; starting " +
                    "empty. Everyone is recorded again the next time they play.");
                return;
            }
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                var snap = Snapshot.Unpack(new ZPackage(pkg.ReadByteArray()));
                // A snapshot written by a build with a different wire format comes back null. The
                // character is simply forgotten; they are recorded again the next time they play.
                if (snap == null) continue;
                snap.FromStore = true;
                snap.Online = false;
                _store[snap.Identity] = snap;
            }
        }

        /// <summary>Keeps a copy of a file we are about to stop using, so nothing is lost silently.</summary>
        private static void Preserve(string why)
        {
            try { File.Copy(_path, _path + "." + why + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); }
            catch { /* best effort */ }
        }

        private static string BuildPath()
        {
            var world = ZNet.World;
            if (world == null) return null;
            return System.IO.Path.Combine(Paths.ConfigPath, "DudeWhatAreMyStats",
                Sanitize(world.m_name) + "_" + world.m_seed + ".stats.bin");
        }

        private static string Sanitize(string s)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "world" : sb.ToString();
        }
    }
}

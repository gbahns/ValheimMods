using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// The shared map on the server: a MapStore persisted to
    /// BepInEx/config/TheGreatestMap/&lt;world&gt;_&lt;seed&gt;.pins.bin. Clients merge their personal
    /// maps into it at cartography tables (or continuously in Instant mode) and take the result
    /// back. Lives on whichever process is the server: a dedicated server, or the hosting
    /// player in a non-dedicated game.
    /// </summary>
    internal static class PinStore
    {
        private static MapStore _store = new MapStore();

        /// <summary>What has changed since each client last heard, so a sync can send the
        /// changes rather than the map. In memory only; see <see cref="Watermark"/>.</summary>
        private static Watermark _marks = new Watermark();
        private static bool _loaded, _dirty;
        private static float _saveAt;
        private static string _path;

        internal static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();
        internal static bool Loaded => _loaded;
        internal static int Count => _store.Pins.Count;
        internal static int SuppressionCount => _store.Suppressions.Count;
        internal static int TombstoneCount => _store.Tombstones.Count;

        internal static void Load()
        {
            _store = new MapStore();
            // A new map means a new numbering. Loading a different world without unloading the
            // last one would otherwise leave clients holding watermarks that count changes to a
            // map nobody has any more.
            _marks = new Watermark();
            _dirty = false;
            _path = BuildPath();
            _loaded = true;
            if (_path == null)
            {
                TheGreatestMapMod.Log.LogWarning("[TheGreatestMap] No world information; the shared map will not be persisted this session.");
                return;
            }
            try
            {
                if (File.Exists(_path))
                {
                    var stored = File.ReadAllBytes(_path);
                    _store = MapStore.FromBytes(stored);
                    TheGreatestMapMod.Log.LogInfo(
                        $"[TheGreatestMap] Loaded the shared map: {_store.Pins.Count} markers, {_store.Tombstones.Count} erased" +
                        (_store.LegacyTombstones.Count > 0 ? $" (and {_store.LegacyTombstones.Count} erased before the rekey)" : "") +
                        $", {_store.Suppressions.Count} erased spots, from {_path}");
                    int crops = _store.ForgetCropErasures();
                    if (crops > 0)
                    {
                        MarkDirty();
                        TheGreatestMapMod.Log.LogInfo(
                            $"[TheGreatestMap] Forgot {crops} erasure(s) left by clearing crops off tilled ground. " +
                            "A planted turnip was never a discovery, so there is nothing to remember about rubbing one out.");
                    }
                    // On every load, not only while converting. It was narrowed to the
                    // conversion because it was eating the erasure that folding two markers
                    // into one depends on -- the two look identical, and only age tells them
                    // apart, which ForgetGhosts now weighs for itself. Narrowing it meant every
                    // ghost that arrived afterwards stayed: 197 of them on the live map.
                    int ghosts = _store.ForgetGhosts();
                    if (ghosts > 0)
                    {
                        MarkDirty();
                        TheGreatestMapMod.Log.LogInfo(
                            $"[TheGreatestMap] Dropped {ghosts} erasure(s) that were only the former name of a marker " +
                            "still standing in the same place. They kept nothing erased and cost more than the markers did.");
                    }
                    if (_store.ReadVersion < MapStore.FormatVersion)
                    {
                        MapBackup.Keep(Path.GetFileNameWithoutExtension(_path), _store.ReadVersion, stored);
                        // Converted as it was read. Written back at once so the next load is not
                        // another conversion, and so the smaller format is what actually goes out.
                        TheGreatestMapMod.Log.LogInfo(
                            $"[TheGreatestMap] The shared map was in format {_store.ReadVersion} and has been brought up to " +
                            $"{MapStore.FormatVersion}: {_store.Pins.Count} markers keyed by what they are, " +
                            $"{_store.LegacyTombstones.Count} erasure(s) carried over as they were. Saving it in the new format." +
                            (_store.Collapsed > 0
                                ? $" {_store.Collapsed} marker(s) turned out to be a second record of one already here -- the same " +
                                  "spot written down twice before markers said what they stand for -- and are now one marker each."
                                : ""));
                        MarkDirty();
                    }
                }
                else
                {
                    TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] No shared map file yet ({_path}); starting empty.");
                }
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogError($"[TheGreatestMap] Could not read {_path}: {e}");
                try { File.Copy(_path, _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { /* best effort */ }
                _store = new MapStore();
            }
        }

        internal static void Unload()
        {
            SaveIfDirty();
            _loaded = false;
            _store = new MapStore();
            // A fresh session: every client's watermark stops being recognised and each is sent
            // one whole map the next time it syncs, which is the safe answer to "I no longer
            // know what you have".
            _marks = new Watermark();
        }

        internal static void Update()
        {
            if (_loaded && _dirty && Time.unscaledTime >= _saveAt) Save();
        }

        internal static void SaveIfDirty()
        {
            if (_loaded && _dirty) Save();
        }

        private static void MarkDirty()
        {
            _dirty = true;
            _saveAt = Time.unscaledTime + 3f;
        }

        internal static void Save()
        {
            if (!_loaded || _path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string tmp = _path + ".tmp";
                File.WriteAllBytes(tmp, _store.ToBytes());
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
                _dirty = false;
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogError($"[TheGreatestMap] Could not save {_path}: {e}");
            }
        }

        /// <summary>Merge a client's map in; the result lists what was new to the shared map.</summary>
        internal static MergeResult Merge(MapStore incoming)
        {
            var result = _store.Merge(incoming);
            if (result.Any) MarkDirty();
            _marks.Record(result);
            return result;
        }

        /// <summary>The change numbering, for answering a sync with only what is new.</summary>
        internal static Watermark Marks => _marks;

        internal static byte[] Snapshot() => _store.ToBytes();

        /// <summary>The shared map itself, for sending compressed.</summary>
        internal static MapStore Store => _store;

        /// <summary>Erase markers for everyone: they become tombstones so no client can bring them back.</summary>
        internal static MapStore Wipe(bool autoOnly)
        {
            var delta = new MapStore();
            long now = MapStore.Now;
            var ids = new List<PinKey>();
            foreach (var kv in _store.Pins) if (!autoOnly || kv.Value.Auto) ids.Add(kv.Key);
            foreach (var id in ids)
            {
                _store.Pins.Remove(id);
                _store.Tombstones[id] = now;
                delta.Tombstones[id] = now;
            }
            _store.Suppressions.Clear();
            MarkDirty();
            // A wipe changes the map without going through Merge, so the numbering has to be
            // told about it here or a client holding a watermark would never be sent the
            // erasures and would keep showing markers an admin has removed for everyone.
            var wiped = new MergeResult();
            foreach (var kv in delta.Tombstones) wiped.NewTombstones.Add(new Tombstone { Key = kv.Key, DeletedAt = kv.Value });
            wiped.Deleted = delta.Tombstones.Count;
            _marks.Record(wiped);
            return delta;
        }

        internal static void ClearSuppressions()
        {
            _store.Suppressions.Clear();
            MarkDirty();
        }

        private static string BuildPath()
        {
            var world = ZNet.World;
            if (world == null) return null;
            return Path.Combine(Paths.ConfigPath, "TheGreatestMap", Sanitize(world.m_name) + "_" + world.m_seed + ".pins.bin");
        }

        private static string Sanitize(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "world" : sb.ToString();
        }
    }
}

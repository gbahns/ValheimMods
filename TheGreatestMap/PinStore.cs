using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Server-side authority for shared markers. Lives on whichever process is the server
    /// (dedicated server, or the hosting player in a non-dedicated game) and is persisted to
    /// BepInEx/config/TheGreatestMap/&lt;world&gt;_&lt;seed&gt;.pins.bin. Clients never persist shared
    /// markers themselves, so a marker erased here cannot be re-uploaded from a stale copy.
    /// </summary>
    internal static class PinStore
    {
        // 1: original. 2: icon keys on markers and suppressions.
        internal const int FileVersion = 2;

        private static readonly Dictionary<string, SharedPin> _pins = new Dictionary<string, SharedPin>();
        private static readonly List<Suppression> _suppressions = new List<Suppression>();
        private static bool _loaded, _dirty;
        private static float _saveAt;
        private static string _path;

        internal static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();
        internal static bool Loaded => _loaded;
        internal static int Count => _pins.Count;
        internal static int SuppressionCount => _suppressions.Count;

        internal static void Load()
        {
            _pins.Clear();
            _suppressions.Clear();
            _dirty = false;
            _path = BuildPath();
            _loaded = true;
            if (_path == null)
            {
                TheGreatestMapMod.Log.LogWarning("[TheGreatestMap] No world information; shared markers will not be persisted this session.");
                return;
            }
            try
            {
                if (File.Exists(_path))
                {
                    var pkg = new ZPackage(File.ReadAllBytes(_path));
                    ReadAll(pkg, _pins, _suppressions);
                    TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Loaded {_pins.Count} shared markers and {_suppressions.Count} erased spots from {_path}");
                }
                else
                {
                    TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] No shared marker file yet ({_path}); starting empty.");
                }
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogError($"[TheGreatestMap] Could not read {_path}: {e}");
                try { File.Copy(_path, _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { /* best effort */ }
                _pins.Clear();
                _suppressions.Clear();
            }
        }

        internal static void Unload()
        {
            SaveIfDirty();
            _loaded = false;
            _pins.Clear();
            _suppressions.Clear();
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
                var pkg = new ZPackage();
                WriteAll(pkg, _pins.Values, _suppressions);
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string tmp = _path + ".tmp";
                File.WriteAllBytes(tmp, pkg.GetArray());
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
                _dirty = false;
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogError($"[TheGreatestMap] Could not save {_path}: {e}");
            }
        }

        internal static void WriteAll(ZPackage pkg, IEnumerable<SharedPin> pins, List<Suppression> suppressions)
        {
            var list = new List<SharedPin>(pins);
            pkg.Write(FileVersion);
            pkg.Write(list.Count);
            foreach (var p in list) p.Write(pkg);
            pkg.Write(suppressions.Count);
            foreach (var s in suppressions) s.Write(pkg);
        }

        internal static void ReadAll(ZPackage pkg, Dictionary<string, SharedPin> pins, List<Suppression> suppressions)
        {
            int version = pkg.ReadInt();
            if (version < 1 || version > FileVersion) throw new InvalidDataException("Unsupported shared marker format " + version);
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++)
            {
                var p = SharedPin.Read(pkg, version);
                if (!string.IsNullOrEmpty(p.Id)) pins[p.Id] = p;
            }
            int m = pkg.ReadInt();
            for (int i = 0; i < m; i++) suppressions.Add(Suppression.Read(pkg, version));
        }

        internal static ZPackage BuildFullSync()
        {
            var pkg = new ZPackage();
            WriteAll(pkg, _pins.Values, _suppressions);
            return pkg;
        }

        internal static bool Add(SharedPin pin)
        {
            if (pin == null || string.IsNullOrEmpty(pin.Id)) return false;
            _pins[pin.Id] = pin;
            MarkDirty();
            return true;
        }

        internal static bool Remove(string id, out Suppression suppression)
        {
            suppression = null;
            if (string.IsNullOrEmpty(id) || !_pins.TryGetValue(id, out var pin)) return false;
            _pins.Remove(id);
            if (pin.Auto)
            {
                suppression = new Suppression { Type = pin.Type, Icon = pin.Icon, Pos = pin.Pos, Name = pin.Name };
                _suppressions.Add(suppression);
            }
            MarkDirty();
            return true;
        }

        internal static bool Update(string id, string name, bool isChecked)
        {
            if (string.IsNullOrEmpty(id) || !_pins.TryGetValue(id, out var pin)) return false;
            pin.Name = name ?? "";
            pin.Checked = isChecked;
            MarkDirty();
            return true;
        }

        internal static int Wipe(bool autoOnly)
        {
            int removed;
            if (autoOnly)
            {
                var ids = new List<string>();
                foreach (var kv in _pins) if (kv.Value.Auto) ids.Add(kv.Key);
                foreach (var id in ids) _pins.Remove(id);
                removed = ids.Count;
            }
            else
            {
                removed = _pins.Count;
                _pins.Clear();
            }
            _suppressions.Clear();
            MarkDirty();
            return removed;
        }

        internal static void ClearSuppressions()
        {
            _suppressions.Clear();
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

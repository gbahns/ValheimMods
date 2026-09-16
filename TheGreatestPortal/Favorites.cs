using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace TheGreatestPortal
{
    /// <summary>
    /// The player's own favorites, default portal and most recently used portals, kept per world in
    /// BepInEx/config/TheGreatestPortal/&lt;world&gt;-&lt;seed&gt;.txt. Portals are referred to by
    /// their permanent ids, so the file survives server restarts. Nothing here is shared.
    /// </summary>
    internal static class Favorites
    {
        private static readonly HashSet<long> _favs = new HashSet<long>();
        private static readonly List<long> _recents = new List<long>();   // newest first
        private const int MaxRecents = 20;   // kept longer than any list shows, so raising the setting has history
        private static long _default;
        private static string _path;
        private static bool _loaded;

        internal static long DefaultId { get { Load(); return _default; } }
        internal static int Count { get { Load(); return _favs.Count; } }

        /// <summary>The portals traveled to or from, newest first.</summary>
        internal static IReadOnlyList<long> Recents { get { Load(); return _recents; } }

        /// <summary>
        /// Remembers a trip: both ends count as used, the destination most recently, so from
        /// wherever you arrive the portal you came from is right there to take you back.
        /// </summary>
        internal static void RecordTrip(long from, long to)
        {
            Load();
            bool changed = false;
            foreach (long id in new[] { from, to })
            {
                if (id == 0L) continue;
                _recents.Remove(id);
                _recents.Insert(0, id);
                changed = true;
            }
            if (!changed) return;
            if (_recents.Count > MaxRecents) _recents.RemoveRange(MaxRecents, _recents.Count - MaxRecents);
            Save();
        }

        internal static bool IsFavorite(long id)
        {
            Load();
            return id != 0L && _favs.Contains(id);
        }

        /// <summary>Flips a favorite and returns its new state.</summary>
        internal static bool Toggle(long id)
        {
            Load();
            if (id == 0L) return false;
            bool on = !_favs.Remove(id);
            if (on) _favs.Add(id);
            Save();
            return on;
        }

        internal static void SetFavorite(long id, bool on)
        {
            Load();
            if (id == 0L) return;
            bool changed = on ? _favs.Add(id) : _favs.Remove(id);
            if (changed) Save();
        }

        internal static void SetDefault(long id)
        {
            Load();
            if (_default == id) return;
            _default = id;
            Save();
        }

        internal static void Reset()
        {
            _favs.Clear();
            _recents.Clear();
            _default = 0L;
            _path = null;
            _loaded = false;
            PortalList.Reset();
        }

        // ── storage ─────────────────────────────────────────────────────────────────

        private static string PathFor()
        {
            var world = ZNet.World;
            if (world == null || string.IsNullOrEmpty(world.m_name)) return null;
            var sb = new StringBuilder();
            foreach (char c in world.m_name)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return Path.Combine(Paths.ConfigPath, "TheGreatestPortal", $"{sb}-{world.m_seed}.txt");
        }

        private static void Load()
        {
            if (_loaded) return;
            _path = PathFor();
            if (_path == null) return;      // world not known yet; try again on the next call
            _loaded = true;
            _favs.Clear();
            _recents.Clear();
            _default = 0L;
            try
            {
                if (!File.Exists(_path)) return;
                foreach (var raw in File.ReadAllLines(_path))
                {
                    var line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    if (!long.TryParse(line.Substring(eq + 1).Trim(), out long id) || id == 0L) continue;
                    if (key == "favorite") _favs.Add(id);
                    else if (key == "default") _default = id;
                    else if (key == "recent" && !_recents.Contains(id) && _recents.Count < MaxRecents) _recents.Add(id);
                }
            }
            catch (Exception e)
            {
                TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not read favorites from {_path}: {e.Message}");
            }
        }

        private static void Save()
        {
            if (_path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var sb = new StringBuilder();
                sb.AppendLine("# The Greatest Portal: this player's default portal, favorites and recent portals (newest first) for one world, by permanent portal id.");
                if (_default != 0L) sb.AppendLine("default=" + _default);
                foreach (var id in _favs) sb.AppendLine("favorite=" + id);
                foreach (var id in _recents) sb.AppendLine("recent=" + id);
                File.WriteAllText(_path, sb.ToString());
            }
            catch (Exception e)
            {
                TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not save favorites to {_path}: {e.Message}");
            }
        }
    }
}

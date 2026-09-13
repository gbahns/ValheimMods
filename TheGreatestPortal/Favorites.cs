using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace TheGreatestPortal
{
    /// <summary>
    /// The player's own favourites and default portal, kept per world in
    /// BepInEx/config/TheGreatestPortal/&lt;world&gt;-&lt;seed&gt;.txt. Portals are referred to by
    /// their permanent ids, so the file survives server restarts. Nothing here is shared.
    /// </summary>
    internal static class Favorites
    {
        private static readonly HashSet<long> _favs = new HashSet<long>();
        private static long _default;
        private static string _path;
        private static bool _loaded;

        internal static long DefaultId { get { Load(); return _default; } }
        internal static int Count { get { Load(); return _favs.Count; } }

        internal static bool IsFavorite(long id)
        {
            Load();
            return id != 0L && _favs.Contains(id);
        }

        /// <summary>Flips a favourite and returns its new state.</summary>
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
            _default = 0L;
            _path = null;
            _loaded = false;
        }

        /// <summary>Portals for a list: favourites first, then the rest, each alphabetical with unnamed last; <paramref name="exclude"/> left out.</summary>
        internal static List<PortalInfo> Sorted(long exclude, ZDOID excludeZdo)
        {
            Load();
            var favs = new List<PortalInfo>();
            var rest = new List<PortalInfo>();
            foreach (var p in Catalog.All)
            {
                if (p.Id == exclude && exclude != 0L) continue;
                if (excludeZdo != ZDOID.None && p.ZdoId == excludeZdo) continue;
                (_favs.Contains(p.Id) ? favs : rest).Add(p);
            }
            favs.Sort(ByName);
            rest.Sort(ByName);
            favs.AddRange(rest);
            return favs;
        }

        private static int ByName(PortalInfo a, PortalInfo b)
        {
            bool an = string.IsNullOrEmpty(a.Name), bn = string.IsNullOrEmpty(b.Name);
            if (an != bn) return an ? 1 : -1;
            int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
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
                }
            }
            catch (Exception e)
            {
                TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not read favourites from {_path}: {e.Message}");
            }
        }

        private static void Save()
        {
            if (_path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var sb = new StringBuilder();
                sb.AppendLine("# The Greatest Portal: this player's default portal and favourites for one world, by permanent portal id.");
                if (_default != 0L) sb.AppendLine("default=" + _default);
                foreach (var id in _favs) sb.AppendLine("favorite=" + id);
                File.WriteAllText(_path, sb.ToString());
            }
            catch (Exception e)
            {
                TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not save favourites to {_path}: {e.Message}");
            }
        }
    }
}

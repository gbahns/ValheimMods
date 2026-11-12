using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;

namespace TheGreatestPortal
{
    /// <summary>
    /// The player's own favorites, default portal, most recently used portals and categories, kept
    /// per world in BepInEx/config/TheGreatestPortal/&lt;world&gt;-&lt;seed&gt;.txt. Portals are
    /// referred to by their permanent ids, so the file survives server restarts. Nothing here is
    /// shared: one player's categories are their own, and nobody else's list changes.
    /// </summary>
    internal static class Favorites
    {
        private static readonly HashSet<long> _favs = new HashSet<long>();
        private static readonly List<long> _recents = new List<long>();   // newest first
        private const int MaxRecents = 50;   // kept longer than any list shows, so raising the setting has history
        private static long _default;

        /// <summary>
        /// Which category each portal is filed under, for the portals filed under one.
        ///
        /// A category is nothing but the name its portals carry: the list of them is whatever
        /// names are in here. So there is no category to create before a portal is put in one and
        /// none left over when the last portal leaves, which is a whole screen of management that
        /// nobody has to be given.
        /// </summary>
        private static readonly Dictionary<long, string> _categories = new Dictionary<long, string>();

        /// <summary>How long a category name may be. Shorter than a portal name: it is a heading
        /// over a list, not a label on a thing.</summary>
        internal const int MaxCategoryLength = 24;

        private static string _path;
        private static bool _loaded;

        internal static long DefaultId { get { Load(); return _default; } }
        internal static int Count { get { Load(); return _favs.Count; } }

        /// <summary>The portals traveled to or from, newest first.</summary>
        internal static IReadOnlyList<long> Recents { get { Load(); return _recents; } }

        /// <summary>The category a portal is filed under, or null for none.</summary>
        internal static string CategoryOf(long id)
        {
            Load();
            string name;
            return id != 0L && _categories.TryGetValue(id, out name) ? name : null;
        }

        /// <summary>Every category in use, in the order a list shows them.</summary>
        internal static List<string> CategoryNames()
        {
            Load();
            var names = new List<string>();
            foreach (var name in _categories.Values)
            {
                bool have = false;
                foreach (var seen in names)
                    if (string.Equals(seen, name, StringComparison.OrdinalIgnoreCase)) { have = true; break; }
                if (!have) names.Add(name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>Files a portal under a category, or takes it out of one with a null or empty
        /// name. A name already in use is matched however it was typed, so "farms" joins "Farms"
        /// rather than standing up a second heading beside it.</summary>
        internal static void SetCategory(long id, string name)
        {
            Load();
            if (id == 0L) return;
            name = CleanCategory(name);
            if (string.IsNullOrEmpty(name))
            {
                if (!_categories.Remove(id)) return;
                Save();
                return;
            }
            foreach (var seen in _categories.Values)
                if (string.Equals(seen, name, StringComparison.OrdinalIgnoreCase)) { name = seen; break; }
            string old;
            if (_categories.TryGetValue(id, out old) && old == name) return;
            _categories[id] = name;
            Save();
        }

        /// <summary>
        /// A typed category name made safe to keep.
        ///
        /// The characters taken out are the ones that would break something that holds the name
        /// later: a comma separates the collapsed-section list in the config file, and an equals
        /// sign, a pipe or a line break would split a line of this player's own file in two.
        /// </summary>
        internal static string CleanCategory(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var sb = new StringBuilder();
            foreach (char c in raw)
            {
                if (c == '=' || c == '|' || c == ',' || c == '\r' || c == '\n' || c == '\t') continue;
                sb.Append(c);
                if (sb.Length >= MaxCategoryLength) break;
            }
            string name = sb.ToString().Trim();
            return name.Length == 0 ? null : name;
        }

        /// <summary>
        /// Remembers a trip: both ends count as used, the destination most recently, so from
        /// wherever you arrive the portal you came from is right there to take you back.
        /// </summary>
        internal static void RecordTrip(long from, long to)
        {
            Load();
            PortalNetwork.SendUsed(from, to);   // everyone else's list learns of it too
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

        /// <summary>
        /// Whether this player has been through a portal, as far back as this file goes.
        ///
        /// The server's own log only knows the trips it has recorded, which began with the
        /// version that added it; this file has been kept all along. It holds no times, so it can
        /// say whether but never when -- which is enough for the thing that asks, the color of a
        /// row, and is why a trip it alone remembers is worded without one.
        /// </summary>
        internal static bool HasUsed(long id)
        {
            Load();
            return id != 0L && _recents.Contains(id);
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
            if (id == 0L)
            {
                TheGreatestPortalMod.Log.LogInfo("[TheGreatestPortal] No default portal any more; portals you build will be open.");
                return;
            }
            var p = Catalog.Get(id);
            TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] Default portal for portals you build is now '{(p != null ? p.DisplayName : "a portal that is not in the list")}' ({id}), saved in {_path}.");
        }

        internal static void Reset()
        {
            _favs.Clear();
            _recents.Clear();
            _categories.Clear();
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
            _categories.Clear();
            _default = 0L;
            try
            {
                if (!File.Exists(_path))
                {
                    TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] Nothing saved for this world yet; favorites and the default portal will go in {_path}.");
                    return;
                }
                foreach (var raw in File.ReadAllLines(_path))
                {
                    var line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    // A category line carries two fields rather than an id, so it is read before
                    // the check that every other line's value is an id.
                    if (key == "category") { ReadCategory(line.Substring(eq + 1)); continue; }
                    if (!long.TryParse(line.Substring(eq + 1).Trim(), out long id) || id == 0L) continue;
                    if (key == "favorite") _favs.Add(id);
                    else if (key == "default") _default = id;
                    else if (key == "recent" && !_recents.Contains(id) && _recents.Count < MaxRecents) _recents.Add(id);
                }
                TheGreatestPortalMod.Log.LogInfo($"[TheGreatestPortal] Read {_favs.Count} favorite(s), {_recents.Count} recent portal(s), {_categories.Count} categorized portal(s) and default portal {(_default == 0L ? "none" : _default.ToString())} from {_path}.");
            }
            catch (Exception e)
            {
                TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not read favorites from {_path}: {e.Message}");
            }
        }

        /// <summary>One "category=&lt;id&gt;|&lt;name&gt;" line. A line that is not that is skipped,
        /// the way an unreadable line of any other kind is.</summary>
        private static void ReadCategory(string value)
        {
            if (value == null) return;
            int bar = value.IndexOf('|');
            if (bar <= 0) return;
            if (!long.TryParse(value.Substring(0, bar).Trim(), out long id) || id == 0L) return;
            string name = CleanCategory(value.Substring(bar + 1));
            if (name != null) _categories[id] = name;
        }

        private static void Save()
        {
            if (_path == null)
            {
                TheGreatestPortalMod.Log.LogWarning("[TheGreatestPortal] The world is not known yet, so favorites and the default portal cannot be saved.");
                return;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var sb = new StringBuilder();
                sb.AppendLine("# The Greatest Portal: this player's default portal, favorites, recent portals (newest first) and categories for one world, by permanent portal id.");
                if (_default != 0L) sb.AppendLine("default=" + _default);
                foreach (var id in _favs) sb.AppendLine("favorite=" + id);
                foreach (var id in _recents) sb.AppendLine("recent=" + id);
                foreach (var pair in _categories) sb.AppendLine("category=" + pair.Key + "|" + pair.Value);
                File.WriteAllText(_path, sb.ToString());
            }
            catch (Exception e)
            {
                TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not save favorites to {_path}: {e.Message}");
            }
        }
    }
}

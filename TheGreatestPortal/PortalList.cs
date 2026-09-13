using System;
using System.Collections.Generic;

namespace TheGreatestPortal
{
    /// <summary>One line of a destination list: a section header or a portal.</summary>
    internal sealed class ListEntry
    {
        public bool IsHeader;
        public string Title;
        public string GroupKey;     // headers: what to collapse or expand
        public bool Collapsed;
        public PortalInfo Portal;
        public bool Favorite;
    }

    /// <summary>
    /// Builds the destination lists shown on the panel and on the map: favorites first, a
    /// search filter, optional grouping by biome, and groups that fold away. Biomes come from
    /// the world generator on the client, so nothing extra travels over the network. The set of
    /// collapsed groups is kept in the config so it survives a relog.
    /// </summary>
    internal static class PortalList
    {
        internal const string FavoritesKey = "favorites";

        private static readonly Dictionary<long, Heightmap.Biome> _biomes = new Dictionary<long, Heightmap.Biome>();
        private static HashSet<string> _collapsed;

        internal static void Reset() => _biomes.Clear();

        // ── biomes ──────────────────────────────────────────────────────────────────

        internal static Heightmap.Biome BiomeOf(PortalInfo p)
        {
            if (p == null) return Heightmap.Biome.None;
            if (_biomes.TryGetValue(p.Id, out var known)) return known;
            var wg = WorldGenerator.instance;
            if (wg == null) return Heightmap.Biome.None;
            Heightmap.Biome biome;
            try { biome = wg.GetBiome(p.Pos); }
            catch { biome = Heightmap.Biome.None; }
            _biomes[p.Id] = biome;
            return biome;
        }

        internal static string BiomeName(Heightmap.Biome biome)
        {
            if (biome == Heightmap.Biome.None) return "Unknown biome";
            return Localization.instance.Localize("$biome_" + biome.ToString().ToLower());
        }

        /// <summary>Where a portal leads, for a list row: "open", "to Name" or "to (gone)".</summary>
        internal static string DestinationText(PortalInfo p)
        {
            if (p == null || p.TargetId == 0L) return "open";
            var t = Catalog.Get(p.TargetId);
            return t != null ? "to " + t.DisplayName : "to (gone)";
        }

        internal static bool Matches(PortalInfo p, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (p.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return BiomeName(BiomeOf(p)).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ── collapsed groups ────────────────────────────────────────────────────────

        private static HashSet<string> Collapsed
        {
            get
            {
                if (_collapsed == null)
                {
                    _collapsed = new HashSet<string>();
                    string raw = TgpConfig.CollapsedGroups != null ? TgpConfig.CollapsedGroups.Value : "";
                    foreach (var k in (raw ?? "").Split(','))
                    {
                        string key = k.Trim();
                        if (key.Length > 0) _collapsed.Add(key);
                    }
                }
                return _collapsed;
            }
        }

        internal static bool IsCollapsed(string key) => key != null && Collapsed.Contains(key);

        internal static void ToggleCollapsed(string key)
        {
            if (key == null) return;
            if (!Collapsed.Remove(key)) Collapsed.Add(key);
            SaveCollapsed();
        }

        internal static void ExpandAll()
        {
            if (Collapsed.Count == 0) return;
            Collapsed.Clear();
            SaveCollapsed();
        }

        /// <summary>Collapses every group present in <paramref name="entries"/>.</summary>
        internal static void CollapseAll(List<ListEntry> entries)
        {
            bool changed = false;
            foreach (var e in entries)
                if (e.IsHeader && e.GroupKey != null && Collapsed.Add(e.GroupKey)) changed = true;
            if (changed) SaveCollapsed();
        }

        private static void SaveCollapsed()
        {
            if (TgpConfig.CollapsedGroups != null) TgpConfig.CollapsedGroups.Value = string.Join(",", Collapsed);
        }

        // ── the list ────────────────────────────────────────────────────────────────

        /// <summary>
        /// The list to show. Flat: favorites first, then the rest, alphabetical with unnamed
        /// last. Grouped: a Favorites section, then one section per biome for the rest (a
        /// favorite is listed once, under Favorites); a collapsed section is just its header. <paramref name="exclude"/>
        /// and <paramref name="excludeZdo"/> leave out the portal being configured or stood in.
        /// </summary>
        internal static List<ListEntry> Build(long exclude, ZDOID excludeZdo, string query, bool groupByBiome)
        {
            var favs = new List<PortalInfo>();
            var rest = new List<PortalInfo>();
            foreach (var p in Catalog.All)
            {
                if (exclude != 0L && p.Id == exclude) continue;
                if (excludeZdo != ZDOID.None && p.ZdoId == excludeZdo) continue;
                if (!Matches(p, query)) continue;
                (Favorites.IsFavorite(p.Id) ? favs : rest).Add(p);
            }
            favs.Sort(ByName);
            var entries = new List<ListEntry>();

            if (!groupByBiome)
            {
                rest.Sort(ByName);
                foreach (var p in favs) entries.Add(new ListEntry { Portal = p, Favorite = true });
                foreach (var p in rest) entries.Add(new ListEntry { Portal = p });
                return entries;
            }

            if (favs.Count > 0)
            {
                bool collapsed = IsCollapsed(FavoritesKey);
                entries.Add(Header($"Favorites ({favs.Count})", FavoritesKey, collapsed));
                if (!collapsed) foreach (var p in favs) entries.Add(new ListEntry { Portal = p, Favorite = true });
            }
            // Favorites live only in their own section; the biome sections hold the rest.
            var groups = new Dictionary<Heightmap.Biome, List<PortalInfo>>();
            foreach (var p in rest) Add(groups, p);
            var biomes = new List<Heightmap.Biome>(groups.Keys);
            biomes.Sort((a, b) =>
            {
                if (a == Heightmap.Biome.None) return b == Heightmap.Biome.None ? 0 : 1;
                if (b == Heightmap.Biome.None) return -1;
                return string.Compare(BiomeName(a), BiomeName(b), StringComparison.OrdinalIgnoreCase);
            });
            foreach (var biome in biomes)
            {
                var list = groups[biome];
                list.Sort(ByName);
                string key = biome.ToString();
                bool collapsed = IsCollapsed(key);
                entries.Add(Header($"{BiomeName(biome)} ({list.Count})", key, collapsed));
                if (collapsed) continue;
                foreach (var p in list) entries.Add(new ListEntry { Portal = p });
            }
            return entries;
        }

        private static ListEntry Header(string title, string key, bool collapsed)
        {
            return new ListEntry { IsHeader = true, Title = title, GroupKey = key, Collapsed = collapsed };
        }

        private static void Add(Dictionary<Heightmap.Biome, List<PortalInfo>> groups, PortalInfo p)
        {
            var biome = BiomeOf(p);
            if (!groups.TryGetValue(biome, out var list)) groups[biome] = list = new List<PortalInfo>();
            list.Add(p);
        }

        private static int ByName(PortalInfo a, PortalInfo b)
        {
            bool an = string.IsNullOrEmpty(a.Name), bn = string.IsNullOrEmpty(b.Name);
            if (an != bn) return an ? 1 : -1;
            int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        }
    }
}

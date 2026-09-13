using System;
using System.Collections.Generic;

namespace TheGreatestPortal
{
    /// <summary>One line of a destination list: a section header or a portal.</summary>
    internal sealed class ListEntry
    {
        public bool IsHeader;
        public string Title;
        public PortalInfo Portal;
        public bool Favorite;
    }

    /// <summary>
    /// Builds the destination lists shown on the panel and on the map: favorites first, a
    /// search filter, and optional grouping by biome. Biomes come from the world generator on
    /// the client, so nothing extra travels over the network.
    /// </summary>
    internal static class PortalList
    {
        private static readonly Dictionary<long, Heightmap.Biome> _biomes = new Dictionary<long, Heightmap.Biome>();

        internal static void Reset() => _biomes.Clear();

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

        internal static bool Matches(PortalInfo p, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (p.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return BiomeName(BiomeOf(p)).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The list to show. Flat: favorites first, then the rest, alphabetical with unnamed
        /// last. Grouped: a Favorites section, then one section per biome (favorites appear in
        /// their biome too). <paramref name="exclude"/> and <paramref name="excludeZdo"/> leave
        /// out the portal being configured or stood in.
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
                entries.Add(new ListEntry { IsHeader = true, Title = "Favorites" });
                foreach (var p in favs) entries.Add(new ListEntry { Portal = p, Favorite = true });
            }
            var groups = new Dictionary<Heightmap.Biome, List<PortalInfo>>();
            foreach (var p in favs) Add(groups, p);
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
                entries.Add(new ListEntry { IsHeader = true, Title = $"{BiomeName(biome)} ({list.Count})" });
                foreach (var p in list) entries.Add(new ListEntry { Portal = p, Favorite = Favorites.IsFavorite(p.Id) });
            }
            return entries;
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

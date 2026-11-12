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

        /// <summary>What this row's background says, where the color means something.</summary>
        public UiKit.RowTint Tint;

        /// <summary>What hovering this row or heading says.</summary>
        public string Tip;

        /// <summary>A Recents heading: right-clicking it switches between one list and three.</summary>
        public bool RecentsHeader;
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
        internal const string RecentsKey = "recents";
        internal const string OthersKey = "others";
        internal const string CreatedKey = "created";

        private const string CombinedTip =
            "Blue, bands leaning right: you are the only one who has been through it.\n" +
            "Green, bands leaning left: other players have.\n" +
            "Right-click to switch to separate lists.";

        private const string SeparateTip = "Right-click to switch to one combined Recents list.";

        /// <summary>Whether the three recent sections are shown as one.</summary>
        internal static bool Combined => TgpConfig.CombinedRecents == null || TgpConfig.CombinedRecents.Value;

        /// <summary>
        /// Switches between one Recents list and three. Right-clicking any of those headings does
        /// this, which is the only way to it besides the setting itself: it is a way of reading the
        /// list rather than a thing the list does, and it belongs where the reading happens.
        /// </summary>
        internal static void ToggleCombinedRecents()
        {
            var cfg = TgpConfig.CombinedRecents;
            if (cfg == null) return;
            cfg.Value = !cfg.Value;
            TheGreatestPortalMod.Message(cfg.Value ? "Recents: one combined list" : "Recents: three separate lists", always: true);
        }

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
            string category = Favorites.CategoryOf(p.Id);
            if (category != null && category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
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
        /// The list to show. Either way a Recents section comes first: the portals traveled to or
        /// from most recently, which keep their usual place below as well. Flat: favorites first, then the rest, alphabetical with unnamed
        /// last. Grouped: a Favorites section, then one section per biome for the rest (a
        /// favorite is listed once, under Favorites); a collapsed section is just its header. <paramref name="exclude"/>
        /// and <paramref name="excludeZdo"/> leave out the portal being configured or stood in.
        /// </summary>
        internal static List<ListEntry> Build(long exclude, ZDOID excludeZdo, string query, bool groupByBiome)
        {
            LastUsedId = LastUsedByMe(exclude, excludeZdo);
            var all = new List<PortalInfo>();
            var favs = new List<PortalInfo>();
            foreach (var p in Catalog.All)
            {
                if (exclude != 0L && p.Id == exclude) continue;
                if (excludeZdo != ZDOID.None && p.ZdoId == excludeZdo) continue;
                if (!Matches(p, query)) continue;
                all.Add(p);
                if (Favorites.IsFavorite(p.Id)) favs.Add(p);
            }
            favs.Sort(ByName);
            all.Sort(ByName);
            var entries = new List<ListEntry>();
            var above = new HashSet<long>();   // what the sections on top have already shown
            if (Combined) AddCombinedRecents(entries, above, exclude, excludeZdo, query);
            else
            {
                // No portal appears in more than one of the three. Each is asked to skip whatever
                // the sections above it have already shown, which is what `above` is for: three
                // headings read as one block of recents, and the same row twice in it looks like a
                // bug whatever the headings say. A section coming up one row short is the cheaper
                // of the two.
                AddRecents(entries, above, exclude, excludeZdo, query);
                AddOthersRecents(entries, above, exclude, excludeZdo, query);
                AddRecentlyCreated(entries, above, exclude, excludeZdo, query);
            }
            bool haveTop = entries.Count > 0;

            if (!groupByBiome)
            {
                if (favs.Count > 0)
                {
                    bool favsCollapsed = IsCollapsed(FavoritesKey);
                    entries.Add(Header($"Favorites ({favs.Count})", FavoritesKey, favsCollapsed));
                    if (!favsCollapsed) foreach (var p in favs) entries.Add(new ListEntry { Portal = p, Favorite = true });
                    haveTop = true;
                }
                if (AddCategories(entries, all)) haveTop = true;
                // With sections above it, the list proper needs a heading of its own too, or it
                // reads as more of the same section.
                if (haveTop && all.Count > 0) entries.Add(new ListEntry { IsHeader = true, Title = "All portals" });
                foreach (var p in all) entries.Add(new ListEntry { Portal = p, Favorite = Favorites.IsFavorite(p.Id) });
                return entries;
            }

            if (favs.Count > 0)
            {
                bool collapsed = IsCollapsed(FavoritesKey);
                entries.Add(Header($"Favorites ({favs.Count})", FavoritesKey, collapsed));
                if (!collapsed) foreach (var p in favs) entries.Add(new ListEntry { Portal = p, Favorite = true });
            }
            AddCategories(entries, all);
            // A favorite is in its biome as well as under Favorites: starring a portal should not
            // take it out of the place you would go looking for it.
            var groups = new Dictionary<Heightmap.Biome, List<PortalInfo>>();
            foreach (var p in all) Add(groups, p);
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
                foreach (var p in list) entries.Add(new ListEntry { Portal = p, Favorite = Favorites.IsFavorite(p.Id) });
            }
            return entries;
        }

        /// <summary>
        /// One Recents section in place of three: everything that has lately happened to a portal,
        /// in one order, newest first -- you went through it, somebody else did, or it was built
        /// and nobody has yet. Three lists of five answer "where have I been", "where has everyone
        /// else been" and "what is new" separately; most of the time the question is the one they
        /// share, which is what has been going on.
        ///
        /// Which of the three a row is comes from its color rather than from a heading, and the
        /// details from hovering it. Only the catalog is read, never this player's own history
        /// file: the catalog says who was last at every portal in the world, which is the whole
        /// of what this orders by, and it goes back as far as the portals do -- so raising the
        /// count fills in at once instead of from now on.
        /// </summary>
        private static void AddCombinedRecents(List<ListEntry> entries, HashSet<long> above, long exclude, ZDOID excludeZdo, string query)
        {
            int limit = TgpConfig.CombinedRecentsCount != null ? TgpConfig.CombinedRecentsCount.Value : 0;
            if (limit <= 0) return;
            var candidates = new List<PortalInfo>();
            foreach (var p in Catalog.All)
            {
                if (When(p) <= 0L) continue;   // nothing recorded: not news either way
                if (exclude != 0L && p.Id == exclude) continue;
                if (excludeZdo != ZDOID.None && p.ZdoId == excludeZdo) continue;
                if (!Matches(p, query)) continue;
                candidates.Add(p);
            }
            candidates.Sort((a, b) => When(b).CompareTo(When(a)));
            var shown = Take(candidates, above, limit);
            if (shown.Count == 0) return;
            foreach (var pi in shown) above.Add(pi.Id);
            bool collapsed = IsCollapsed(RecentsKey);
            var header = Header($"Recents ({shown.Count})", RecentsKey, collapsed);
            header.RecentsHeader = true;
            header.Tip = CombinedTip;
            entries.Add(header);
            if (collapsed) return;
            foreach (var pi in shown)
                entries.Add(new ListEntry
                {
                    Portal = pi,
                    Favorite = Favorites.IsFavorite(pi.Id),
                    Tint = TintFor(pi),
                });
        }

        /// <summary>What a category's collapsed-section key starts with, so a category called
        /// Meadows and the biome of that name fold and unfold separately.</summary>
        private const string CategoryKey = "cat:";

        /// <summary>
        /// A section for each of this player's categories, above the body of the list.
        ///
        /// Like Favorites: a categorized portal is in its category and still in the list proper,
        /// because filing a portal should put it somewhere, not take it out of the place you would
        /// have looked for it. The sections are in alphabetical order -- the player named them, so
        /// there is no order of their own to honor -- and each can be folded like any other.
        /// </summary>
        private static bool AddCategories(List<ListEntry> entries, List<PortalInfo> all)
        {
            if (all.Count == 0) return false;
            var byName = new Dictionary<string, List<PortalInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in all)
            {
                string name = Favorites.CategoryOf(p.Id);
                if (string.IsNullOrEmpty(name)) continue;
                List<PortalInfo> list;
                if (!byName.TryGetValue(name, out list)) byName[name] = list = new List<PortalInfo>();
                list.Add(p);
            }
            if (byName.Count == 0) return false;
            var names = new List<string>(byName.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                var list = byName[name];
                list.Sort(ByName);
                string key = CategoryKey + name;
                bool collapsed = IsCollapsed(key);
                entries.Add(Header($"{name} ({list.Count})", key, collapsed));
                if (collapsed) continue;
                foreach (var p in list) entries.Add(new ListEntry { Portal = p, Favorite = Favorites.IsFavorite(p.Id) });
            }
            return true;
        }

        /// <summary>
        /// The portal the player most recently traveled through, of the ones a list can show.
        ///
        /// Set as a list is built and read as its rows are made, so every section marks the same
        /// portal and the panel and the map agree.
        /// </summary>
        internal static long LastUsedId { get; private set; }

        /// <summary>
        /// The newest portal in the player's own history that this list has a row for.
        ///
        /// A trip remembers both ends with the destination newest, so the top of that history is
        /// the portal you are standing in -- which is the one the list leaves out. Skipping what
        /// the list leaves out therefore lands on where you came from, which is the one worth
        /// marking: the way back is one click rather than a search.
        /// </summary>
        private static long LastUsedByMe(long exclude, ZDOID excludeZdo)
        {
            foreach (long id in Favorites.Recents)
            {
                if (id == 0L) continue;
                if (exclude != 0L && id == exclude) continue;
                var p = Catalog.Get(id);
                if (p == null) continue;
                if (excludeZdo != ZDOID.None && p.ZdoId == excludeZdo) continue;
                return id;
            }
            return 0L;
        }

        /// <summary>When a portal last had anything happen to it: gone through, or failing that built.</summary>
        internal static long When(PortalInfo p)
        {
            if (p == null) return 0L;
            return p.UsedAt > p.Born ? p.UsedAt : p.Born;
        }

        /// <summary>Whether a name is this player's own.</summary>
        private static bool IsMe(string who)
        {
            if (string.IsNullOrEmpty(who)) return false;
            string me = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "";
            return !string.IsNullOrEmpty(me) && string.Equals(who, me, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Everyone who has been through a portal, newest first.
        ///
        /// The log is what the server has recorded since it began keeping one. A portal last used
        /// before that has only the single name the older record kept, which is still worth
        /// saying, so it stands in as a log of one.
        /// </summary>
        internal static List<PortalData.Visit> Visits(PortalInfo p)
        {
            var visits = p != null ? PortalData.ParseVisits(p.UsedLog) : new List<PortalData.Visit>();
            if (visits.Count == 0 && p != null && p.UsedAt > 0L)
                visits.Add(new PortalData.Visit(string.IsNullOrEmpty(p.UsedBy) ? "Someone" : p.UsedBy, p.UsedAt));
            return visits;
        }

        /// <summary>
        /// What color says who uses a portal: blue if you are the only one who has been through
        /// it, green once anybody else has, and the plain row if nobody has at all.
        ///
        /// Anyone else having used it outranks your having used it, because that is the thing
        /// worth seeing at a glance: blue is a portal that is yours alone. It reads the whole log
        /// rather than the last trip, so a portal stays green after you pass through it. Your own
        /// trips come from your own file as well as from the log, so a portal only you use is
        /// blue from the start rather than once the server has seen you go through it.
        /// </summary>
        private static UiKit.RowTint TintFor(PortalInfo p)
        {
            if (p == null) return UiKit.RowTint.None;
            bool mine = false;
            foreach (var v in Visits(p))
            {
                if (!IsMe(v.Who)) return UiKit.RowTint.Theirs;
                mine = true;
            }
            if (!mine) mine = Favorites.HasUsed(p.Id);
            return mine ? UiKit.RowTint.Mine : UiKit.RowTint.None;
        }

        /// <summary>
        /// Who has been through a portal and who built it, for hovering the row: the trips newest
        /// first, and the building last, because that is the oldest thing that happened to it.
        /// </summary>
        internal static string TipFor(PortalInfo p)
        {
            if (p == null) return null;
            var lines = new List<string>();
            if (LastUsedId != 0L && p.Id == LastUsedId) lines.Add("The last portal you traveled through");
            bool mine = false;
            foreach (var v in Visits(p))
            {
                if (IsMe(v.Who)) mine = true;
                lines.Add(IsMe(v.Who) ? $"You {Ago(v.At)}" : $"{v.Who} {Ago(v.At)}");
            }
            // Your own file remembers trips the server's log is too young to have seen. It keeps no
            // times, so this one goes last among the trips: everything above it is dated, and a
            // line with no date cannot claim a place among them.
            if (!mine && Favorites.HasUsed(p.Id)) lines.Add("You at some point");
            if (lines.Count == 0) lines.Add("Nobody has been through it yet");
            lines.Add(p.Born <= 0L
                ? "Built a long time ago"
                : string.IsNullOrEmpty(p.BornBy)
                    ? "Built " + Ago(p.Born)
                    : $"Built by {p.BornBy} {Ago(p.Born)}");
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>
        /// How long ago a world second was, in words.
        ///
        /// The world's own clock, not the wall clock: it stands still while nobody is playing, so
        /// "ten minutes ago" is ten minutes of the world being lived in rather than ten minutes
        /// that might have been spent asleep. A day here is a day of that, not an in-game day.
        /// </summary>
        internal static string Ago(long worldSecond)
        {
            long now = PortalData.Now();
            if (now <= 0L || worldSecond <= 0L) return "at some point";
            long since = now - worldSecond;
            if (since < 0L) return "at some point";
            if (since < 60L) return "just now";
            if (since < 3600L) return $"{since / 60L} min ago";
            if (since < 86400L)
            {
                long hours = since / 3600L;
                return hours == 1L ? "an hour ago" : $"{hours} hours ago";
            }
            long days = since / 86400L;
            return days == 1L ? "a day ago" : $"{days} days ago";
        }

        /// <summary>
        /// The Recents section: newest first, as many as the setting asks for, counted after the
        /// portal you are standing at and any gone or filtered-out ones are skipped.
        /// </summary>
        private static void AddRecents(List<ListEntry> entries, HashSet<long> above, long exclude, ZDOID excludeZdo, string query)
        {
            int limit = TgpConfig.RecentPortals != null ? TgpConfig.RecentPortals.Value : 0;
            if (limit <= 0) return;
            var shown = new List<PortalInfo>();
            foreach (long id in Favorites.Recents)
            {
                if (shown.Count >= limit) break;
                if (exclude != 0L && id == exclude) continue;
                if (above.Contains(id)) continue;   // nothing is above this one today; the rule holds anyway
                var p = Catalog.Get(id);
                if (p == null) continue;
                if (excludeZdo != ZDOID.None && p.ZdoId == excludeZdo) continue;
                if (!Matches(p, query)) continue;
                shown.Add(p);
            }
            Section(entries, above, shown, RecentsKey, "Recents", SeparateTip);
        }

        /// <summary>
        /// Where everyone else has been lately: the portals someone other than you last travelled
        /// through, newest first. The server stamps both ends of every trip, so this is the one
        /// part of the list that knows something your own game could not work out. A portal already
        /// shown in a section above is left out rather than repeated.
        /// </summary>
        private static void AddOthersRecents(List<ListEntry> entries, HashSet<long> above, long exclude, ZDOID excludeZdo, string query)
        {
            int limit = TgpConfig.OthersRecentPortals != null ? TgpConfig.OthersRecentPortals.Value : 0;
            if (limit <= 0) return;
            string me = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "";
            var candidates = new List<PortalInfo>();
            foreach (var p in Catalog.All)
            {
                if (p.UsedAt <= 0L) continue;
                if (!string.IsNullOrEmpty(me) && string.Equals(p.UsedBy, me, StringComparison.OrdinalIgnoreCase)) continue;
                if (exclude != 0L && p.Id == exclude) continue;
                if (excludeZdo != ZDOID.None && p.ZdoId == excludeZdo) continue;
                if (!Matches(p, query)) continue;
                candidates.Add(p);
            }
            candidates.Sort((a, b) => b.UsedAt.CompareTo(a.UsedAt));
            Section(entries, above, Take(candidates, above, limit), OthersKey, "Others' Recents", SeparateTip);
        }

        /// <summary>The portals built most recently, newest first. One with no recorded birth is
        /// from before the mod started noting it, and is not news.</summary>
        private static void AddRecentlyCreated(List<ListEntry> entries, HashSet<long> above, long exclude, ZDOID excludeZdo, string query)
        {
            int limit = TgpConfig.RecentlyCreatedPortals != null ? TgpConfig.RecentlyCreatedPortals.Value : 0;
            if (limit <= 0) return;
            var candidates = new List<PortalInfo>();
            foreach (var p in Catalog.All)
            {
                if (p.Born <= 0L) continue;
                if (exclude != 0L && p.Id == exclude) continue;
                if (excludeZdo != ZDOID.None && p.ZdoId == excludeZdo) continue;
                if (!Matches(p, query)) continue;
                candidates.Add(p);
            }
            candidates.Sort((a, b) => b.Born.CompareTo(a.Born));
            Section(entries, above, Take(candidates, above, limit), CreatedKey, "Recently Created", SeparateTip);
        }

        /// <summary>The first <paramref name="limit"/> that no section above has already shown.</summary>
        private static List<PortalInfo> Take(List<PortalInfo> candidates, HashSet<long> above, int limit)
        {
            var shown = new List<PortalInfo>();
            foreach (var p in candidates)
            {
                if (shown.Count >= limit) break;
                if (above.Contains(p.Id)) continue;
                shown.Add(p);
            }
            return shown;
        }

        /// <summary>Adds one of the sections that sit above the list proper, if it has anything in it.</summary>
        private static void Section(List<ListEntry> entries, HashSet<long> above, List<PortalInfo> shown, string key, string title, string tip = null)
        {
            if (shown.Count == 0) return;
            foreach (var p in shown) above.Add(p.Id);
            bool collapsed = IsCollapsed(key);
            var header = Header($"{title} ({shown.Count})", key, collapsed);
            header.RecentsHeader = true;
            header.Tip = tip;
            entries.Add(header);
            if (collapsed) return;
            foreach (var p in shown) entries.Add(new ListEntry { Portal = p, Favorite = Favorites.IsFavorite(p.Id) });
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

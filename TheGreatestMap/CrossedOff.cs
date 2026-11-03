using System;
using System.Collections.Generic;

namespace TheGreatestMap
{
    /// <summary>
    /// Which crossed-off markers are still drawn. A crossed-off deposit or plant is cleared: the
    /// thing is gone, and one switch covers all of them. Anything else crossed off is searched: the
    /// place is still there and merely done with. Those are hidden by group, because a player done
    /// with burial chambers may still want to see the sunken crypts they have cleared: each dungeon
    /// type is a group of its own, every other kind (structures, camps) is one group, and markers
    /// placed by hand are one more.
    ///
    /// A dungeon marker does not store which dungeon it is, and carries no label by default, so its
    /// type is read from its icon through the catalog. That is also how a player tells them apart
    /// on the map, so the grouping matches what is on screen.
    /// </summary>
    internal static class CrossedOff
    {
        internal const string OtherDungeons = "Other Dungeons";
        internal const string Placed = "Placed Markers";
        internal const string Unsorted = "Other Markers";

        /// <summary>
        /// Kinds whose markers are grouped by what they are rather than lumped together. Dungeons
        /// and structures are both several things wearing one category: a player done with burial
        /// chambers may still want their sunken crypts, and one done with swamp huts may still want
        /// mountain graves.
        /// </summary>
        internal static bool HasTypes(Category c) =>
            c == Category.Dungeon || c == Category.Structure || c == Category.Camp;

        /// <summary>
        /// True once this kind's types are actually told apart on the map. A type is read from a
        /// marker's icon, so a kind whose entries all wear the kind's fallback icon has nothing to
        /// split by: those all land in the catch-all, leaving one group, and the kind stays whole.
        /// Give one of them an icon of its own and it becomes a group, and the kind splits by
        /// itself -- which is what happened the day drake nests took the dragon egg.
        /// </summary>
        internal static bool Splits(Category c) => HasTypes(c) && GroupsOf(c).Count > 1;

        /// <summary>The group for one of these whose icon the catalog does not know.</summary>
        internal static string Catchall(Category c) => c == Category.Dungeon ? OtherDungeons : "Other " + Categories.Label(c);

        private sealed class Types
        {
            internal Dictionary<string, string> ByIcon;
            internal List<string> Groups;
        }

        private static readonly Dictionary<Category, Types> _types = new Dictionary<Category, Types>();
        private static HashSet<string> _hidden;
        private static HashSet<string> _cleared;

        /// <summary>Forget everything derived from the catalog, after it changes.</summary>
        internal static void Forget()
        {
            _types.Clear();
        }

        /// <summary>Forget the parsed hidden list, after the setting changes.</summary>
        internal static void ForgetHidden() => _hidden = null;

        /// <summary>Forget the parsed list of used-up types, after the setting changes.</summary>
        internal static void ForgetCleared() => _cleared = null;

        /// <summary>A crossed-off marker that means the thing is gone: a used-up deposit or plant.</summary>
        internal static bool IsCleared(SharedPin pin)
        {
            var kind = ClientPins.KindOf(pin);
            return kind.HasValue && Categories.IsResource(kind.Value);
        }

        /// <summary>The group a searched marker belongs to; null for a cleared one.</summary>
        internal static string GroupOf(SharedPin pin)
        {
            var kind = ClientPins.KindOf(pin);
            if (kind.HasValue)
            {
                if (Categories.IsResource(kind.Value)) return null;
                if (Splits(kind.Value)) return TypeOf(kind.Value, ClientPins.IconOf(pin));
                return Categories.Label(kind.Value);
            }
            return pin.Auto ? Unsorted : Placed;
        }

        /// <summary>The kind whose types this group belongs to, or null if it is not one of those.</summary>
        internal static Category? OwnerOf(string group)
        {
            if (group == null) return null;
            foreach (var cat in Categories.All)
                if (Splits(cat) && Contains(GroupsOf(cat), group)) return cat;
            return null;
        }

        /// <summary>Every group of this kind there can be, in catalog order, ending with the catch-all.</summary>
        internal static List<string> GroupsOf(Category cat)
        {
            Build(cat, out _, out var groups);
            return groups;
        }

        /// <summary>True when this marker is crossed off and the player has chosen not to see its kind of crossed-off marker.</summary>
        internal static bool IsHidden(SharedPin pin)
        {
            if (!pin.Checked) return false;
            if (IsCleared(pin)) return !ClearedDrawn(ClientPins.IconOf(pin));
            return IsGroupHidden(GroupOf(pin));
        }

        // ── used up, one type at a time ──────────────────────────────────────────
        //
        // A used-up deposit has no group: the thing is gone, and what is left to say about it is
        // which type it was. So these are hidden by their icon, the same way a type is hidden
        // anywhere else in this mod, with Show Cleared Deposits over the top of them as the
        // blanket switch it has always been.
        //
        // One switch for every one of them was the whole of it until 4.7.0, and the button on
        // each row was a lie: pressing "1 done" on the copper row put the picked raspberries back
        // on the map along with the worked copper, because there was only ever one switch behind
        // all of those buttons.

        /// <summary>Is a used-up marker of this type still drawn?</summary>
        internal static bool ClearedDrawn(string icon)
        {
            if (TgmConfig.ShowClearedDeposits != null && !TgmConfig.ShowClearedDeposits.Value) return false;
            string key = IconRegistry.Normalize(icon);
            return key == null || !Cleared().Contains(key);
        }

        /// <summary>Is anything used up of any of these types still drawn?</summary>
        internal static bool AnyClearedDrawn(IEnumerable<string> icons)
        {
            if (icons == null) return false;
            foreach (var icon in icons) if (ClearedDrawn(icon)) return true;
            return false;
        }

        internal static int HiddenClearedCount() => Cleared().Count;

        /// <summary>
        /// Show or hide the used-up markers of these types.
        ///
        /// <para>Turning one on while the blanket switch is off is the player asking for that type
        /// and not for the rest, so the blanket comes off and everything else goes on this list --
        /// the same hand-over the marker list makes when a type is turned on inside a kind that is
        /// switched off. Without it the button would do nothing at all, which is how it was found:
        /// every "done" button on the list was lit, one was pressed, and the only thing that
        /// happened was that all the others went out.</para>
        /// </summary>
        internal static void SetClearedDrawn(IEnumerable<string> icons, bool draw)
        {
            if (icons == null) return;
            var set = new HashSet<string>(Cleared(), StringComparer.OrdinalIgnoreCase);
            bool blanket = TgmConfig.ShowClearedDeposits != null && !TgmConfig.ShowClearedDeposits.Value;
            if (!draw)
            {
                foreach (var icon in icons)
                {
                    string key = IconRegistry.Normalize(icon);
                    if (key != null) set.Add(key);
                }
                SaveCleared(set);
                return;
            }
            if (blanket) set = new HashSet<string>(ClearedIcons(), StringComparer.OrdinalIgnoreCase);
            foreach (var icon in icons)
            {
                string key = IconRegistry.Normalize(icon);
                if (key != null) set.Remove(key);
            }
            // The list before the blanket: both of these redraw the map as they are written, and
            // lifting the blanket first would put every used-up marker in the world back for a
            // frame.
            SaveCleared(set);
            if (blanket) TgmConfig.ShowClearedDeposits.Value = true;
        }

        /// <summary>Every type the catalog knows whose crossed-off markers mean the thing is gone.</summary>
        internal static List<string> ClearedIcons()
        {
            var list = new List<string>();
            foreach (var cat in Categories.All) if (Categories.IsResource(cat)) Icons(cat, list);
            return list;
        }

        /// <summary>The same, for one kind of them.</summary>
        internal static List<string> ClearedIconsOf(Category cat)
        {
            var list = new List<string>();
            if (Categories.IsResource(cat)) Icons(cat, list);
            return list;
        }

        private static void Icons(Category cat, List<string> into)
        {
            foreach (var kv in Catalog.TypesOf(cat))
            {
                string key = IconRegistry.Normalize(kv.Key);
                if (key != null) into.Add(key);
            }
        }

        private static HashSet<string> Cleared()
        {
            if (_cleared != null) return _cleared;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in ((TgmConfig.HiddenCleared != null ? TgmConfig.HiddenCleared.Value : null) ?? "").Split(','))
            {
                string key = IconRegistry.Normalize(raw);
                if (key != null) set.Add(key);
            }
            _cleared = set;
            return set;
        }

        private static void SaveCleared(HashSet<string> set)
        {
            if (TgmConfig.HiddenCleared == null) return;
            var list = new List<string>(set);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            string line = string.Join(",", list.ToArray());
            if (!string.Equals(line, TgmConfig.HiddenCleared.Value, StringComparison.Ordinal))
                TgmConfig.HiddenCleared.Value = line;
        }

        internal static bool IsGroupHidden(string group)
        {
            return group != null && Hidden().Contains(group);
        }

        internal static int HiddenGroupCount() => Hidden().Count;

        internal static void SetGroupHidden(string group, bool hide)
        {
            if (group == null) return;
            var set = new HashSet<string>(Hidden(), StringComparer.OrdinalIgnoreCase);
            if (hide) set.Add(group);
            else set.Remove(group);
            Save(set);
        }

        /// <summary>Every group of one kind at once. The other kinds are left alone.</summary>
        internal static void SetTypesHidden(Category cat, bool hide)
        {
            var set = new HashSet<string>(Hidden(), StringComparer.OrdinalIgnoreCase);
            foreach (var group in GroupsOf(cat))
            {
                if (hide) set.Add(group);
                else set.Remove(group);
            }
            Save(set);
        }

        /// <summary>
        /// The group a type of a kind belongs to, asked without a marker in hand: the marker list
        /// needs it for a row that stands for a type rather than for one marker. Null for a kind
        /// whose crossed-off markers mean the thing is gone rather than done with -- those share
        /// one switch, having no group of their own.
        /// </summary>
        internal static string GroupFor(Category cat, string icon)
        {
            if (Categories.IsResource(cat)) return null;
            if (Splits(cat)) return TypeOf(cat, icon);
            return Categories.Label(cat);
        }

        /// <summary>
        /// Is anything crossed off still being drawn? Asked by the one switch at the foot of the
        /// marker list, which is the whole of this idea in one press -- the per-kind and per-type
        /// switches live on the rows they are about.
        /// </summary>
        internal static bool AnyShown()
        {
            if (AnyClearedDrawn(ClearedIcons())) return true;
            foreach (var cat in Categories.All)
            {
                if (Categories.IsResource(cat)) continue;
                if (Splits(cat))
                {
                    foreach (var group in GroupsOf(cat)) if (!IsGroupHidden(group)) return true;
                }
                else if (!IsGroupHidden(Categories.Label(cat))) return true;
            }
            return !IsGroupHidden(Placed) || !IsGroupHidden(Unsorted);
        }

        /// <summary>Stop drawing everything that has been crossed off, of every kind.</summary>
        internal static void HideAll()
        {
            var set = new HashSet<string>(Hidden(), StringComparer.OrdinalIgnoreCase);
            foreach (var cat in Categories.All)
            {
                if (Categories.IsResource(cat)) continue;
                if (Splits(cat)) foreach (var group in GroupsOf(cat)) set.Add(group);
                else set.Add(Categories.Label(cat));
            }
            set.Add(Placed);
            set.Add(Unsorted);
            Save(set);
            // The blanket covers every type at once, so the per-type list has nothing left to say
            // and is cleared rather than left to disagree with it later.
            if (TgmConfig.HiddenCleared != null && !string.IsNullOrEmpty(TgmConfig.HiddenCleared.Value))
                TgmConfig.HiddenCleared.Value = "";
            if (TgmConfig.ShowClearedDeposits != null) TgmConfig.ShowClearedDeposits.Value = false;
        }

        internal static void ShowAll()
        {
            if (TgmConfig.ShowClearedDeposits != null && !TgmConfig.ShowClearedDeposits.Value) TgmConfig.ShowClearedDeposits.Value = true;
            if (TgmConfig.HiddenCleared != null && !string.IsNullOrEmpty(TgmConfig.HiddenCleared.Value))
                TgmConfig.HiddenCleared.Value = "";
            if (TgmConfig.HiddenSearched != null && !string.IsNullOrEmpty(TgmConfig.HiddenSearched.Value)) TgmConfig.HiddenSearched.Value = "";
        }

        private static string TypeOf(Category cat, string icon)
        {
            Build(cat, out var byIcon, out _);
            string key = IconRegistry.Normalize(icon);
            if (key != null && byIcon.TryGetValue(key, out var type)) return type;
            return Catchall(cat);
        }

        private static void Build(Category cat, out Dictionary<string, string> byIcon, out List<string> groups)
        {
            if (_types.TryGetValue(cat, out var cached))
            {
                byIcon = cached.ByIcon;
                groups = cached.Groups;
                return;
            }
            byIcon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            groups = new List<string>();
            // Everything still wearing the kind's own fallback icon is not a type, it is the
            // remainder: one house pin covers abandoned farms, swamp huts and stone towers alike,
            // and naming that group after whichever of them the catalog happened to list first
            // would be a lie. They go to the catch-all until the catalog tells them apart.
            string fallback = IconRegistry.Normalize(Categories.DefaultIcon(cat));
            foreach (var kv in Catalog.TypesOf(cat))
            {
                string group = string.Equals(kv.Key, fallback, StringComparison.OrdinalIgnoreCase)
                    ? Catchall(cat)
                    : Plural(kv.Value);
                if (byIcon.ContainsKey(kv.Key)) continue;
                byIcon[kv.Key] = group;
                if (!Contains(groups, group)) groups.Add(group);
            }
            // Sunken crypts wore the swamp crypt key before they had a trophy, and markers written
            // then keep it: the repair only moves markers whose name says they are something else.
            string cryptKey = IconRegistry.Normalize("CryptKey");
            if (cat == Category.Dungeon && !byIcon.ContainsKey(cryptKey))
            {
                string sunken = groups.Find(g => g.StartsWith("Sunken", StringComparison.OrdinalIgnoreCase));
                if (sunken != null) byIcon[cryptKey] = sunken;
            }
            if (!Contains(groups, Catchall(cat))) groups.Add(Catchall(cat));
            // An icon the catalog does not spell out is worked out by looking items up, which needs
            // the item database. Without it the answer is used once rather than remembered.
            if (ObjectDB.instance != null) _types[cat] = new Types { ByIcon = byIcon, Groups = groups };
        }

        private static bool Contains(List<string> list, string value)
        {
            return list.Exists(g => string.Equals(g, value, StringComparison.OrdinalIgnoreCase));
        }

        private static string Plural(string name)
        {
            if (string.IsNullOrEmpty(name)) return OtherDungeons;
            return name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? name : name + "s";
        }

        private static HashSet<string> Hidden()
        {
            if (_hidden != null) return _hidden;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in ((TgmConfig.HiddenSearched != null ? TgmConfig.HiddenSearched.Value : null) ?? "").Split(','))
            {
                string s = raw.Trim();
                if (s.Length > 0) set.Add(s);
            }
            _hidden = set;
            return set;
        }

        private static void Save(HashSet<string> set)
        {
            if (TgmConfig.HiddenSearched == null) return;
            var list = new List<string>(set);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            TgmConfig.HiddenSearched.Value = string.Join(",", list);
        }
    }
}

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
        internal static bool HasTypes(Category c) => c == Category.Dungeon || c == Category.Structure;

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

        /// <summary>Forget everything derived from the catalog, after it changes.</summary>
        internal static void Forget()
        {
            _types.Clear();
        }

        /// <summary>Forget the parsed hidden list, after the setting changes.</summary>
        internal static void ForgetHidden() => _hidden = null;

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
                if (Splits(kind.Value)) return TypeOf(kind.Value, pin.Icon);
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
            if (IsCleared(pin)) return TgmConfig.ShowClearedDeposits != null && !TgmConfig.ShowClearedDeposits.Value;
            return IsGroupHidden(GroupOf(pin));
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

        internal static void ShowAll()
        {
            if (TgmConfig.ShowClearedDeposits != null && !TgmConfig.ShowClearedDeposits.Value) TgmConfig.ShowClearedDeposits.Value = true;
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

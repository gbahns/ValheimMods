using System;
using System.Collections.Generic;

namespace TheGreatestMap
{
    /// <summary>
    /// Looking for one thing on a crowded map.
    ///
    /// A map that has been shared for a few weeks carries hundreds of markers, and finding the
    /// frost caves among them means reading past everything else. Hiding is already there, kind by
    /// kind and icon by icon, but pressing it into service to see one thing means switching off
    /// twenty others and then remembering what was off to begin with.
    ///
    /// So this sits over the top of the hide switches rather than among them. While a focus is set,
    /// only what matches is drawn, and nothing is written down: clearing it puts the map back
    /// exactly as it was, whatever was hidden stays hidden, and there is nothing to undo by hand.
    /// It overrides those switches while it lasts, because a player asking for frost caves means
    /// the frost caves whether or not dungeons happen to be switched off.
    ///
    /// Three ways to name the thing, one at a time. A kind, for the broad sweep. A single icon --
    /// a type, a frost cave rather than a dungeon, since the icon is the only thing on a marker
    /// that says which. Or text, read against the type's name, the words written on the marker and
    /// the kind's name, so "frost" finds the caves and "eggs" finds the portal somebody called
    /// 3 Eggs.
    /// </summary>
    internal static class Focus
    {
        private static Category? _kind;
        private static string _icon;
        private static string _text;
        private static Category? _biomeKind;
        private static Heightmap.Biome _biome;

        internal static bool On => _kind.HasValue || _icon != null || _biomeKind.HasValue
                                  || !string.IsNullOrEmpty(_text);
        internal static string Text => _text ?? "";
        internal static Category? Kind => _kind;
        internal static string Icon => _icon;

        /// <summary>
        /// What a marker is, as narrowly as the catalog can say: a frost cave, not a dungeon.
        /// Failing that, the two kinds of failure want different answers. A structure still wearing
        /// the house pin is one of a crowd the catalog cannot yet tell apart, so it is named after
        /// its kind. A marker whose kind has no types at all is already as narrow as it gets, and
        /// there the icon's own name is the useful one -- "Copper Ore" rather than "Ore".
        ///
        /// <para>Except where the icon is the kind's own picture, which is a picture of the kind
        /// and not of any one type in it. Named after that, a row claims a type nobody has: the
        /// marker list grew a "Coins" under Traders and a "Boss" under Boss Altars, each the one
        /// row under a heading it was already named by.</para>
        /// </summary>
        internal static string TypeLabel(SharedPin pin)
        {
            if (pin == null) return "marker";
            string named = Catalog.NameForSource(pin.Source);
            if (!string.IsNullOrEmpty(named)) return named;
            var kind = ClientPins.KindOf(pin);
            if (kind.HasValue)
            {
                string type = Catalog.TypeNameFor(kind.Value, ClientPins.IconOf(pin));
                if (!string.IsNullOrEmpty(type)) return type;
                if (CrossedOff.HasTypes(kind.Value)) return Categories.Label(kind.Value);
                // Wearing its kind's own picture, which is the picture chosen to stand for the
                // whole kind: that says nothing narrower than the kind, so naming it after the
                // picture invents a type that does not exist -- "Coins" under Traders, "Boss"
                // under Boss Altars, "Fire" under Camps. The kind's own name is the honest answer,
                // and because it then says what the row above says, the triangle and the single
                // pointless row under it both go away.
                if (IconRegistry.SameKey(ClientPins.IconOf(pin), Categories.DefaultIcon(kind.Value)))
                    return Categories.Label(kind.Value);
            }
            // Down to the icon, which is the useful name where a kind's types each have a picture
            // of their own: "Copper Ore" rather than "Ore".
            return IconRegistry.DisplayName(ClientPins.IconOf(pin));
        }

        /// <summary>
        /// What a row is: one kind wearing one picture. Not the picture alone, which two kinds
        /// can share, and not the kind alone, which has as many rows as it has pictures.
        /// </summary>
        internal static string TypeKey(Category? kind, string icon) =>
            (kind.HasValue ? kind.Value.ToString() : "") + "|" + (IconRegistry.Normalize(icon) ?? icon ?? "");

        /// <summary>
        /// The one group every one of these belongs to, or null if they are not all in one. A
        /// group is a name somebody chose for a set of things, which is better than any name a
        /// machine can work out from the things themselves.
        /// </summary>
        private static string SharedGroup(HashSet<string> sources)
        {
            string shared = null;
            foreach (var source in sources)
            {
                string group = Catalog.GroupOf(source);
                if (string.IsNullOrEmpty(group)) return null;
                if (shared == null) shared = group;
                else if (!string.Equals(shared, group, StringComparison.OrdinalIgnoreCase)) return null;
            }
            return shared;
        }

        /// <summary>
        /// The words every one of these names begins with, or null when they share none.
        ///
        /// <para>For a row covering several things at once: "Dvergr Guard Tower", "Dvergr
        /// Excavation" and "Dvergr Harbor" are all dvergr, and "Dvergr" is both true of the row
        /// and shorter than naming one of them. Whole words only -- the common letters of
        /// "Stonehenge" and "Stone Circle" are "Stone", which would be a row called after a
        /// material. Where there is nothing in common the caller falls back to the kind.</para>
        /// </summary>
        private static string SharedName(HashSet<string> names)
        {
            List<string> first = null;
            foreach (var name in names)
            {
                var words = new List<string>((name ?? "").Split(' '));
                if (first == null) { first = words; continue; }
                int keep = 0;
                while (keep < first.Count && keep < words.Count
                       && string.Equals(first[keep], words[keep], StringComparison.OrdinalIgnoreCase)) keep++;
                first.RemoveRange(keep, first.Count - keep);
                if (first.Count == 0) return null;
            }
            if (first == null || first.Count == 0) return null;
            string shared = string.Join(" ", first.ToArray()).Trim();
            return shared.Length == 0 ? null : shared;
        }

        /// <summary>What is being looked for, for a row or a message to say.</summary>
        internal static string What()
        {
            if (_icon != null) return LabelForIcon(_icon);
            if (_kind.HasValue) return Categories.Label(_kind.Value);
            if (!string.IsNullOrEmpty(_text)) return "\"" + _text + "\"";
            return "";
        }

        /// <summary>
        /// What everything wearing this picture is, said as one thing: the group they are all in,
        /// or the words their names share, or their kind -- and only failing all three, the name
        /// of the picture itself.
        ///
        /// <para>For anything that acts on a whole picture at once, which is a set and not a
        /// thing. "Hide all Black Marble 2x2x2 markers" named the block the picture was borrowed
        /// from; what it would actually hide is the dvergr ruins. A picture is a costume and the
        /// menu should say who is wearing it.</para>
        ///
        /// <para>It used to take the first marker it found and use that one's name, which is the
        /// same race the type rows lost -- one of the set standing in for all of it.</para>
        /// </summary>
        internal static string LabelForIcon(string icon)
        {
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Category? kind = null;
            foreach (var pin in ClientPins.All)
            {
                if (!IconRegistry.SameKey(ClientPins.IconOf(pin), icon)) continue;
                sources.Add(pin.Source ?? "");
                names.Add(TypeLabel(pin) ?? "");
                if (!kind.HasValue) kind = ClientPins.KindOf(pin);
            }
            if (names.Count == 1)
            {
                foreach (var only in names) return only;
            }
            if (names.Count > 1)
            {
                string shared = SharedGroup(sources) ?? SharedName(names);
                if (!string.IsNullOrEmpty(shared)) return shared;
                if (kind.HasValue) return Categories.Label(kind.Value);
            }
            return IconRegistry.DisplayName(icon);
        }

        internal static void Clear()
        {
            if (!On) return;
            _kind = null;
            _icon = null;
            _text = null;
            _biomeKind = null;
            ClientPins.Restyle();
        }

        /// <summary>What biome is being looked in, if the focus is one of a kind's biome rows.</summary>
        internal static Category? BiomeKind => _biomeKind;
        internal static Heightmap.Biome BiomeShown => _biome;

        /// <summary>Only this kind, and only where it stands in this biome.</summary>
        internal static void SetBiome(Category kind, Heightmap.Biome biome)
        {
            if (_biomeKind.HasValue && _biomeKind.Value == kind && _biome == biome) { Clear(); return; }
            _biomeKind = kind;
            _biome = biome;
            _kind = null;
            _icon = null;
            _text = null;
            ClientPins.Restyle();
        }

        /// <summary>Only this kind. Asking for the one already focused clears it, so the same row turns it off.</summary>
        internal static void SetKind(Category kind)
        {
            if (_kind.HasValue && _kind.Value == kind) { Clear(); return; }
            _kind = kind;
            _icon = null;
            _text = null;
            ClientPins.Restyle();
        }

        /// <summary>Only markers wearing this icon -- one type.</summary>
        internal static void SetIcon(string icon)
        {
            string key = IconRegistry.Normalize(icon);
            if (key == null) { Clear(); return; }
            if (_icon != null && IconRegistry.SameKey(_icon, key)) { Clear(); return; }
            _icon = key;
            _kind = null;
            _text = null;
            ClientPins.Restyle();
        }

        /// <summary>Only markers mentioning this text. Empty text is no focus at all.</summary>
        internal static void Search(string text)
        {
            string needle = (text ?? "").Trim();
            if (needle.Length == 0)
            {
                if (_text == null) return;
                _text = null;
                ClientPins.Restyle();
                return;
            }
            _text = needle;
            _kind = null;
            _icon = null;
            ClientPins.Restyle();
        }

        /// <summary>
        /// Whether a kind drawn live from the server -- ships, carts -- is what is being looked
        /// for. Those have no marker to ask about: the pins are the server's answer, drawn and
        /// thrown away, so the kind and its icon are all there is to go on.
        /// </summary>
        internal static bool ShowsKind(Category kind, string icon)
        {
            if (!On) return true;
            if (_biomeKind.HasValue) return _biomeKind.Value == kind;
            if (_icon != null) return IconRegistry.SameKey(_icon, icon);
            if (_kind.HasValue) return _kind.Value == kind;
            return Has(Categories.Label(kind), _text);
        }

        /// <summary>True when this marker is the thing being looked for. Always true with no focus set.</summary>
        internal static bool Shows(SharedPin pin)
        {
            if (!On || pin == null) return true;
            if (_biomeKind.HasValue)
            {
                var standsIn = ClientPins.KindOf(pin);
                return standsIn.HasValue && standsIn.Value == _biomeKind.Value
                       && Biomes.Of(pin.Pos) == _biome;
            }
            if (_icon != null) return IconRegistry.SameKey(ClientPins.IconOf(pin), _icon);
            if (_kind.HasValue)
            {
                var kind = ClientPins.KindOf(pin);
                return kind.HasValue && kind.Value == _kind.Value;
            }
            return Mentions(pin, _text);
        }

        /// <summary>
        /// Everything a marker can be called, against what was typed: the words written on it, the
        /// type it is, the kind it belongs to, and the icon's own name for a marker whose kind does
        /// not tell its types apart. Any one of them is enough -- a player typing "frost" has no
        /// reason to know which of those the word lives in.
        /// </summary>
        private static bool Mentions(SharedPin pin, string needle)
        {
            if (string.IsNullOrEmpty(needle)) return true;
            if (Has(pin.Name, needle)) return true;
            if (Has(Catalog.NameForSource(pin.Source), needle)) return true;
            var kind = ClientPins.KindOf(pin);
            if (kind.HasValue)
            {
                if (Has(Categories.Label(kind.Value), needle)) return true;
                string type = Catalog.TypeNameFor(kind.Value, ClientPins.IconOf(pin));
                if (type != null && Has(type, needle)) return true;
            }
            return Has(IconRegistry.DisplayName(ClientPins.IconOf(pin)), needle);
        }

        private static bool Has(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack)
                && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>How many markers are being shown, and how many there are in all.</summary>
        internal static void Tally(out int shown, out int all)
        {
            shown = 0;
            all = 0;
            foreach (var pin in ClientPins.All)
            {
                all++;
                if (Shows(pin)) shown++;
            }
        }

        /// <summary>One type of marker on the map: its icon, what to call it, and how many there are.</summary>
        internal sealed class TypeCount
        {
            public string Icon;
            public string Label;
            public Category? Kind;
            public int Count;
            /// <summary>Set when this row stands for one biome rather than for one picture.</summary>
            public Heightmap.Biome? Biome;
            /// <summary>One of the places the game marks itself, rather than a type of marker this mod recorded.</summary>
            public bool Place;
        }

        /// <summary>
        /// Every type of marker on the map that mentions this text, commonest first. Built from the
        /// markers themselves rather than from the catalog, so it lists what is actually out there:
        /// a type nobody has found yet is not something to go looking for.
        /// </summary>
        internal static List<TypeCount> Types(string text)
        {
            string needle = (text ?? "").Trim();
            var byIcon = new Dictionary<string, TypeCount>(StringComparer.OrdinalIgnoreCase);
            // What each row turned out to cover. A row is keyed by picture, and most pictures
            // belong to one thing, so the first marker's name is the row's name. The house pin is
            // not one of those: every structure in the game wears it, for want of a picture of an
            // abandoned farm. Naming the row after whichever marker the dictionary happened to
            // meet first put "Graveyard" over 251 markers that were mostly farms and ruins -- and
            // it only began once those markers knew what they were, since a nameless marker fell
            // through to the kind's own name and the race never showed.
            var sourcesOf = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var namesOf = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pin in ClientPins.All)
            {
                if (needle.Length > 0 && !Mentions(pin, needle)) continue;
                string key = IconRegistry.Normalize(ClientPins.IconOf(pin)) ?? IconRegistry.FallbackKey;
                // A kind whose types all wear the kind's own picture gets a row per biome instead
                // of the one row per picture it would otherwise have. See Catalog.GroupsByBiome.
                var groups = ClientPins.KindOf(pin);
                if (groups.HasValue && Categories.GroupsByBiome(groups.Value) && Biomes.Known)
                {
                    var where = Biomes.Of(pin.Pos);
                    string bkey = Biomes.Key(groups.Value, where);
                    if (!byIcon.TryGetValue(bkey, out var row))
                        byIcon[bkey] = row = new TypeCount
                        {
                            Icon = key, Label = Biomes.Label(where), Kind = groups, Biome = where,
                        };
                    row.Count++;
                    continue;
                }
                // Keyed by the kind as well as the picture. A picture can be worn by two
                // kinds -- iron scrap is a swamp scrap pile and a sunken crypt, guck is a guck
                // sack and an infested tree -- and keyed by the picture alone those became one
                // row, filed under whichever kind the first marker happened to belong to. The
                // other kind then counted markers it had no row for: Ore read "1010" over rows
                // adding to 1007, and the three missing were scrap piles listed under Dungeons.
                string rowKey = TypeKey(groups, key);
                if (!byIcon.TryGetValue(rowKey, out var entry))
                {
                    byIcon[rowKey] = entry = new TypeCount { Icon = key, Label = TypeLabel(pin), Kind = groups };
                    sourcesOf[rowKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    namesOf[rowKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                entry.Count++;
                sourcesOf[rowKey].Add(pin.Source ?? "");
                // What this marker stands for, and only that. TypeLabel falls back to the
                // kind's own name for a marker that cannot say -- and one unidentified draugr
                // village putting "Enemy Bases" into this set was enough to outvote the real
                // name and rename the row after the heading above it.
                string says = Catalog.NameForSource(pin.Source);
                if (!string.IsNullOrEmpty(says)) namesOf[rowKey].Add(says);
            }
            foreach (var entry in byIcon.Values)
            {
                if (!entry.Kind.HasValue || entry.Biome.HasValue) continue;
                string rowKey = TypeKey(entry.Kind, entry.Icon);
                if (!namesOf.ContainsKey(rowKey)) continue;
                // The question is whether this row covers several differently-named things, not
                // whether its picture happens to be the kind's own. That was the first answer and
                // it was too narrow by half: the four dvergr bases share the dvergr trophy, which
                // is nobody's default, so a row covering guard towers, excavations, lighthouses
                // and harbors alike was labelled "Dvergr Harbour" -- whichever of them the
                // dictionary met first. Several prefabs under one name (Ruin1, Ruin2, Ruin3 are
                // all "Black Forest Ruin") are one type and keep theirs.
                var says = namesOf[rowKey];
                // Nothing wearing this picture can say what it is, so the label it already has --
                // the kind's, or the picture's -- is the best there is.
                if (says.Count == 0) continue;
                // One thing, named. Taken even though the row already has a label, because that
                // label came from whichever marker was met first and that one may have been a
                // marker that could not say.
                if (says.Count == 1)
                {
                    foreach (var only in says) entry.Label = only;
                    continue;
                }
                // The group they are all in, where they are all in one: that name was written for
                // exactly this -- "Dvergr Bases" over the four kinds of place the dvergr hold --
                // and it beats anything worked out from the names themselves. See Tiers.
                entry.Label = SharedGroup(sourcesOf[rowKey])
                              ?? SharedName(says)
                              ?? Categories.Label(entry.Kind.Value);
            }
            var list = new List<TypeCount>(byIcon.Values);
            var order = TgmConfig.TypeRowOrder != null ? TgmConfig.TypeRowOrder.Value : TypeOrder.Progression;
            list.Sort((a, b) =>
            {
                // Biome rows read as a journey, always, whatever the setting says about the rest:
                // "most first" over Meadows, Black Forest, Swamp is a list nobody can scan.
                if (a.Biome.HasValue && b.Biome.HasValue)
                    return Biomes.Order(a.Biome.Value).CompareTo(Biomes.Order(b.Biome.Value));
                if (order == TypeOrder.MostFirst && a.Count != b.Count) return b.Count.CompareTo(a.Count);
                if (order == TypeOrder.Progression)
                {
                    // The catalog's own order, which its lines are written in: a dandelion before
                    // a thistle. Anything the catalog does not list sorts after everything it
                    // does, by name, rather than jumbling in among them.
                    int ra = Catalog.OrderOf(a.Icon), rb = Catalog.OrderOf(b.Icon);
                    if (ra != rb) return ra.CompareTo(rb);
                }
                return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }
    }
}

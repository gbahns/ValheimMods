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

        internal static bool On => _kind.HasValue || _icon != null || !string.IsNullOrEmpty(_text);
        internal static string Text => _text ?? "";
        internal static Category? Kind => _kind;
        internal static string Icon => _icon;

        /// <summary>
        /// What a marker is, as narrowly as the catalog can say: a frost cave, not a dungeon.
        /// Failing that, the two kinds of failure want different answers. A structure still wearing
        /// the house pin is one of a crowd the catalog cannot yet tell apart, so it is named after
        /// its kind. A marker whose kind has no types at all is already as narrow as it gets, and
        /// there the icon's own name is the useful one -- "Copper Ore" rather than "Ore".
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
            }
            return IconRegistry.DisplayName(ClientPins.IconOf(pin));
        }

        /// <summary>What is being looked for, for a row or a message to say.</summary>
        internal static string What()
        {
            if (_icon != null) return LabelForIcon(_icon);
            if (_kind.HasValue) return Categories.Label(_kind.Value);
            if (!string.IsNullOrEmpty(_text)) return "\"" + _text + "\"";
            return "";
        }

        private static string LabelForIcon(string icon)
        {
            foreach (var pin in ClientPins.All)
                if (IconRegistry.SameKey(ClientPins.IconOf(pin), icon)) return TypeLabel(pin);
            return IconRegistry.DisplayName(icon);
        }

        internal static void Clear()
        {
            if (!On) return;
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
            if (_icon != null) return IconRegistry.SameKey(_icon, icon);
            if (_kind.HasValue) return _kind.Value == kind;
            return Has(Categories.Label(kind), _text);
        }

        /// <summary>True when this marker is the thing being looked for. Always true with no focus set.</summary>
        internal static bool Shows(SharedPin pin)
        {
            if (!On || pin == null) return true;
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
            foreach (var pin in ClientPins.All)
            {
                if (needle.Length > 0 && !Mentions(pin, needle)) continue;
                string key = IconRegistry.Normalize(ClientPins.IconOf(pin)) ?? IconRegistry.FallbackKey;
                if (!byIcon.TryGetValue(key, out var entry))
                    byIcon[key] = entry = new TypeCount { Icon = key, Label = TypeLabel(pin), Kind = ClientPins.KindOf(pin) };
                entry.Count++;
            }
            var list = new List<TypeCount>(byIcon.Values);
            var order = TgmConfig.TypeRowOrder != null ? TgmConfig.TypeRowOrder.Value : TypeOrder.Progression;
            list.Sort((a, b) =>
            {
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

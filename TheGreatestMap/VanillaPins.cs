using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// The map's own pins in the marker list: the ones a player drops by hand, their death marker
    /// and their bed, and the landmarks the game draws for the world.
    ///
    /// <para>These were the hole in that list. It had a row for everything this mod records and
    /// everything the server answers for, and nothing at all for the hundred pins a player had
    /// placed themselves -- so a map with both on it could be read from one place for half of it
    /// and had to be read from vanilla's own buttons for the other half. Worse, the two halves
    /// said different things with the same words: pressing Structures in this list hides the
    /// structures this mod recorded and leaves a hand-placed house pin exactly where it was.</para>
    ///
    /// <para><b>One switch, not a second one.</b> Vanilla already has this switch --
    /// <c>m_visibleIconTypes</c>, set by double-clicking or alt-clicking an icon button under the
    /// map -- so these rows press that rather than keeping a shadow copy, and vanilla's button
    /// goes gray when a row here turns its type off, exactly as if the button had been clicked.
    /// Two controls for one state have to move together or the one you are not looking at is
    /// lying. This is the opposite call to the one the hide-all master makes, which hides pins
    /// one at a time and deliberately leaves <c>m_visibleIconTypes</c> alone: that switch is
    /// momentary and is not what the buttons mean, while this one is the same switch under
    /// another name.</para>
    ///
    /// <para>Two of these the game gives no button for at all -- the bed, and the landmarks --
    /// because <c>UpdatePins</c> reads the array for every pin it draws while only eight types
    /// have a button. So the list ends up with switches vanilla does not have, which is the
    /// usual reason to put something in a list.</para>
    /// </summary>
    internal static class VanillaPins
    {
        /// <summary>
        /// What a player put on the map themselves: the five icons the game lets them drop, where
        /// they died, and where they will wake up. The order is the order the buttons are in
        /// under the map, so the two read alike.
        /// </summary>
        internal static readonly Minimap.PinType[] Yours =
        {
            Minimap.PinType.Icon0, Minimap.PinType.Icon1, Minimap.PinType.Icon2,
            Minimap.PinType.Icon3, Minimap.PinType.Icon4,
            Minimap.PinType.Death, Minimap.PinType.Bed,
        };

        /// <summary>
        /// What the game put there. The boss altars, the traders, Hildir's camps and the center of
        /// the world are every one of them a <c>PinType.None</c> pin, rebuilt every five seconds
        /// from the locations the world knows, so one row is the switch for all of them and there
        /// is no finer grain to offer. <c>Boss</c> is listed beside it because vanilla has a
        /// filter button for it, and anything wearing it was not placed by a player.
        /// </summary>
        internal static readonly Minimap.PinType[] Theirs =
        {
            Minimap.PinType.None, Minimap.PinType.Boss,
        };

        /// <summary>The names as the mod ships them, for the setting to start from.</summary>
        internal const string DefaultNames =
            "Icon0=Fire,Icon1=House,Icon2=Hammer,Icon3=Dot,Icon4=Portal," +
            "Death=Death Marker,Bed=Bed,None=Landmarks,Boss=Boss Pins";

        private static readonly Dictionary<Minimap.PinType, int> _counts =
            new Dictionary<Minimap.PinType, int>();

        private static Dictionary<Minimap.PinType, string> _names;
        private static string _namesFrom;

        /// <summary>
        /// Count the map's own pins by type, ours left out of it.
        ///
        /// <para>Ours are in the same list -- a recorded marker is a vanilla pin with a picture on
        /// it -- and they have their own rows already, so counting them here would say every
        /// number twice. Read when the list is built and on every refresh, which is per press
        /// rather than per frame.</para>
        /// </summary>
        internal static void Recount(Minimap map)
        {
            _counts.Clear();
            if (map == null) return;
            foreach (var pin in Access.Pins(map))
            {
                if (pin == null || ClientPins.IsOurs(pin)) continue;
                _counts.TryGetValue(pin.m_type, out int n);
                _counts[pin.m_type] = n + 1;
            }
        }

        internal static int CountOf(Minimap.PinType type)
        {
            _counts.TryGetValue(type, out int n);
            return n;
        }

        /// <summary>How many of these the map is drawing: all of them, or none.</summary>
        internal static int ShownCount(Minimap map, Minimap.PinType type)
        {
            return Drawn(map, type) ? CountOf(type) : 0;
        }

        /// <summary>
        /// Is this type being drawn? Vanilla's array, read straight: there is nothing else to
        /// read, and nothing of ours to disagree with it.
        /// </summary>
        internal static bool Drawn(Minimap map, Minimap.PinType type)
        {
            if (map == null) return false;
            var visible = Access.VisibleIconTypes(map);
            int at = (int)type;
            return visible == null || at < 0 || at >= visible.Length || visible[at];
        }

        /// <summary>
        /// Show or hide a type, as vanilla's own double-click does, down to graying out its
        /// button.
        ///
        /// <para>Written out here rather than calling the game's <c>ToggleIconFilter</c>, which
        /// does the same three things and also plays a gamepad rumble: a click on a row in a list
        /// is not the press of an icon button, and a list that buzzed the pad every time somebody
        /// quietened a kind would be the one thing in this panel nobody asked for.</para>
        /// </summary>
        internal static void SetDrawn(Minimap map, Minimap.PinType type, bool show)
        {
            if (map == null) return;
            var visible = Access.VisibleIconTypes(map);
            int at = (int)type;
            if (visible == null || at < 0 || at >= visible.Length) return;
            if (visible[at] == show) return;
            visible[at] = show;
            // Vanilla rebuilds its pins from this array rather than hiding them in place, so
            // nothing happens until it is told the layout is stale.
            Access.PinUpdateRequired(map) = true;
            Repaint(map);
        }

        internal static void Toggle(Minimap map, Minimap.PinType type) => SetDrawn(map, type, !Drawn(map, type));

        /// <summary>
        /// Put vanilla's icon buttons back in step with the array: white for a type being drawn,
        /// gray for one that is not. The game colors the button behind the little selection
        /// image, which is what its own filter does, so this reaches the same object.
        /// </summary>
        private static void Repaint(Minimap map)
        {
            var icons = Access.SelectedIcons(map);
            if (icons == null) return;
            var visible = Access.VisibleIconTypes(map);
            if (visible == null) return;
            foreach (var kv in icons)
            {
                if (kv.Value == null) continue;
                int at = (int)kv.Key;
                if (at < 0 || at >= visible.Length) continue;
                var parent = kv.Value.transform.parent;
                var image = parent != null ? parent.GetComponent<UnityEngine.UI.Image>() : null;
                if (image != null) image.color = visible[at] ? Color.white : Color.gray;
            }
        }

        /// <summary>
        /// Arm the game's own placing for this icon, which is what clicking its button under the
        /// map does. The game's method rather than ours: it moves the little highlight from one
        /// button to the next and turns the ping selection off, and a player who then looks down
        /// at the buttons has to see the one this list armed.
        /// </summary>
        internal static void Place(Minimap map, Minimap.PinType type)
        {
            if (map != null) Access.SelectIcon(map, type);
        }

        /// <summary>Is the game set to place this icon next?</summary>
        internal static bool Armed(Minimap map, Minimap.PinType type)
        {
            // Not while this mod has a kind of its own armed: arming one sets vanilla's selected
            // icon to Icon0 as a side effect, and the fire row would have claimed to be armed
            // every time somebody readied a berry bush.
            return map != null && !ManualPlace.Armed.HasValue && Access.SelectedType(map) == type;
        }

        /// <summary>
        /// A type a player can actually drop by hand. Death, the bed, the landmarks and the boss
        /// pins are not: the game places those itself, so a row for one has a switch and no
        /// place-by-hand button -- there would be nothing to arm.
        /// </summary>
        internal static bool Placeable(Minimap.PinType type)
        {
            switch (type)
            {
                case Minimap.PinType.Icon0:
                case Minimap.PinType.Icon1:
                case Minimap.PinType.Icon2:
                case Minimap.PinType.Icon3:
                case Minimap.PinType.Icon4:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// What a row calls this type. The game gives these no names at all -- they are five
        /// pictures and nothing else, and what each one means is whatever the player decided it
        /// means -- so they are named after what they look like and the setting lets a player
        /// write down what theirs are for. Hammer is Greg's "a place", Dot is "a resource";
        /// somebody else's are something else, and the mod has no business guessing.
        /// </summary>
        internal static string Label(Minimap.PinType type)
        {
            string line = TgmConfig.VanillaPinNames != null ? TgmConfig.VanillaPinNames.Value : DefaultNames;
            if (_names == null || !string.Equals(_namesFrom, line, StringComparison.Ordinal))
            {
                _namesFrom = line;
                _names = new Dictionary<Minimap.PinType, string>();
                Parse(DefaultNames);
                Parse(line);
            }
            return _names.TryGetValue(type, out var name) && !string.IsNullOrEmpty(name) ? name : type.ToString();
        }

        private static void Parse(string line)
        {
            foreach (var raw in (line ?? "").Split(','))
            {
                string one = raw.Trim();
                int eq = one.IndexOf('=');
                if (eq <= 0) continue;
                string key = one.Substring(0, eq).Trim();
                string name = one.Substring(eq + 1).Trim();
                if (name.Length == 0) continue;
                if (Enum.TryParse(key, true, out Minimap.PinType type)) _names[type] = name;
            }
        }

        internal static string IconKey(Minimap.PinType type) => "pin:" + type;

        internal static string Tip(Minimap.PinType type)
        {
            switch (type)
            {
                case Minimap.PinType.Death:
                    return "Where you died, which the game pins for you. The same switch as double-clicking the " +
                           "skull under the map.";
                case Minimap.PinType.Bed:
                    return "Where you will wake up. The game draws it and gives no button for it, so this is the " +
                           "only switch there is.";
                case Minimap.PinType.None:
                    return "The landmarks the game draws for the world: boss altars, traders, Hildir's camps and the " +
                           "center of it. All one pin type, so this is one switch for the lot, and the game gives no " +
                           "button for it. The hide-all box leaves these alone on purpose -- they are the map rather " +
                           "than notes on it -- so this is how you put them away.";
                case Minimap.PinType.Boss:
                    return "Pins wearing the game's boss icon that no player placed. The same switch as " +
                           "double-clicking that icon under the map.";
                default:
                    return "Pins you placed by hand with this icon. The same switch as double-clicking the icon " +
                           "under the map, and the picture means whatever you decided it means -- the name is yours " +
                           "to set in Vanilla Pin Names.";
            }
        }

        // ── a whole heading's worth ──────────────────────────────────────────────

        private static Minimap.PinType[] Under(Group group)
        {
            if (group == Group.Yours) return Yours;
            return group == Group.Places ? Theirs : null;
        }

        /// <summary>How many of the map's own pins sit under this heading, and how many are drawn.</summary>
        internal static void Tally(Minimap map, Group group, out int all, out int shown)
        {
            all = 0;
            shown = 0;
            var types = Under(group);
            if (types == null) return;
            foreach (var type in types)
            {
                all += CountOf(type);
                shown += ShownCount(map, type);
            }
        }

        internal static bool AnyShown(Minimap map, Group group)
        {
            var types = Under(group);
            if (types == null) return false;
            foreach (var type in types) if (CountOf(type) > 0 && Drawn(map, type)) return true;
            return false;
        }

        /// <summary>
        /// Show or hide every one of these under a heading. Only the types that are actually
        /// there: writing to a type nobody has any of would gray out a button for nothing.
        /// </summary>
        internal static void SetAll(Minimap map, Group group, bool show)
        {
            var types = Under(group);
            if (types == null) return;
            foreach (var type in types) if (CountOf(type) > 0) SetDrawn(map, type, show);
        }
    }
}

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
        /// What the game put there, as rows of this list's own. There are none left: the
        /// landmarks were one row called Landmarks standing for the center of the world, the
        /// altars, the traders and Hildir's quests all at once, and the boss pins were another
        /// row for the altars somebody had read a vegvisir for. Both are told apart by
        /// <see cref="WorldPins"/> now and drawn on the rows that are already about those things,
        /// which is one row per thing. The array stays because the heading's switch asks it what
        /// it holds, and the day the game draws something new that belongs to nobody, this is
        /// where it goes.
        /// </summary>
        internal static readonly Minimap.PinType[] Theirs = new Minimap.PinType[0];

        /// <summary>The names as the mod ships them, for the setting to start from.</summary>
        internal const string DefaultNames =
            "Icon0=Fire,Icon1=House,Icon2=Hammer,Icon3=Dot,Icon4=Portal," +
            "Death=Death Marker,Bed=Bed,None=Landmarks,Boss=Bosses";

        /// <summary>
        /// The names as 4.7.0 first shipped them, which lived in one unreleased build. A setting
        /// saved from that build holds the old string, and a saved string wins over the default --
        /// so somebody who had the map open before this would go on reading "Boss Pins" forever
        /// over a default that says Bosses. A line still exactly as it shipped was nobody's
        /// choice, so it is read as the default it was rather than as a preference.
        /// </summary>
        private const string ShippedOnce =
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
                if (pin == null || Drew(pin)) continue;
                _counts.TryGetValue(pin.m_type, out int n);
                _counts[pin.m_type] = n + 1;
            }
        }

        /// <summary>
        /// Did this mod draw this pin? Everything of ours is a vanilla pin underneath -- that is
        /// how a marker is drawn at all -- so the only way to count the player's own pins is to
        /// know ours when we see them.
        ///
        /// <para>The markers are the easy half and were the whole of it for a while, which was
        /// wrong by one: the live portals are drawn with vanilla's portal icon, which means
        /// vanilla's portal pin type, so every portal the server answered for was being counted
        /// as a pin the player had dropped by hand. There was a Portal row under Placed Markers
        /// with a dozen in it and a Portals row under Dynamic Markers with the same dozen, and the
        /// one under Placed Markers could hide them. The ships, the carts and the erased-marker ghosts
        /// are drawn with pin types of this mod's own, so they could never have landed here --
        /// they are asked about anyway, because that is a fact about today's icons and not a rule
        /// anybody promised to keep.</para>
        /// </summary>
        internal static bool Drew(Minimap.PinData pin)
        {
            if (ClientPins.IsOurs(pin)) return true;
            foreach (var portal in Portals.DrawnPins) if (portal == pin) return true;
            foreach (var mark in OwnMarks.DrawnPins) if (mark == pin) return true;
            foreach (var shown in Transports.DrawnPins) if (shown != null && shown.Pin == pin) return true;
            foreach (var ghost in Erasures.DrawnPins) if (ghost == pin) return true;
            foreach (var ghost in SurveyPreview.DrawnPins) if (ghost == pin) return true;
            return false;
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
            if (map == null) return;
            // Whatever kind of ours was armed is not armed any more, and has to stop saying it
            // is: arming works by writing a pin type into the one field the map reads when a pin
            // is placed, and this is about to write another one into it. Two rows both claiming
            // the next pin is theirs, when only the last press can be right, is the kind of lie
            // this list is built not to tell.
            ManualPlace.Cancel();
            Access.SelectIcon(map, type);
        }

        /// <summary>
        /// Ready the game's ping, which is sent by double-clicking the map and heard by everybody
        /// in range. A press while it is already ready puts it away again, because something has
        /// to: while the ping is ready a double-click on the map pings rather than placing a pin,
        /// and a player who had forgotten arming it would find the map had stopped taking
        /// markers.
        ///
        /// <para>This exists because hiding the game's buttons hides its ping button with them,
        /// and the ping is the one thing on that strip no row in this list stands for -- there is
        /// no such thing as a ping marker to list.</para>
        /// </summary>
        internal static void TogglePing(Minimap map)
        {
            if (map == null) return;
            // Asked before Cancel, which now puts the ping away itself: reading it afterwards
            // always said "not armed", and a press on the lit row armed it again instead of
            // turning it off.
            bool was = PingArmed(map);
            ManualPlace.Cancel();
            Access.SelectIcon(map, was ? Minimap.PinType.Icon0 : Minimap.PinType.Ping);
        }

        internal static bool PingArmed(Minimap map)
        {
            return map != null && Access.SelectedType(map) == Minimap.PinType.Ping;
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
            if (string.Equals(line, ShippedOnce, StringComparison.Ordinal)) line = DefaultNames;
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

        internal static string IconKey(Minimap.PinType type) => IconRegistry.KeyForVanilla((int)type);

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
                    return "Everything on your map wearing this picture, whether you dropped it with the game's " +
                           "own button or this mod recorded it -- one row, because they are one thing. The switch " +
                           "moves both, and the game's button under the map grays out with it. The pin at the end " +
                           "places one. The picture means whatever you decided it means -- the name is yours to " +
                           "set in Vanilla Pin Names.";
            }
        }

        // ── the game's own buttons ───────────────────────────────────────────────

        /// <summary>
        /// Should the game's row of pin buttons be off the map? Only while there is a way to the
        /// list that replaced it: with the marker button off, the list cannot be opened at all,
        /// and hiding both would leave a map with no pin controls whatever -- a setting that
        /// takes something away has to leave the thing it was traded for reachable.
        /// </summary>
        internal static bool HidingControls =>
            TgmConfig.HideVanillaControls != null && TgmConfig.HideVanillaControls.Value
            && TgmConfig.MarkerButton != null && TgmConfig.MarkerButton.Value;

        /// <summary>
        /// Take the game's pin controls off the large map, or put them back: the buttons, the
        /// plate they sit on and the little mouse pictures beside them telling you which button
        /// does what. Read every frame the map is open, so a changed setting shows at once,
        /// nothing is left hidden by a mod that has been switched off, and the key hints stay
        /// down -- the game sets those active again every time the map is opened.
        ///
        /// <para>The boxes rather than the buttons one at a time, which is what this did first
        /// and was wrong: it left the plates they were drawn on and the mouse pictures standing in
        /// an empty corner, which is worse to look at than the buttons were. Boxes plural, which
        /// is what it got wrong next: the game keeps the five you place in one box and the death
        /// and boss filters in another, so a search that started from the first icon and climbed
        /// found the one box and left the other sitting there. Every one of the buttons is a
        /// starting point now -- see <see cref="Boxes"/>.</para>
        /// </summary>
        internal static void Controls(Minimap map, bool hide)
        {
            // Nothing of the map's has been touched and nothing is being asked for: no reason to
            // go looking for the boxes at all, which is most of the time, this being off unless
            // somebody turned it on.
            if (map == null || (!hide && !_hidden)) return;
            _hidden = hide;

            foreach (var box in Boxes(map)) Show(map, box, !hide);

            // The mouse pictures, which are the game's key hints for the map and belong to these
            // controls -- they say which button places a pin and which erases one. A list of its
            // own rather than part of the strip because the game sets every one of them active
            // whenever the large map opens, from the Key Hints setting, so they have to be put
            // back down afterwards rather than once.
            if (map.m_hints != null)
                foreach (var hint in map.m_hints) Show(map, hint, !hide);
        }

        private static bool _hidden;
        private static readonly List<GameObject> _boxes = new List<GameObject>();
        private static Minimap _boxesFor;
        private static bool _looked;

        private static GameObject Button(UnityEngine.UI.Image selected)
        {
            return selected != null && selected.transform.parent != null
                ? selected.transform.parent.gameObject : null;
        }

        /// <summary>
        /// Every one of the game's pin buttons: the five you can place, the death and boss
        /// filters, and the ping. The dictionary the game filters by and the ping image beside
        /// it, which is the whole of that strip and the only part of it this mod can name.
        /// </summary>
        private static IEnumerable<GameObject> Seats(Minimap map)
        {
            var icons = Access.SelectedIcons(map);
            if (icons != null)
                foreach (var kv in icons)
                {
                    var seat = Button(kv.Value);
                    if (seat != null) yield return seat;
                }
            var ping = Button(map.m_selectedIconPing);
            if (ping != null) yield return ping;
        }

        private static void Show(Minimap map, GameObject go, bool show)
        {
            if (go == null) return;
            // The touch-screen ping panel is the game's to switch: it sets that panel active or
            // inactive every frame from whether a touch device is in use, so anything written
            // there by this mod is overwritten and all that is left of it is the fight.
            if (map.m_touchPingPanel != null && go.transform.IsChildOf(map.m_touchPingPanel)) return;
            if (go.activeSelf != show) go.SetActive(show);
        }

        /// <summary>
        /// The objects that hold the game's pin buttons and nothing else of the map's: as few as
        /// one, and as many as there are buttons.
        ///
        /// <para>Found by climbing. Each button starts where it stands and goes up while the next
        /// one up still contains nothing the map needs -- the plate behind a box of buttons and
        /// the mouse pictures beside them are a level or two above them, which is why this climbs
        /// at all. It stops the moment a level holds something that has to stay, which is what
        /// keeps the public-position box, the biome name, the pin-name box and the shared-map
        /// hint on the screen; those are checked for by the fields the game exposes them through,
        /// so a level is judged by what the game says is in it rather than by its name. A button
        /// whose own box holds something that has to stay is hidden by itself, and the plate
        /// behind it stays.</para>
        ///
        /// <para>Every button rather than one of them because the game keeps them in more than
        /// one box -- the five you place in one, the death and boss filters in another -- and
        /// climbing from the first icon alone found the first box and left the second where it
        /// was. Where two of them climb to the same place the answer is one object, since an
        /// ancestor already covers everything under it. <c>tgm_mapui</c> prints what was found.
        /// </para>
        /// </summary>
        private static List<GameObject> Boxes(Minimap map)
        {
            if (_boxesFor != map || !_looked)
            {
                _boxesFor = map;
                _looked = true;
                _boxes.Clear();
                Search(map, _boxes);
            }
            return _boxes;
        }

        private static void Search(Minimap map, List<GameObject> into)
        {
            var root = map.m_largeRoot != null ? map.m_largeRoot.transform : null;
            foreach (var seat in Seats(map))
            {
                var found = seat.transform;
                for (var up = found.parent; OnlyControls(map, up, root); up = up.parent) found = up;
                Keep(into, found.gameObject);
            }
        }

        /// <summary>
        /// Remember one thing to hide, in place of anything of it we were already going to hide:
        /// hiding an object hides everything under it, so a list holding both an object and its
        /// parent would be doing the same work twice and saying it twice in the report.
        /// </summary>
        private static void Keep(List<GameObject> into, GameObject go)
        {
            for (int i = into.Count - 1; i >= 0; i--)
            {
                var have = into[i];
                if (have == null) { into.RemoveAt(i); continue; }
                // IsChildOf counts a transform as its own child, so this covers "the same one"
                // as well.
                if (go.transform.IsChildOf(have.transform)) return;
                if (have.transform.IsChildOf(go.transform)) into.RemoveAt(i);
            }
            into.Add(go);
        }

        /// <summary>
        /// Is this level of the map nothing but the pin controls? Everything the map needs is
        /// asked for by name; anything else in there is the plate, the hints and the spacing,
        /// which is what we came for.
        /// </summary>
        private static bool OnlyControls(Minimap map, Transform at, Transform root)
        {
            // The large map's own root holds everything there is, so the climb stops below it.
            if (at == null || at == root) return false;
            foreach (var keep in Keepers(map))
                if (keep != null && keep.IsChildOf(at)) return false;
            return !Modded(at);
        }

        private static IEnumerable<Transform> Keepers(Minimap map)
        {
            yield return map.m_publicPosition != null ? map.m_publicPosition.transform : null;
            yield return map.m_mapImageLarge != null ? map.m_mapImageLarge.transform : null;
            yield return map.m_pinRootLarge;
            yield return map.m_pinNameRootLarge;
            yield return map.m_biomeNameLarge != null ? map.m_biomeNameLarge.transform : null;
            yield return map.m_largeMarker;
            yield return map.m_largeShipMarker;
            yield return map.m_windMarker;
            yield return map.m_gamepadCrosshair;
            yield return map.m_sharedMapHint != null ? map.m_sharedMapHint.transform : null;
            yield return map.m_touchPingPanel;
            yield return Access.NameInput(map);
        }

        /// <summary>
        /// Anything another mod -- or this one -- has put in here. This mod's own map-pin button
        /// is a clone of one of these and moves out to the map root as it is built, so it should
        /// never be found; a button hiding itself would be a hard thing to work out from the
        /// outside, which is reason enough to look.
        /// </summary>
        private static bool Modded(Transform at)
        {
            if (at.name.StartsWith("TGM_", StringComparison.Ordinal)) return true;
            foreach (Transform child in at) if (Modded(child)) return true;
            return false;
        }

        /// <summary>What <c>tgm_mapui</c> says about all this, for working out what was hidden and what was not.</summary>
        internal static string StripReport(Minimap map)
        {
            if (map == null) return "no map";
            var boxes = Boxes(map);
            if (boxes.Count == 0) return "none found";
            var said = new List<string>(boxes.Count);
            foreach (var box in boxes)
            {
                if (box == null) continue;
                int n = box.GetComponentsInChildren<Transform>(true).Length;
                string where = box.transform.parent != null ? box.transform.parent.name : "none";
                said.Add($"{box.name} ({n} objects, parent {where}){(box.activeSelf ? "" : " -- hidden")}");
            }
            int hints = map.m_hints != null ? map.m_hints.Count : 0;
            return string.Join("; ", said.ToArray()) +
                   $", plus {hints} key hint{(hints == 1 ? "" : "s")}";
        }

        // ── a whole heading's worth ──────────────────────────────────────────────

        private static Minimap.PinType[] Under(Group group)
        {
            if (group == Group.Yours) return Yours;
            return group == Group.Places ? Theirs : null;
        }

        /// <summary>How many of the map's own pins sit under this heading, and how many are drawn.</summary>
        // There was a Tally here that added up CountOf over a heading's pictures, for the
        // heading to print. It counted the pins the game holds and not this mod's markers wearing
        // the same pictures, while the rows under it counted both -- so Placed Markers said 2
        // over rows of 25 hammers and 33 dots. A heading adds up its own rows now, which is the
        // only arithmetic a reader can check: see KindMenu._rowCounts.

        /// <summary>
        /// How many markers of this mod's wear one of these pictures and belong to no kind.
        ///
        /// <para>The other half of what a picture is. Kindless on purpose: a marker that belongs
        /// to a kind is counted and switched by that kind's rows, and counting it here as well
        /// would say the same markers twice under one heading. What is left is exactly what a
        /// player placed by hand without saying what it was -- which, since a pin placed with
        /// vanilla's own control is adopted the moment it is named, is every one of those.</para>
        /// </summary>
        internal static int OursWearing(Minimap.PinType type)
        {
            string key = IconKey(type);
            int n = 0;
            foreach (var pin in ClientPins.All)
            {
                if (ClientPins.KindOf(pin).HasValue) continue;
                if (IconRegistry.SameKey(ClientPins.IconOf(pin), key)) n++;
            }
            return n;
        }

        /// <summary>Is this mod's half of a picture being drawn?</summary>
        internal static bool OursLit(Minimap.PinType type) => !TgmConfig.IsOwnIconHidden(IconKey(type));

        /// <summary>
        /// The game's own button was used: bring this mod's half of that picture into line.
        ///
        /// <para>The row in the marker list presses both halves, and this is the same thing from
        /// the other side -- a player who alt-clicks the hammer under the map means the hammers,
        /// not the hammers this mod happens not to have drawn. Without it the game's button moved
        /// its own pins and left every marker wearing that picture standing, which is exactly the
        /// "two controls for one state" the list was built to end.</para>
        ///
        /// <para>Written whether or not anything wears the picture yet. The point of the switch
        /// is the picture rather than today's markers, so a hammer placed after it was switched
        /// off should stay off; and the list has always written this setting itself, so a key in
        /// it for a picture nobody is using costs nothing. tgm_show clears the lot.</para>
        /// </summary>
        internal static void MirrorFromVanilla(Minimap map, Minimap.PinType type)
        {
            bool show = Drawn(map, type);
            if (OursLit(type) == show) return;
            SetOursDrawn(type, show);
        }

        /// <summary>
        /// Start the game's buttons where this mod's switches already are.
        ///
        /// <para>Vanilla builds its filter afresh every session with everything visible, while a
        /// picture switched off in the marker list stays off because this mod writes it down. So
        /// the two disagreed at every login: the button under the map white, the row above it
        /// unlit, and the markers hidden. Pushing our state into vanilla's array once, as the map
        /// is built, settles it in the direction that has a memory.</para>
        /// </summary>
        internal static void Restore(Minimap map)
        {
            if (map == null) return;
            foreach (var type in Yours)
                if (Placeable(type) && Drawn(map, type) != OursLit(type)) SetDrawn(map, type, OursLit(type));
        }

        /// <summary>Show or hide this mod's half of a picture.</summary>
        internal static void SetOursDrawn(Minimap.PinType type, bool show)
        {
            // The list about markers a player placed, not the general one: these rows are
            // counted over markers with no kind (see OursWearing), so they have to hide the same
            // ones they count. A recorded structure wears the same house pin and is not theirs.
            TgmConfig.SetOwnIconHidden(IconKey(type), !show);
        }

        /// <summary>
        /// Is anything wearing one of these pictures being drawn? Both halves, because a picture
        /// is one thing: the heading above it would otherwise read "nothing to hide" over a map
        /// full of markers it had drawn itself.
        /// </summary>
        internal static bool AnyShown(Minimap map, Group group)
        {
            var types = Under(group);
            if (types == null) return false;
            foreach (var type in types)
            {
                if (CountOf(type) > 0 && Drawn(map, type)) return true;
                if (OursWearing(type) > 0 && OursLit(type)) return true;
            }
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
            foreach (var type in types)
            {
                int ours = OursWearing(type);
                // Vanilla's own switch moves while either half is there, so its button under the
                // map stays in step with the row above it. Only a picture nothing wears at all is
                // left alone, which is the case the old rule was written for.
                if (CountOf(type) > 0 || ours > 0) SetDrawn(map, type, show);
                if (ours > 0) SetOursDrawn(type, show);
            }
        }
    }
}

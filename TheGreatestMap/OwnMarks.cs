using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// This mod's own markers for the places the game marks itself: the boss altars and the three
    /// errands Hildir sends you on.
    ///
    /// <para>Drawn in place of the game's pins rather than beside them. The game marks every altar
    /// on every map from the first minute and all three of her sites once she has asked, so a
    /// marker of this mod's standing with one is the same place marked twice -- which is what a
    /// player saw and reported. One switch each decides which of the two you get, and because it
    /// is one switch there is no way to ask for both.</para>
    ///
    /// <para><b>Drawn from the game's marks, not from records.</b> That is the whole point and it
    /// was the bug in the first attempt: the boss half drew recorded altar markers, so a player
    /// with records for two altars and none for the rest turned the switch on, had the game's
    /// pins hidden as promised, and got two markers where there had been seven. Nothing is
    /// recorded for Hildir's errands at all. So the question a swap has to answer is not "what
    /// has this mod written down" but "what has the game marked", and the answer is on the map
    /// already: its landmark pins for the altars and her sites, and its vegvisir pins for the
    /// altars somebody has read a stone for.</para>
    ///
    /// <para>What each one is comes from the catalog, by the location it stands on: Eikthyrnir is
    /// Eikthyr and wears his trophy, Hildir_cave is the Howling Cavern and wears whatever that
    /// line says. So the pictures are the player's to set, in the same place every other picture
    /// in this mod is set, and a line edited by hand moves these with it.</para>
    ///
    /// <para>The places are gathered whether or not either switch is on, because the marker list
    /// names them under their rows either way -- that is what makes "Hildir's Quests" a row you
    /// can unfold into the Howling Cavern, the Smoldering Tomb and the Sealed Tower rather than a
    /// number. Only the drawing asks the switches.</para>
    ///
    /// <para>Live pins, like the portals and the ships: m_save false, no record, nothing shared,
    /// nothing that can reach a character file or another player's map. They are gone the moment
    /// the setting is.</para>
    /// </summary>
    internal static class OwnMarks
    {
        /// <summary>One place the game has marked, and what this mod would call it.</summary>
        internal sealed class Mark
        {
            public Vector3 Pos;
            public WorldPins.Kind Kind;
            /// <summary>The world's location standing there, where anything could say which it is.</summary>
            public string Location;
            public string Icon;
            public string Name;
            /// <summary>This mod's pin for it, while the swap is drawing them.</summary>
            public Minimap.PinData Pin;

            public Category Cat =>
                Kind == WorldPins.Kind.Boss ? Category.BossAltar : Category.HildirQuest;
        }

        /// <summary>
        /// How often the map is asked what the game has marked. Nothing but a boss stone being
        /// read, Hildir asking, and a switch moving can change the answer, so there is nothing to
        /// be gained by looking faster -- and the asking is a walk along the whole pin list.
        /// </summary>
        private const float Every = 2f;

        /// <summary>How close two marks have to be to be the same place.</summary>
        private const float Near = 1f;

        private static readonly List<Mark> _marks = new List<Mark>();
        private static readonly List<Minimap.PinData> _drawn = new List<Minimap.PinData>();
        private static float _next;
        private static string _was;

        /// <summary>The pins this drew, so nothing takes them for markers that lost their record.</summary>
        internal static List<Minimap.PinData> DrawnPins => _drawn;

        /// <summary>Every place the game has marked of the two kinds, drawn or not.</summary>
        internal static List<Mark> Marks => _marks;

        internal static void Reset()
        {
            _marks.Clear();
            _drawn.Clear();
            _next = 0f;
            _was = null;
            _sites = null;
            _counted = false;
            _born = 0f;
        }

        /// <summary>Look again at once, for a switch that has just moved.</summary>
        internal static void Forget()
        {
            _next = 0f;
            _was = null;
        }

        /// <summary>Vanilla cleared every pin on a profile load; ours went with them.</summary>
        internal static void OnPinsCleared()
        {
            _drawn.Clear();
            foreach (var mark in _marks) mark.Pin = null;
            _was = null;
        }

        /// <summary>One of the game's own quest marks. Nothing else ever wears these three types.</summary>
        internal static bool IsHildirMark(Minimap.PinData pin)
        {
            return pin != null && (pin.m_type == Minimap.PinType.Hildir1
                                   || pin.m_type == Minimap.PinType.Hildir2
                                   || pin.m_type == Minimap.PinType.Hildir3);
        }

        private static bool BossWanted =>
            WorldPins.CustomBoss && WorldPins.Wanted(WorldPins.Kind.Boss)
            && (!TgmConfig.ShowKind.TryGetValue(Category.BossAltar, out var show) || show.Value);

        private static bool HildirWanted =>
            WorldPins.CustomHildir && WorldPins.Wanted(WorldPins.Kind.Hildir)
            && (!TgmConfig.ShowKind.TryGetValue(Category.HildirQuest, out var show) || show.Value);

        private static bool Wanted(WorldPins.Kind kind) =>
            kind == WorldPins.Kind.Boss ? BossWanted : HildirWanted;

        /// <summary>Whether this mod draws its own marker for this place: the switches, and the one type row.</summary>
        private static bool Drawing(Mark mark) =>
            Wanted(mark.Kind) && !TgmConfig.IsPlaceHidden(mark.Icon);

        /// <summary>
        /// Whether the game's own pin standing here should be drawn, as far as the one place is
        /// concerned. The kind's switch is a separate question and is asked by the caller; this
        /// one is the type row under it -- Smoldering Tomb switched off on its own.
        /// </summary>
        internal static bool SiteShown(Minimap.PinData pin)
        {
            if (pin == null) return true;
            var mark = At(pin.m_pos);
            return mark == null || !TgmConfig.IsPlaceHidden(mark.Icon);
        }

        /// <summary>
        /// Whether this place is marked on the map at all, by this mod's marker or by the game's
        /// own pin. What a type row under one of these two kinds means by lit: with the swap off
        /// the game's pin is the one standing there, and a row reading none of one while a pin for
        /// it is on the map would be this list contradicting the map beside it.
        /// </summary>
        internal static bool Lit(Mark mark)
        {
            if (mark == null || TgmConfig.IsPlaceHidden(mark.Icon)) return false;
            return mark.Pin != null || WorldPins.Drawn(mark.Kind);
        }

        /// <summary>Whether any one place is switched off, which is what makes the hiding pass worth running.</summary>
        internal static bool AnySiteHidden
        {
            get
            {
                foreach (var mark in _marks) if (TgmConfig.IsPlaceHidden(mark.Icon)) return true;
                return false;
            }
        }

        /// <summary>The place marked here, if there is one.</summary>
        private static Mark At(Vector3 pos)
        {
            foreach (var mark in _marks)
                if (Utils.DistanceXZ(mark.Pos, pos) < Near) return mark;
            return null;
        }

        /// <summary>
        /// How many places of this kind the game has marked, and how many of them are marked on
        /// the map now. One per place however many pins the game put on it: an altar somebody has
        /// read a vegvisir for carries two, and each of Hildir's sites carries her quest pin as
        /// well as a landmark.
        /// </summary>
        internal static void Tally(WorldPins.Kind kind, out int places, out int lit)
        {
            places = 0;
            lit = 0;
            foreach (var mark in _marks)
            {
                if (mark.Kind != kind) continue;
                places++;
                if (Lit(mark)) lit++;
            }
        }

        /// <summary>Where the places of one kind are, for a ping that points at a whole row.</summary>
        internal static void Spots(WorldPins.Kind kind, List<Vector3> into)
        {
            if (into == null) return;
            foreach (var mark in _marks)
            {
                if (mark.Kind != kind) continue;
                bool had = false;
                foreach (var spot in into)
                    if (Geo.FlatDistance(spot, mark.Pos) < Near) { had = true; break; }
                if (!had) into.Add(mark.Pos);
            }
        }

        /// <summary>
        /// Where the places wearing one picture are, for a ping that points at a single row under
        /// one of those kinds -- the Bonemass altar rather than every altar.
        ///
        /// <para>Positions rather than this mod's own pins, because with the swap off there are
        /// none of ours: the pin standing on that altar is the game's, and a row that lists the
        /// altar has to be able to point at whatever is marking it.</para>
        /// </summary>
        internal static void SpotsWithIcon(string icon, List<Vector3> into)
        {
            if (into == null || string.IsNullOrEmpty(icon)) return;
            foreach (var mark in _marks)
            {
                if (!IconRegistry.SameKey(mark.Icon, icon)) continue;
                bool had = false;
                foreach (var spot in into)
                    if (Geo.FlatDistance(spot, mark.Pos) < Near) { had = true; break; }
                if (!had) into.Add(mark.Pos);
            }
        }

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Every;
            // Ask the world where its landmarks are before reading the answer. Nothing else here
            // would: the counting pass asks, and that runs when the marker list is opened, so a
            // player who turns the switch on in the gear menu and never opens the list had an
            // empty answer -- every mark falling back to its kind's own picture, which is the
            // game's boss pin and Hildir's camp. That is a swap that draws the game's own
            // pictures on this mod's pins, and it is what both switches looked like when they
            // were reported as not working.
            WorldPins.Ask();
            var found = Gather(map);
            string now = Signature(found);
            if (now == _was) return;
            _was = now;
            Redraw(map, found);
        }

        /// <summary>
        /// Make sure the places have been gathered, for a marker list built before the first
        /// look. The list names them under their rows whether either switch is on or not, and a
        /// panel opened in the first two seconds of a session would otherwise have nothing to
        /// unfold those two rows onto.
        /// </summary>
        internal static void Ensure()
        {
            if (_marks.Count > 0) return;
            _next = 0f;
            Update();
        }

        /// <summary>Which of the two kinds this pin of the game's marks, or null if it marks neither.</summary>
        private static WorldPins.Kind? Marked(Minimap.PinData pin)
        {
            if (pin == null || ClientPins.IsOurs(pin)) return null;
            if (IsHildirMark(pin)) return WorldPins.Kind.Hildir;
            // The vegvisir pin for an altar somebody has read a stone for. It stands on the altar,
            // so where it is says which boss it is, the same as a landmark pin does.
            if (pin.m_type == Minimap.PinType.Boss) return WorldPins.Kind.Boss;
            if (pin.m_type != Minimap.PinType.None) return null;
            var kind = WorldPins.KindAt(pin.m_pos);
            return kind == WorldPins.Kind.Boss || kind == WorldPins.Kind.Hildir ? kind : (WorldPins.Kind?)null;
        }

        /// <summary>
        /// Every place the game has marked of these two kinds, one entry per place however many
        /// pins the game has put on it -- an altar is marked twice where somebody has read its
        /// vegvisir, and one marker of ours is enough for a place.
        /// </summary>
        private static List<Mark> Gather(Minimap map)
        {
            var found = new List<Mark>();
            Dictionary<string, string> byName = null;
            foreach (var theirs in Access.Pins(map))
            {
                var kind = Marked(theirs);
                if (!kind.HasValue) continue;
                bool had = false;
                foreach (var mark in found)
                    if (Utils.DistanceXZ(mark.Pos, theirs.m_pos) < Near) { had = true; break; }
                if (had) continue;
                string where = WorldPins.LocationAt(theirs.m_pos);
                if (string.IsNullOrEmpty(where))
                {
                    if (byName == null) byName = PlacesByName();   // once, and only where one is needed
                    where = NamedPlace(theirs, kind.Value, byName);
                }
                if (string.IsNullOrEmpty(where) && Sites().TryGetValue(theirs.m_type, out var byType))
                    where = byType;
                var one = new Mark
                {
                    Pos = theirs.m_pos,
                    Kind = kind.Value,
                    Location = where,
                    Icon = IconRegistry.Normalize(IconFor(where, kind.Value)),
                    Name = NameFor(theirs, where),
                };
                found.Add(one);
            }
            Census(found);
            return found;
        }

        private static Dictionary<string, string> _byName;

        /// <summary>
        /// The catalog's places for these two kinds, by the name they are written under. For
        /// reading a pin the world cannot account for: see <see cref="NamedPlace"/>.
        /// </summary>
        private static Dictionary<string, string> PlacesByName()
        {
            // Kept, because the legend it is read from resolves every catalog line's picture as
            // it builds -- a prefab lookup per line -- and this is asked on a poll. Thrown away
            // when the catalog changes, which is the only thing that can change the answer.
            if (_byName != null) return _byName;
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Catalog.Legend())
            {
                if (row.Cat != Category.BossAltar && row.Cat != Category.HildirQuest) continue;
                string key = (int)row.Cat + "|" + (row.Name ?? "");
                if (!byName.ContainsKey(key)) byName[key] = row.Prefab.TrimEnd('*');
            }
            _byName = byName;
            return byName;
        }

        /// <summary>The catalog changed: what a place is called and what it wears come from it.</summary>
        internal static void ForgetCatalog()
        {
            _byName = null;
            Forget();
        }

        /// <summary>
        /// Which place this pin is for, read off the name the game gave it.
        ///
        /// <para>For a pin standing where this world knows of no location at all, which is not
        /// the odd case it sounds like: Valheim saves these pins in the character file rather
        /// than in the world, so a character that has played more than one world carries every
        /// boss stone it has ever read and every errand Hildir has ever sent it on, at
        /// coordinates belonging to a world that is not this one. The game draws them here all
        /// the same, so this mod has to answer for them -- and the name the game gave the pin is
        /// the only thing left that says which altar it was.</para>
        /// </summary>
        private static string NamedPlace(Minimap.PinData pin, WorldPins.Kind kind, Dictionary<string, string> byName)
        {
            string name = pin.m_name ?? "";
            if (name.Length == 0) return null;
            if (Localization.instance != null) name = Localization.instance.Localize(name);
            if (name.Length == 0 || name.StartsWith("[")) return null;
            var cat = kind == WorldPins.Kind.Boss ? Category.BossAltar : Category.HildirQuest;
            return byName.TryGetValue((int)cat + "|" + name, out var prefab) ? prefab : null;
        }

        private static bool _counted;
        private static float _born;

        /// <summary>How long after the first look the map is taken to have arrived.</summary>
        private const float Settle = 20f;

        /// <summary>
        /// Say once a session what the game has marked and how much of it this mod could name.
        ///
        /// <para>Because the two switches draw from this and nothing else, and a marker wearing
        /// the wrong picture looks the same whether the place was misread or the picture was. The
        /// first report of them was that the custom boss markers did not work, and the answer was
        /// in neither the pictures nor the switches but in thirteen pins that had outlived the
        /// world they were made in.</para>
        /// </summary>
        private static void Census(List<Mark> found)
        {
            if (_counted || found.Count == 0) return;
            // Not at the first look. The game puts its landmark pins up a few seconds after a
            // player lands, so a census taken at once describes a map that is still arriving --
            // the first one written said nothing was standing where the world knew of a
            // location, because the world had not drawn any of them yet.
            if (_born == 0f) { _born = Time.unscaledTime; return; }
            if (Time.unscaledTime - _born < Settle) return;
            _counted = true;
            int bosses = 0, hers = 0, nameless = 0;
            var said = new List<string>();
            foreach (var mark in found)
            {
                if (mark.Kind == WorldPins.Kind.Boss) bosses++; else hers++;
                if (string.IsNullOrEmpty(mark.Location)) nameless++;
                said.Add((string.IsNullOrEmpty(mark.Name) ? "?" : mark.Name)
                         + (string.IsNullOrEmpty(mark.Location) ? "*" : ""));
            }
            said.Sort(StringComparer.OrdinalIgnoreCase);
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] The game marks {found.Count} place(s) of its own on this map: {bosses} boss " +
                $"altar(s) and {hers} of Hildir's. " + string.Join(", ", said.ToArray()) + ". " +
                (nameless > 0
                    ? $"The {nameless} marked * stand where this world knows of no location and could not be read " +
                      "from the pin either, so they wear their kind's own picture. Valheim saves these pins in your " +
                      "character file rather than in the world, so one made in another world still shows up here."
                    : "Every one of them was named."));
        }

        private static Dictionary<Minimap.PinType, string> _sites;

        /// <summary>
        /// Which location each of the game's own pin types means, read off the things that put
        /// those pins up: a vegvisir and a rune stone each carry the location they point at and
        /// the pin type they draw it with. Only the types that mean exactly one location are
        /// kept -- every boss stone in the game draws PinType.Boss, so that one says nothing,
        /// while Hildir's three have a type apiece and are the reason this exists.
        ///
        /// <para>Read from the game's own prefabs rather than written down here: her three sites
        /// are prefab data, and which of her quests is which is not something to guess at from
        /// the order of a list.</para>
        /// </summary>
        private static Dictionary<Minimap.PinType, string> Sites()
        {
            if (_sites != null) return _sites;
            var found = new Dictionary<Minimap.PinType, string>();
            _from.Clear();
            // Nothing is loaded before the world is, and caching an empty answer would keep it
            // for the session.
            var scene = ZNetScene.instance;
            if (scene == null) return found;
            var shared = new HashSet<Minimap.PinType>();
            try
            {
                // The game's own list of everything it can spawn, as well as whatever is loaded.
                // Both, because missing one stone is worse than missing all of them: every boss
                // stone in the game draws PinType.Boss, and it takes two of them in hand to know
                // that. See one and the type looks like it means that one boss, which is how
                // thirteen altars came to be named Eikthyr.
                foreach (var prefab in scene.m_prefabs)
                {
                    if (prefab == null) continue;
                    Read(prefab.GetComponent<Vegvisir>(), found, shared);
                    Read(prefab.GetComponent<RuneStone>(), found, shared);
                }
                foreach (var stone in Resources.FindObjectsOfTypeAll<Vegvisir>()) Read(stone, found, shared);
                foreach (var stone in Resources.FindObjectsOfTypeAll<RuneStone>()) Read(stone, found, shared);
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] Could not read which location the game's own pin types mean: {e.Message}");
                return found;
            }
            foreach (var type in shared) found.Remove(type);
            _sites = found;
            if (found.Count > 0)
            {
                var said = new List<string>();
                foreach (var kv in found) said.Add(kv.Key + "=" + kv.Value);
                said.Sort(StringComparer.Ordinal);
                TheGreatestMapMod.Log.LogInfo(
                    "[TheGreatestMap] The game's own pin types mean these locations, read off the stones and tables " +
                    "that put them up: " + string.Join(", ", said.ToArray()) + ". Used to name a place the world's " +
                    "location list cannot account for, which is any of Hildir's errands nobody has walked into.");
            }
            return found;
        }

        private static void Read(Vegvisir stone, Dictionary<Minimap.PinType, string> found,
                                 HashSet<Minimap.PinType> shared)
        {
            if (stone == null || stone.m_locations == null) return;
            foreach (var one in stone.m_locations)
                if (one != null) Note(found, shared, one.m_pinType, one.m_locationName, stone.name);
        }

        private static void Read(RuneStone stone, Dictionary<Minimap.PinType, string> found,
                                 HashSet<Minimap.PinType> shared)
        {
            if (stone != null) Note(found, shared, stone.m_pinType, stone.m_locationName, stone.name);
        }

        /// <summary>
        /// One stone's answer. A type is kept only while one thing in the game draws it: the
        /// moment a second stone claims the same type, that type says nothing about which place a
        /// pin of it is for -- whether or not the two name the same location. The same stone
        /// found twice, once in the game's spawn list and once loaded, is told by its name.
        /// </summary>
        private static void Note(Dictionary<Minimap.PinType, string> found, HashSet<Minimap.PinType> shared,
                                 Minimap.PinType type, string location, string stone)
        {
            if (string.IsNullOrEmpty(location) || type == Minimap.PinType.None) return;
            string mark = Itself(stone) + "/" + location;
            if (_from.TryGetValue(type, out var had))
            {
                if (!string.Equals(had, mark, StringComparison.OrdinalIgnoreCase)) shared.Add(type);
                return;
            }
            _from[type] = mark;
            found[type] = location;
        }

        /// <summary>Which stone each type's answer came from, so the same stone twice is not two stones.</summary>
        private static readonly Dictionary<Minimap.PinType, string> _from = new Dictionary<Minimap.PinType, string>();

        /// <summary>
        /// The thing itself, rather than the copy of it standing in the world. Unity names a copy
        /// after the prefab it came from and adds "(Clone)", and this pass looks at both lists --
        /// so Hildir's table was two tables claiming the same pin type, and a type claimed twice
        /// is one this cannot read. That left her quest pins with nothing to name them.
        /// </summary>
        private static string Itself(string stone)
        {
            string name = stone ?? "";
            int at;
            while ((at = name.IndexOf("(Clone)", StringComparison.Ordinal)) >= 0)
                name = name.Remove(at, 7);
            return name.Trim();
        }

        /// <summary>What the drawing would be made of: the places, their pictures, and what is drawn.</summary>
        private static string Signature(List<Mark> found)
        {
            var sb = new StringBuilder();
            foreach (var mark in found)
            {
                sb.Append('|').Append((int)mark.Pos.x).Append(',').Append((int)mark.Pos.z)
                  .Append(':').Append(mark.Icon)
                  .Append(Drawing(mark) ? '+' : '-');
            }
            return sb.ToString();
        }

        /// <summary>
        /// The picture for the place this pin marks: the catalog's, by the location underneath.
        /// Its own kind's icon where nothing can say what it is standing on -- a location added
        /// by another mod, or one this mod's catalog has no line for.
        /// </summary>
        private static string IconFor(string where, WorldPins.Kind kind)
        {
            string icon = Catalog.IconFor(where);
            if (!string.IsNullOrEmpty(icon)) return icon;
            var cat = kind == WorldPins.Kind.Boss ? Category.BossAltar : Category.HildirQuest;
            return TgmConfig.CategoryIcon != null && TgmConfig.CategoryIcon.TryGetValue(cat, out var cfg)
                   && !string.IsNullOrEmpty(cfg.Value)
                ? cfg.Value
                : Categories.DefaultIcon(cat);
        }

        /// <summary>The catalog's name for the place, or the game's own name for its pin.</summary>
        private static string NameFor(Minimap.PinData pin, string where)
        {
            string name = Catalog.NameForSource(where);
            if (!string.IsNullOrEmpty(name)) return name;
            name = pin.m_name ?? "";
            return Localization.instance != null && name.Length > 0 ? Localization.instance.Localize(name) : name;
        }

        private static void Redraw(Minimap map, List<Mark> found)
        {
            var pins = Access.Pins(map);
            foreach (var mine in _drawn) if (mine != null && pins.Contains(mine)) map.RemovePin(mine);
            _drawn.Clear();
            _marks.Clear();
            _marks.AddRange(found);

            foreach (var mark in _marks)
            {
                if (!Drawing(mark)) continue;
                int type = IconRegistry.TypeFor(mark.Icon);
                var pin = map.AddPin(mark.Pos, (Minimap.PinType)type, mark.Name, false, false, 0L);
                if (pin == null) continue;
                mark.Pin = pin;
                _drawn.Add(pin);
            }
            ClientPins.Restyle();
        }
    }
}

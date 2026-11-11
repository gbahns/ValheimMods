using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// The landmarks the game draws for the world, told apart.
    ///
    /// <para>Vanilla marks a handful of places by itself as you find them -- the boss altars, the
    /// traders, Hildir's three quest sites, the center of the world -- and every one of those is
    /// a <c>PinType.None</c> pin, rebuilt every five seconds from the locations the world knows.
    /// One pin type between them means vanilla's own filter cannot tell them apart, so the marker
    /// list had one row called Landmarks standing for the lot: eleven pins, four different
    /// questions, one switch.</para>
    ///
    /// <para>Worse, two of those questions already had rows of their own. "Traders" named the
    /// trader pin and could not hide it; Landmarks could hide it and did not say it was a trader.
    /// So each landmark is classified here by the location it stands on, and the row that is
    /// already about that thing takes it over: one row per thing, which is the whole point of the
    /// list.</para>
    ///
    /// <para><b>Hidden one pin at a time</b>, not through vanilla's filter: the filter is one
    /// switch for every <c>None</c> pin and these are four separate answers, so it cannot express
    /// them. That is the same call <see cref="ClientPins.AfterLayoutMaster"/> makes for the pins a
    /// player placed, and it has the same shape -- re-asserted after each layout, touching only a
    /// pin whose state is wrong, because layout runs on every move of the map.</para>
    /// </summary>
    internal static class WorldPins
    {
        /// <summary>What a landmark is. One row each, except Boss and Trader, which join the rows the list already had.</summary>
        internal enum Kind
        {
            /// <summary>Where the world begins, which the game marks from the first minute.</summary>
            Center,
            /// <summary>A boss altar, the stones you offer at.</summary>
            Boss,
            /// <summary>Haldor, Hildir, the Bog Witch: whoever will trade with you.</summary>
            Trader,
            /// <summary>The three places Hildir sends you, marked once she has asked.</summary>
            Hildir,
            /// <summary>Anything else the world draws a pin for, this mod included -- another mod's location, or one Valheim adds next.</summary>
            Other,
        }

        internal static readonly Kind[] All = (Kind[])Enum.GetValues(typeof(Kind));

        /// <summary>The rows these have of their own; the rest are drawn by their kinds' rows instead.</summary>
        internal static readonly Kind[] OwnRows = { Kind.Center, Kind.Other };

        /// <summary>
        /// The location the world's center stands on. In code rather than in the catalog because
        /// nothing is recorded for it and nothing ever will be: there is exactly one, the game
        /// draws it, and all this mod does is give it a name and a switch.
        /// </summary>
        private const string CenterLocation = "StartTemple";

        /// <summary>
        /// Hildir's quest sites. Her own camp is a trader and comes from the Traders catalog line;
        /// these three are the places she sends you, and they are matched by name as well as by
        /// this list -- anything whose location name begins with "Hildir" and is not a trader is
        /// one of hers, so a name Valheim changes or adds still lands in the right row.
        /// </summary>
        private static readonly string[] HildirLocations =
            { "Hildir_cave", "Hildir_crypt", "Hildir_plainsfortress" };

        private static readonly Dictionary<Kind, int> _counts = new Dictionary<Kind, int>();
        private static readonly Dictionary<Kind, List<Vector3>> _spots = new Dictionary<Kind, List<Vector3>>();

        /// <summary>Where each landmark the world knows about is, and what it is. Read from the world, not from the pins.</summary>
        private static readonly Dictionary<Vector3, Kind> _world = new Dictionary<Vector3, Kind>();

        /// <summary>
        /// And which location each one is, kept because a kind is not enough to draw with: the
        /// catalog names Eikthyrnir as Eikthyr and gives it his trophy, and that answer is the
        /// player's to edit. One dictionary rather than two lookups of the same walk.
        /// </summary>
        private static readonly Dictionary<Vector3, string> _where = new Dictionary<Vector3, string>();
        private static float _asked;
        private static HashSet<Kind> _hidden;

        /// <summary>How close a pin has to be to a location to be that location's pin.</summary>
        private const float Near = 1f;

        /// <summary>
        /// How often the world is asked where its landmarks are. Vanilla rebuilds these pins
        /// every five seconds from the same source, so there is nothing to be gained by asking
        /// faster, and the answer walks over every location the world has spawned.
        /// </summary>
        private const float AskEvery = 5f;

        internal static void Forget()
        {
            _world.Clear();
            _where.Clear();
            _asked = 0f;
            _counts.Clear();
            _spots.Clear();
        }

        /// <summary>Forget the parsed list of hidden landmarks, after the setting changes.</summary>
        internal static void ForgetHidden() => _hidden = null;

        /// <summary>
        /// Ask the game to lay its pins out again, which is what puts a landmark back or takes it
        /// away: these are vanilla's own pins, so the pass that hides them runs after vanilla has
        /// drawn them, and nothing happens until vanilla is told the layout is stale.
        /// </summary>
        internal static void Redraw()
        {
            var map = Minimap.instance;
            if (map != null) Access.PinUpdateRequired(map) = true;
        }

        /// <summary>
        /// Put the landmarks right now, without asking the game to lay the map out again.
        ///
        /// <para>Because asking is expensive in a way a switch must not be: a layout destroys and
        /// rebuilds the marker of every pin on the map, and a well-used map holds thousands --
        /// one to two seconds of it, for a row that hides eleven pins. A player toggling Hildir's
        /// errands noticed it at once, and the reason the kind rows above it felt instant is that
        /// they never asked for a layout at all.</para>
        ///
        /// <para>Nothing here needs one. Hiding a landmark switches its marker off and showing it
        /// switches the same marker back on; the object is still there either way. The one case
        /// that does need the game is vanilla's own icon filter, which destroys what it hides --
        /// and that is asked for separately, where it is used.</para>
        /// </summary>
        internal static void Apply() => AfterLayout(Minimap.instance);

        // ── what the world holds ────────────────────────────────────────────────

        /// <summary>
        /// Ask the world where its landmarks are, and what each one is. The same call vanilla
        /// makes to decide which of these pins to draw, so our answer and its pins cannot
        /// disagree about what is out there.
        /// </summary>
        private static void AskTheWorld()
        {
            if (Time.unscaledTime - _asked < AskEvery && _world.Count > 0) return;
            _asked = Time.unscaledTime;
            var zone = ZoneSystem.instance;
            if (zone == null) return;
            var icons = new Dictionary<Vector3, string>();
            try { zone.GetLocationIcons(icons); }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not read the world's location icons: {e.Message}");
                return;
            }
            _world.Clear();
            _where.Clear();
            foreach (var kv in icons)
            {
                _world[kv.Key] = Classify(kv.Value);
                _where[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// Make sure the world has been asked. Called by anything that reads the answer without
        /// having counted first -- the asking is rate-limited inside, so asking is nearly free
        /// and not asking leaves an empty answer for a player who never opened the list.
        /// </summary>
        internal static void Ask() => AskTheWorld();

        /// <summary>
        /// Every landmark the world knows of, and which location each one is. For anything that
        /// has to walk the whole list rather than ask about one spot.
        /// </summary>
        internal static IEnumerable<KeyValuePair<Vector3, string>> Known()
        {
            AskTheWorld();
            return _where;
        }

        /// <summary>What kind of landmark stands at this spot, if the world knows of one.</summary>
        internal static Kind? KindAt(Vector3 pos)
        {
            foreach (var kv in _world)
                if (Geo.FlatDistance(kv.Key, pos) < Near) return kv.Value;
            return null;
        }

        /// <summary>
        /// Whether the world marks a landmark of its own anywhere near here. A wider question
        /// than <see cref="KindAt"/>, which asks whether a pin is that landmark's pin: this one
        /// is asked of a marker a player dropped by hand, and a hand is not accurate to a meter.
        /// </summary>
        internal static bool MarkedWithin(Vector3 pos, float radius)
        {
            AskTheWorld();
            foreach (var kv in _world)
                if (Geo.FlatDistance(kv.Key, pos) < radius) return true;
            return false;
        }

        /// <summary>The location standing at this spot, for the catalog to name and picture.</summary>
        internal static string LocationAt(Vector3 pos)
        {
            foreach (var kv in _where)
                if (Geo.FlatDistance(kv.Key, pos) < Near) return kv.Value;
            return null;
        }

        /// <summary>
        /// What kind of landmark a location is. The catalog answers for the altars and the
        /// traders, which is the same line that says which of the world's locations those are
        /// anywhere else in this mod -- so editing a Catalog line moves these rows with it.
        /// </summary>
        private static Kind Classify(string location)
        {
            if (string.IsNullOrEmpty(location)) return Kind.Other;
            if (location.Equals(CenterLocation, StringComparison.OrdinalIgnoreCase)) return Kind.Center;
            foreach (var row in Catalog.Legend())
            {
                if (!string.Equals(row.Prefab.TrimEnd('*'), location, StringComparison.OrdinalIgnoreCase)) continue;
                if (row.Cat == Category.BossAltar) return Kind.Boss;
                if (row.Cat == Category.Trader) return Kind.Trader;
                break;
            }
            foreach (var one in HildirLocations)
                if (location.Equals(one, StringComparison.OrdinalIgnoreCase)) return Kind.Hildir;
            // Her camp is a trader and was caught above; anything else of hers is somewhere she
            // sends you, whatever Valheim has called it this year.
            if (location.StartsWith("Hildir", StringComparison.OrdinalIgnoreCase)) return Kind.Hildir;
            return Kind.Other;
        }

        /// <summary>What kind of landmark this pin is, or null if it is not one of the world's.</summary>
        private static Kind? KindOf(Minimap.PinData pin)
        {
            if (pin == null) return null;
            // Hildir's three are marked by a route of their own: a saved pin wearing a pin type
            // per quest, put up when she asks, named $hud_pin_hildir1 and so on. That is not the
            // landmark mechanism below, which is one None pin per location the world draws an
            // icon for, and only that half was being claimed -- so the row counted a fraction of
            // the question marks on the map and its switch hid that same fraction, which is what
            // a player meant by three of six tracked. Claimed by type, with no location to stand
            // on: nothing but Hildir's quests ever wears one of these three.
            if (pin.m_type == Minimap.PinType.Hildir1 || pin.m_type == Minimap.PinType.Hildir2
                || pin.m_type == Minimap.PinType.Hildir3) return Kind.Hildir;
            if (pin.m_type != Minimap.PinType.None) return null;
            foreach (var kv in _world)
                if (Geo.FlatDistance(kv.Key, pin.m_pos) < Near) return kv.Value;
            // A None pin standing on no location this world knows about: somebody else's, and
            // ours to leave alone rather than to claim.
            return null;
        }

        // ── counting ────────────────────────────────────────────────────────────

        /// <summary>
        /// Count the world's landmarks by what they are. Read when the marker list is built and
        /// on every refresh, which is per press rather than per frame.
        /// </summary>
        internal static void Recount(Minimap map)
        {
            _counts.Clear();
            _spots.Clear();
            if (map == null) return;
            AskTheWorld();
            foreach (var pin in Access.Pins(map))
            {
                var kind = KindOf(pin);
                if (!kind.HasValue) continue;
                if (!_spots.TryGetValue(kind.Value, out var where)) _spots[kind.Value] = where = new List<Vector3>();
                // One count per place rather than per pin. The game marks one place more than
                // once: an altar carries a landmark pin and a vegvisir pin, and each of Hildir's
                // sites carries a landmark pin and the quest pin her table put up. Counting pins
                // made a row say nine for three errands, which is a number a player cannot check
                // against anything -- and it would not have added up against the places listed
                // under it either.
                bool had = false;
                foreach (var spot in where)
                    if (Geo.FlatDistance(spot, pin.m_pos) < Near) { had = true; break; }
                if (had) continue;
                where.Add(pin.m_pos);
                _counts.TryGetValue(kind.Value, out int n);
                _counts[kind.Value] = n + 1;
            }
        }

        internal static int CountOf(Kind kind)
        {
            _counts.TryGetValue(kind, out int n);
            return n;
        }

        internal static int ShownCount(Kind kind) => Wanted(kind) ? CountOf(kind) : 0;

        /// <summary>Where the ones on the map are, for a ping.</summary>
        internal static List<Vector3> Spots(Kind kind)
        {
            return _spots.TryGetValue(kind, out var where) ? where : new List<Vector3>();
        }

        // ── showing and hiding ──────────────────────────────────────────────────

        /// <summary>
        /// Whether this mod draws its own markers in place of the game's, for the two kinds the
        /// game marks by itself and this mod can mark better.
        ///
        /// <para>One switch each, because the alternative is two -- ours and the game's -- and
        /// two switches for one question lets a player ask for both at once, which is the same
        /// place marked twice and reads as a bug. Here rather than in the drawing code so that
        /// every side of the swap reads it from one place.</para>
        /// </summary>
        internal static bool CustomBoss =>
            TgmConfig.CustomBossMarkers != null && TgmConfig.CustomBossMarkers.Value;

        internal static bool CustomHildir =>
            TgmConfig.CustomHildirMarkers != null && TgmConfig.CustomHildirMarkers.Value;

        /// <summary>
        /// Whether this kind is shown at all, which is what its row's switch says. Not the same
        /// question as whether the game's own pins for it are drawn: with the swap on, this kind
        /// is shown and what shows is this mod's.
        /// </summary>
        internal static bool Wanted(Kind kind) => !Hidden().Contains(kind);

        /// <summary>Whether the game's own pins for this kind are drawn.</summary>
        internal static bool Drawn(Kind kind)
        {
            if (!Wanted(kind)) return false;
            if (kind == Kind.Boss && CustomBoss) return false;
            if (kind == Kind.Hildir && CustomHildir) return false;
            return true;
        }

        internal static void SetDrawn(Kind kind, bool draw)
        {
            var set = new HashSet<Kind>(Hidden());
            if (draw) set.Remove(kind); else set.Add(kind);
            Save(set);
        }

        internal static void Toggle(Kind kind) => SetDrawn(kind, !Drawn(kind));

        internal static int HiddenCount() => Hidden().Count;

        /// <summary>
        /// Put the hidden ones away again after a layout, and the shown ones back.
        ///
        /// <para>Only a pin whose state is wrong is touched. This runs on every move of the map,
        /// and SetActive on a few thousand objects a frame is what stalled this client into
        /// dropped connections once before -- the landmarks are a dozen pins, but the loop that
        /// finds them walks all of them, so it does nothing at all while nothing is hidden.</para>
        /// </summary>
        internal static void AfterLayout(Minimap map)
        {
            if (map == null) return;
            // The game's vegvisir pins are a type of their own rather than landmarks, so they are
            // put away through vanilla's own filter. Re-asserted here, with the rest, because
            // anything that touches that array -- vanilla's icon button, a row in this mod's own
            // list -- would otherwise bring them back beside ours.
            if (CustomBoss) VanillaPins.SetDrawn(map, Minimap.PinType.Boss, false);
            bool any = Hidden().Count > 0 || CustomBoss || CustomHildir || OwnMarks.AnySiteHidden;
            if (!any && !_wasHiding) return;
            _wasHiding = any;
            foreach (var pin in Access.Pins(map))
            {
                if (pin == null || pin.m_uiElement == null) continue;
                var kind = KindOf(pin);
                if (!kind.HasValue)
                {
                    // A vegvisir pin is an altar with a type of its own, so it is not a landmark
                    // and KindOf has nothing to say about it -- but the type row for one altar
                    // does, and vanilla's filter is one switch for every altar at once.
                    //
                    // Shown as well as hidden. Whether the type is drawn at all is still
                    // vanilla's filter's to say and this does not argue with it: a pin that
                    // filter has put away is destroyed rather than hidden, and a destroyed one
                    // has no element for the line above to have let through. Hiding without
                    // showing is what left a row that had been switched off and on again with
                    // its names back and no pictures under them -- nothing else would ever have
                    // put those back.
                    if (pin.m_type == Minimap.PinType.Boss && !ClientPins.IsOurs(pin))
                        ClientPins.ShowMarker(pin, OwnMarks.SiteShown(pin));
                    continue;
                }
                ClientPins.ShowMarker(pin, Drawn(kind.Value) && OwnMarks.SiteShown(pin));
            }
        }

        private static bool _wasHiding;

        private static HashSet<Kind> Hidden()
        {
            if (_hidden != null) return _hidden;
            var set = new HashSet<Kind>();
            foreach (var raw in ((TgmConfig.HiddenLandmarks != null ? TgmConfig.HiddenLandmarks.Value : null) ?? "").Split(','))
            {
                string one = raw.Trim();
                if (one.Length == 0) continue;
                if (Enum.TryParse(one, true, out Kind kind)) set.Add(kind);
            }
            _hidden = set;
            return set;
        }

        private static void Save(HashSet<Kind> set)
        {
            if (TgmConfig.HiddenLandmarks == null) return;
            var list = new List<string>();
            foreach (var kind in All) if (set.Contains(kind)) list.Add(kind.ToString());
            string line = string.Join(",", list.ToArray());
            if (!string.Equals(line, TgmConfig.HiddenLandmarks.Value, StringComparison.Ordinal))
                TgmConfig.HiddenLandmarks.Value = line;
        }

        /// <summary>Show or hide every kind of landmark at once, for the heading over them.</summary>
        internal static void SetAll(bool draw)
        {
            var set = new HashSet<Kind>();
            foreach (var one in All)
            {
                // Only the ones there are any of: writing a kind nobody has would leave the
                // heading's switch saying hide over a world with no such landmark in it. One
                // already hidden with none of it about keeps its setting, this press having had
                // nothing to say about it.
                bool has = CountOf(one) > 0;
                if (has ? !draw : !Drawn(one)) set.Add(one);
            }
            Save(set);
        }

        /// <summary>How many of the world's landmarks there are, and how many are drawn.</summary>
        // There was a Tally here for the Places heading to print. It counted each landmark
        // once, while the altar and errand rows beneath it counted the records and the vegvisir
        // pins as well, so the heading and its rows were answering different questions. The
        // heading adds up its own rows now: see KindMenu._rowCounts.

        internal static bool AnyShown()
        {
            foreach (var kind in All) if (CountOf(kind) > 0 && Drawn(kind)) return true;
            return false;
        }

        /// <summary>Show every one of them again, for the row that puts everything back.</summary>
        internal static void ShowAll()
        {
            if (TgmConfig.HiddenLandmarks != null && !string.IsNullOrEmpty(TgmConfig.HiddenLandmarks.Value))
                TgmConfig.HiddenLandmarks.Value = "";
        }

        // ── names and pictures ──────────────────────────────────────────────────

        /// <summary>
        /// The one location every landmark in this bucket is, or null when they are several
        /// things or none.
        ///
        /// <para>For the leftovers row. "Landmarks" is an honest name for a bag of things this
        /// mod cannot tell apart, and a poor one for a bag holding exactly one thing the world
        /// has both a name and a picture for -- which is what it usually holds, since everything
        /// this mod does know about has a row of its own by now.</para>
        /// </summary>
        private static string OnlyLocation(Kind kind)
        {
            string only = null;
            foreach (var kv in _world)
            {
                if (kv.Value != kind) continue;
                if (!_where.TryGetValue(kv.Key, out string where) || string.IsNullOrEmpty(where)) return null;
                if (only == null) only = where;
                else if (!string.Equals(only, where, StringComparison.OrdinalIgnoreCase)) return null;
            }
            return only;
        }

        internal static string Label(Kind kind)
        {
            switch (kind)
            {
                case Kind.Center:  return "World Center";
                case Kind.Boss:    return Categories.Label(Category.BossAltar);
                case Kind.Trader:  return Categories.Label(Category.Trader);
                case Kind.Hildir:  return "Hildir's Quests";
                default:
                    string one = OnlyLocation(kind);
                    if (string.IsNullOrEmpty(one)) return "Landmarks";
                    return Catalog.NameForSource(one) ?? Catalog.Prettify(one);
            }
        }

        internal static string IconKey(Kind kind)
        {
            switch (kind)
            {
                // The game's own picture for that location, which is the one already on the map
                // beside it -- there is no item meaning "the middle of the world", and a plain
                // dot would have been this row pretending it did not know.
                case Kind.Center:  return "loc:" + CenterLocation;
                case Kind.Boss:    return Categories.DefaultIcon(Category.BossAltar);
                case Kind.Trader:  return Categories.DefaultIcon(Category.Trader);
                // Her quests are the three locked chests, and the key is the thing you carry
                // around looking for them.
                case Kind.Hildir:  return IconRegistry.PickItem("HildirKey_forestcrypt", "Coins") ?? "Coins";
                default:
                    // The game's own picture for it, for the same reason the world center uses
                    // one: the pin is already on the map wearing it, and a plain dot beside it
                    // is the row saying it does not know what its own marker is.
                    string one = OnlyLocation(kind);
                    return string.IsNullOrEmpty(one) ? "pin:Icon3" : "loc:" + one;
            }
        }

        internal static string Tip(Kind kind)
        {
            switch (kind)
            {
                case Kind.Center:
                    return "The middle of the world, which the game marks from your first minute in it. One pin, and " +
                           "this is the only switch there is for it.";
                case Kind.Hildir:
                    return "The three places Hildir sends you, which the game marks once she has asked. Hidden one " +
                           "pin at a time, since the game draws all its landmarks as one kind of pin and offers no " +
                           "button for any of them.";
                default:
                    string one = OnlyLocation(kind);
                    if (!string.IsNullOrEmpty(one))
                        return $"A landmark the game draws for this world that this mod has no row of its own for " +
                               $"({one}) -- another mod's, or one Valheim has added since. It is here so it has a " +
                               "switch, wearing the picture the game gave it.";
                    return "Landmarks the game draws for this world that this mod cannot name -- another mod's, or " +
                           "one Valheim has added since. They are here so they have a switch, and named for what " +
                           "they are rather than guessed at.";
            }
        }
    }
}

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

        /// <summary>The rows these have of their own; Boss and Trader are drawn by their kinds' rows instead.</summary>
        internal static readonly Kind[] OwnRows = { Kind.Center, Kind.Hildir, Kind.Other };

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
            foreach (var kv in icons) _world[kv.Key] = Classify(kv.Value);
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
            if (pin == null || pin.m_type != Minimap.PinType.None) return null;
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
                _counts.TryGetValue(kind.Value, out int n);
                _counts[kind.Value] = n + 1;
                if (!_spots.TryGetValue(kind.Value, out var where)) _spots[kind.Value] = where = new List<Vector3>();
                where.Add(pin.m_pos);
            }
        }

        internal static int CountOf(Kind kind)
        {
            _counts.TryGetValue(kind, out int n);
            return n;
        }

        internal static int ShownCount(Kind kind) => Drawn(kind) ? CountOf(kind) : 0;

        /// <summary>Where the ones on the map are, for a ping.</summary>
        internal static List<Vector3> Spots(Kind kind)
        {
            return _spots.TryGetValue(kind, out var where) ? where : new List<Vector3>();
        }

        // ── showing and hiding ──────────────────────────────────────────────────

        internal static bool Drawn(Kind kind) => !Hidden().Contains(kind);

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
            bool any = Hidden().Count > 0;
            if (!any && !_wasHiding) return;
            _wasHiding = any;
            foreach (var pin in Access.Pins(map))
            {
                if (pin == null || pin.m_uiElement == null) continue;
                var kind = KindOf(pin);
                if (!kind.HasValue) continue;
                ClientPins.ShowMarker(pin, Drawn(kind.Value));
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
        internal static void Tally(out int all, out int shown)
        {
            all = 0;
            shown = 0;
            foreach (var kind in All)
            {
                all += CountOf(kind);
                shown += ShownCount(kind);
            }
        }

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

        internal static string Label(Kind kind)
        {
            switch (kind)
            {
                case Kind.Center:  return "World Center";
                case Kind.Boss:    return Categories.Label(Category.BossAltar);
                case Kind.Trader:  return Categories.Label(Category.Trader);
                case Kind.Hildir:  return "Hildir's Quests";
                default:           return "Landmarks";
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
                default:           return "pin:Icon3";
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
                    return "Landmarks the game draws for this world that this mod cannot name -- another mod's, or " +
                           "one Valheim has added since. They are here so they have a switch, and named for what " +
                           "they are rather than guessed at.";
            }
        }
    }
}
